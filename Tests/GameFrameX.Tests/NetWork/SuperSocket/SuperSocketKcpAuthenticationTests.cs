// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Message;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;
using GameFrameX.SuperSocket.Server;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Host;
using GameFrameX.SuperSocket.Server.Abstractions.Middleware;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.SuperSocket.Server.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameFrameX.Tests.NetWork.SuperSocket;

/// <summary>
/// SuperSocket KCP 首消息鉴权集成测试 / SuperSocket KCP first-message authentication integration test
/// </summary>
/// <remarks>
/// 覆盖 GFX-822（KCP 接入生产前补齐首消息鉴权防线）的验收标准：
/// 通过与 <c>AppStartUpByServer.ConfigureKcpServer</c> 相同的构建路径
/// （<c>MultipleServerHostBuilder.Create</c> + <c>AddServer&lt;IMessage, MessageObjectPipelineFilter&gt;</c>
/// + <c>UseKcp</c> + <c>UseMiddleware&lt;SessionAuthenticationMiddleware&gt;</c>）启动 KCP 服务器，
/// 用伪造 conv 的 UDP 洪泛制造未鉴权会话，验证鉴权超时窗口后服务端主动关闭全部会话（会话数受控、无泄漏）。
/// </remarks>
public class SuperSocketKcpAuthenticationTests : IAsyncLifetime
{
    private const int ForgedSessionCount = 16;

    private IHost _host;
    private int _kcpPort;
    private SessionAuthenticationMiddleware _authenticationMiddleware;

    public async Task InitializeAsync()
    {
        _kcpPort = GetAvailableUdpPort();
        var options = new SessionAuthenticationOptions
        {
            // 短超时 + 短扫描，保证集成测试周期可控（实际关闭最晚比 Timeout 晚一个扫描周期）
            Timeout = TimeSpan.FromMilliseconds(500),
            ScanInterval = TimeSpan.FromMilliseconds(100),
        };
        _authenticationMiddleware = new SessionAuthenticationMiddleware(options);

        var multipleServerHostBuilder = MultipleServerHostBuilder.Create();

        multipleServerHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
        {
            builder
                .UseKcp(o =>
                {
                    o.NoDelay = true;
                    o.Interval = 10;
                    o.Mtu = 1400;
                    o.IdleTimeout = 60000;
                })
                .UseSessionHandler(OnConnected, OnDisconnected)
                .UseInProcSessionContainer()
                // UseMiddleware 返回非泛型 ISuperSocketHostBuilder，须置于泛型链末尾
                .UseMiddleware<SessionAuthenticationMiddleware>(_ => _authenticationMiddleware)
                .ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        var listenOptions = new ListenOptions
                        {
                            Ip = IPAddress.Loopback.ToString(),
                            Port = _kcpPort,
                        };
                        options.AddListener(listenOptions);
                    });
                });
        });

        _host = multipleServerHostBuilder.Build();
        await _host.StartAsync();
    }

    public async Task DisposeAsync()
    {
        if (_host != null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
    }

    /// <summary>
    /// 伪造 conv 的 UDP 洪泛只会产生受鉴权超时约束的短命会话：超时窗口后全部被服务端主动关闭，追踪数归零、无泄漏。
    /// </summary>
    /// <remarks>
    /// KCP listener 收到未知 <c>RemoteEndPoint:Conv</c> 的任意 ≥24 字节（IKCP_OVERHEAD）包即建连；
    /// 从同一 UdpClient 以不同 conv 发包可制造多个未鉴权会话。会话建立后不发送任何鉴权消息，
    /// 验证鉴权超时扫描（<c>SessionAuthenticationMiddleware</c>）将其全部关闭。
    /// 同时证明挂载鉴权中间件后 KCP 服务器仍正常启动并监听（会话可建立即端口在监听）。
    /// </remarks>
    [Fact]
    public async Task KcpServer_ForgedConvFlood_UnauthenticatedSessionsShouldBeClosedAfterTimeout()
    {
        using var udpClient = new UdpClient();
        udpClient.Connect(IPAddress.Loopback, _kcpPort);

        // 洪泛：ForgedSessionCount 个不同 conv 的最小 KCP 头包（cmd=IKCP_CMD_PUSH，len=0）
        for (var i = 0; i < ForgedSessionCount; i++)
        {
            var datagram = CreateMinimalKcpDatagram(1000u + (uint)i);
            await udpClient.SendAsync(datagram, datagram.Length);
        }

        // 会话按 UDP 包到达异步建立：等待全部未鉴权会话进入鉴权追踪
        var allTracked = await WaitUntilAsync(() => _authenticationMiddleware.GetTrackedSessionCount() >= ForgedSessionCount, TimeSpan.FromSeconds(5));
        Assert.True(allTracked, $"Expected {ForgedSessionCount} tracked sessions, got {_authenticationMiddleware.GetTrackedSessionCount()}.");

        // 超时窗口（500ms）+ 扫描周期后，全部未鉴权会话被服务端主动关闭并注销
        var allClosed = await WaitUntilAsync(() => _authenticationMiddleware.GetTrackedSessionCount() == 0, TimeSpan.FromSeconds(10));
        Assert.True(allClosed, $"Expected all sessions closed after authentication timeout, still tracking {_authenticationMiddleware.GetTrackedSessionCount()}.");
    }

    /// <summary>
    /// 构造最小合法长度的 KCP 数据报（24 字节头：conv + cmd + frg + wnd + ts + sn + una + len）。
    /// </summary>
    /// <param name="conv">KCP 会话 Conv（每次洪泛取不同值以制造独立会话）/ The KCP conv</param>
    /// <returns>24 字节 KCP 头数据报 / The 24-byte KCP header datagram</returns>
    private static byte[] CreateMinimalKcpDatagram(uint conv)
    {
        var datagram = new byte[24];
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(0, 4), conv);
        datagram[4] = 81; // cmd = IKCP_CMD_PUSH
        datagram[5] = 0; // frg
        BinaryPrimitives.WriteUInt16LittleEndian(datagram.AsSpan(6, 2), 32); // wnd
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(8, 4), 0); // ts
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(12, 4), 0); // sn
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(16, 4), 0); // una
        BinaryPrimitives.WriteUInt32LittleEndian(datagram.AsSpan(20, 4), 0); // len
        return datagram;
    }

    /// <summary>
    /// 轮询等待条件成立或超时。
    /// </summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTimeOffset.UtcNow.Add(timeout);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }

        return condition();
    }

    /// <summary>
    /// 获取可用 UDP 端口。
    /// </summary>
    private static int GetAvailableUdpPort()
    {
        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
    }

    private static ValueTask OnConnected(IAppSession session)
    {
        return ValueTask.CompletedTask;
    }

    private static ValueTask OnDisconnected(IAppSession session, CloseEventArgs closeEventArgs)
    {
        return ValueTask.CompletedTask;
    }
}
