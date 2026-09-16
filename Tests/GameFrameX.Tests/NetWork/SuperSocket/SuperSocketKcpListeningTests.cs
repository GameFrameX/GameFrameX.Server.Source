// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Message;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;
using GameFrameX.SuperSocket.Server;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.SuperSocket.Server.Host;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameFrameX.Tests.NetWork.SuperSocket;

/// <summary>
/// SuperSocket KCP 服务器监听冒烟测试 / SuperSocket KCP server listening smoke test
/// </summary>
/// <remarks>
/// 覆盖 GFX-821（基于 GameFrameX.SuperSocket.Kcp 重写服务端 KCP 接入）的验收标准：
/// 通过与 <c>AppStartUpByServer.ConfigureKcpServer</c> 相同的构建路径
/// （<c>MultipleServerHostBuilder.Create</c> + <c>AddServer&lt;IMessage, MessageObjectPipelineFilter&gt;</c>
/// + <c>UseKcp</c>）启动 KCP 服务器，并用真实 UDP 客户端发送最小 KCP 头包，
/// 以会话建立回调被触发证明端口不仅可收包、整条 KCP 建连链路已就绪。
/// </remarks>
public class SuperSocketKcpListeningTests : IAsyncLifetime
{
    private IHost _host;
    private int _kcpPort;
    private readonly TaskCompletionSource<bool> _sessionConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

    public async Task InitializeAsync()
    {
        _kcpPort = GetAvailableUdpPort();

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
                .UsePackageHandler(PackageHandler)
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
    /// KCP 服务器启动后 UDP 端口应处于监听状态 / KCP server should listen on UDP port after startup
    /// </summary>
    /// <remarks>
    /// 用 UdpClient 真实发送一个 24 字节（IKCP_OVERHEAD）最小 KCP 头包：KcpConnectionListener 对未知
    /// <c>RemoteEndPoint:Conv</c> 的合法 KCP 包即建连，进而触发 <c>UseSessionHandler</c> 注册的 OnConnected 回调。
    /// 断言该回调在超时内触发——「发送成功即视为已 listen」不成立（无连接 UDP 的首次 sendto 即使端口无人监听也会返回成功），
    /// 只有回调触发才证明监听器完成「收包 → 建连 → 会话建立」全链路。完整端到端 IMessage 收发不在本冒烟测试范围。
    /// </remarks>
    [Fact]
    public async Task KcpServer_AfterStart_ShouldListenOnUdpPort()
    {
        using var udpClient = new UdpClient();
        var datagram = CreateMinimalKcpDatagram(1001u);

        // 真实 UDP 发送：服务器对未知 conv 的合法 KCP 包建连并触发 OnConnected
        await udpClient.SendAsync(datagram, datagram.Length, new IPEndPoint(IPAddress.Loopback, _kcpPort));

        var connectedTask = _sessionConnected.Task;
        await Task.WhenAny(connectedTask, Task.Delay(TimeSpan.FromSeconds(5)));
        Assert.True(connectedTask.IsCompleted, $"Expected a session to be established on UDP port {_kcpPort} after sending a minimal KCP datagram.");
    }

    /// <summary>
    /// 构造最小合法长度的 KCP 数据报（24 字节头：conv + cmd + frg + wnd + ts + sn + una + len）。
    /// </summary>
    /// <param name="conv">KCP 会话 Conv / The KCP conv</param>
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
    /// 获取可用 UDP 端口 / Get an available UDP port
    /// </summary>
    private static int GetAvailableUdpPort()
    {
        using var udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
    }

    private ValueTask OnConnected(IAppSession session)
    {
        _sessionConnected.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    private static ValueTask OnDisconnected(IAppSession session, CloseEventArgs closeEventArgs)
    {
        return ValueTask.CompletedTask;
    }

    private static ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        return ValueTask.CompletedTask;
    }
}
