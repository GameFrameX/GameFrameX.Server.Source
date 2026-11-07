// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Net;
using System.Net.Sockets;
using System.Reflection;
using GameFrameX.Network.HTTP;
using GameFrameX.StartUp;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// HTTP 宿主 WebApplication 停机释放回归测试（GFX-1067）。
/// </summary>
/// <remarks>
/// Anchors both halves of the leak fix: the startup path must retain the built <c>WebApplication</c>
/// in the private <c>_webApplication</c> field, and the shutdown path (<c>StopServerAsync</c>) must
/// stop and dispose it so the HTTP port is deterministically released.
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> keeps these tests serialized with the
/// other runtime-state tests: <c>StopServerAsync</c> flips the process-wide <c>GameAppRuntime</c> running flag.
/// </remarks>
[Collection(GameAppRuntimeCollection.Name)]
public class AppStartUpHttpServerStopReleaseTests
{

    /// <summary>
    /// 启动路径保存 WebApplication 引用：StartHttpServerAsync 成功后字段非空、端口真实监听；停机后端口释放、字段置空。
    /// </summary>
    [Fact]
    public async Task StartHttpServerAsync_ShouldRetainWebApplicationAndReleaseOnStop()
    {
        var httpPort = GetAvailableHttpPort();
        var startUp = new HttpStopTestStartUp(CreateHttpEnabledSetting(httpPort));
        try
        {
            var startMethod = typeof(AppStartUpBase).GetMethod("StartHttpServerAsync", BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.NotNull(startMethod);

            await (Task)startMethod.Invoke(startUp, new object[] { new List<BaseHttpHandler>(), null, null, LogLevel.Debug });

            // A/B 判别锚点：旧代码构建后引用即丢（字段恒为 null），泄漏复现时此断言必失败
            var webApplication = GetWebApplicationField(startUp);
            Assert.NotNull(webApplication);
            Assert.True(await IsTcpPortAcceptingAsync(httpPort), $"HTTP 端口 {httpPort} 启动后应接受连接");
        }
        finally
        {
            await startUp.InvokeStopServerAsync();
        }

        Assert.Null(GetWebApplicationField(startUp));
        Assert.False(await IsTcpPortAcceptingAsync(httpPort), $"HTTP 端口 {httpPort} 停机后应拒绝连接（端口已释放）");
    }

    /// <summary>
    /// 停机路径停止并释放 HTTP 宿主：hosted service 收到一次停止、容器释放一次、字段置空，重复停机幂等。
    /// </summary>
    [Fact]
    public async Task StopServerAsync_ShouldStopAndDisposeHttpWebApplication_Idempotently()
    {
        var recordingService = new RecordingHostedService();
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        // 工厂注册（非实例注册）：容器只跟踪自己创建的服务，DisposeAsync 时才会释放该实例
        builder.Services.AddSingleton<IHostedService>(_ => recordingService);
        var application = builder.Build();
        await application.StartAsync();

        var startUp = new HttpStopTestStartUp(CreateHttpEnabledSetting(GetAvailableHttpPort()));
        SetWebApplicationField(startUp, application);

        await startUp.InvokeStopServerAsync();

        // A/B 判别锚点：旧代码从不触碰 HTTP 宿主（停止计数恒为 0），泄漏复现时必失败
        Assert.Equal(1, recordingService.StopCallCount);
        Assert.Equal(1, recordingService.DisposeCallCount);
        Assert.Null(GetWebApplicationField(startUp));

        // 幂等：字段已置空后再次停机不产生第二次停止/释放
        await startUp.InvokeStopServerAsync();
        Assert.Equal(1, recordingService.StopCallCount);
        Assert.Equal(1, recordingService.DisposeCallCount);
    }

    /// <summary>
    /// 构造 HTTP 启用且指向指定端口的设置（OTel 全关，避开 MetricsPort/HttpsPort 分支）。
    /// </summary>
    /// <param name="httpPort">HTTP 监听端口 / HTTP listening port</param>
    /// <returns>测试用应用设置 / Test application setting</returns>
    private static AppSetting CreateHttpEnabledSetting(int httpPort)
    {
        return new AppSetting
        {
            ServerType = "HttpStopTest",
            HttpPort = (ushort)httpPort,
            IsEnableHttp = true,
            IsOpenTelemetry = false,
            IsOpenTelemetryMetrics = false,
            IsOpenTelemetryTracing = false,
        };
    }

    /// <summary>
    /// 获取一个落在 HTTP 合法区间 [5000, 65534] 的临时可用端口。
    /// </summary>
    /// <returns>可用端口号 / An available port</returns>
    private static int GetAvailableHttpPort()
    {
        while (true)
        {
            using (var listener = new TcpListener(IPAddress.Loopback, 0))
            {
                listener.Start();
                var port = ((IPEndPoint)listener.LocalEndpoint).Port;
                listener.Stop();

                if (port >= 5000 && port <= ushort.MaxValue - 1)
                {
                    return port;
                }
            }
        }
    }

    /// <summary>
    /// 探测端口是否接受 TCP 连接（监听中 = true；连接被拒/无监听 = false）。
    /// </summary>
    /// <param name="port">目标端口 / Target port</param>
    /// <returns>是否接受连接 / Whether the port accepts connections</returns>
    private static async Task<bool> IsTcpPortAcceptingAsync(int port)
    {
        try
        {
            using (var tcpClient = new TcpClient())
            {
                await tcpClient.ConnectAsync(IPAddress.Loopback, port);
                return tcpClient.Connected;
            }
        }
        catch (SocketException)
        {
            return false;
        }
    }

    /// <summary>
    /// 读取 AppStartUpBase 的私有 _webApplication 字段。
    /// </summary>
    /// <param name="startUp">启动实例 / Startup instance</param>
    /// <returns>字段当前值（null = 未启动或已停机释放）/ Current field value (null = not started or already released)</returns>
    private static WebApplication GetWebApplicationField(AppStartUpBase startUp)
    {
        var field = typeof(AppStartUpBase).GetField("_webApplication", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        return (WebApplication)field.GetValue(startUp);
    }

    /// <summary>
    /// 向 AppStartUpBase 的私有 _webApplication 字段注入已启动宿主（停机路径单测装配）。
    /// </summary>
    /// <param name="startUp">启动实例 / Startup instance</param>
    /// <param name="application">已启动的 Web 应用程序 / The started web application</param>
    private static void SetWebApplicationField(AppStartUpBase startUp, WebApplication application)
    {
        var field = typeof(AppStartUpBase).GetField("_webApplication", BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(field);
        field.SetValue(startUp, application);
    }

    /// <summary>
    /// 录制型 hosted service：记录停止与释放调用次数，作为宿主 StopAsync/DisposeAsync 的行为探针。
    /// </summary>
    private sealed class RecordingHostedService : IHostedService, IDisposable
    {
        /// <summary>
        /// 停止调用次数。
        /// </summary>
        public int StopCallCount;

        /// <summary>
        /// 释放调用次数。
        /// </summary>
        public int DisposeCallCount;

        /// <summary>
        /// 记录启动调用（无操作语义，仅满足接口）。
        /// </summary>
        /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
        /// <returns>已完成的任务 / A completed task</returns>
        public Task StartAsync(CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 记录停止调用。
        /// </summary>
        /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
        /// <returns>已完成的任务 / A completed task</returns>
        public Task StopAsync(CancellationToken cancellationToken)
        {
            StopCallCount++;
            return Task.CompletedTask;
        }

        /// <summary>
        /// 记录释放调用。
        /// </summary>
        public void Dispose()
        {
            DisposeCallCount++;
        }
    }

    /// <summary>
    /// 测试用启动实例：仅装配 Setting 与停机路径访问器，不触碰进程级初始化。
    /// </summary>
    private sealed class HttpStopTestStartUp : AppStartUpBase
    {
        /// <summary>
        /// 以指定设置构造测试启动实例。
        /// </summary>
        /// <param name="setting">应用设置 / Application setting</param>
        public HttpStopTestStartUp(AppSetting setting)
        {
            Setting = setting;
        }

        /// <summary>
        /// 启动空实现（本测试只关注停机路径）。
        /// </summary>
        /// <returns>已完成的任务 / A completed task</returns>
        public override Task StartAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 暴露受保护的 StopServerAsync 供测试直接调用。
        /// </summary>
        /// <returns>停止任务 / The stop task</returns>
        public Task InvokeStopServerAsync()
        {
            return StopServerAsync();
        }
    }
}
