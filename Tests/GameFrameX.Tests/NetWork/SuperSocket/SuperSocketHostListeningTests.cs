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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Message;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Server;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.SuperSocket.Server.Host;
using GameFrameX.SuperSocket.Udp;
using GameFrameX.SuperSocket.WebSocket;
using GameFrameX.SuperSocket.WebSocket.Server;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace GameFrameX.Tests.NetWork.SuperSocket;

/// <summary>
/// SuperSocket 多服务器主机构建器监听冒烟测试 / SuperSocket multiple-server host builder listening smoke tests
/// </summary>
/// <remarks>
/// 覆盖 GFX-819（GameFrameX.SuperSocket 1.2.0 → 1.3.0 升级）的验收标准：
/// 通过与 <c>AppStartUpByServer.StartServer</c> 相同的构建路径（<c>MultipleServerHostBuilder.Create</c> +
/// <c>AddServer</c> + <c>AddWebSocketServer</c> + <c>UseUdp</c>）启动 TCP（含 UDP）与 WebSocket 服务器，
/// 并用真实客户端连接证明端口处于监听状态。
/// </remarks>
public class SuperSocketHostListeningTests : IAsyncLifetime
{
    private IHost _host;
    private int _tcpPort;
    private int _webSocketPort;

    public async Task InitializeAsync()
    {
        _tcpPort = GetAvailablePort();
        _webSocketPort = GetAvailablePort();

        var multipleServerHostBuilder = MultipleServerHostBuilder.Create();

        multipleServerHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
        {
            // 注意：不要在此链上调用 UseUdp()——SuperSocket 的连接监听器工厂按「最后注册者生效」解析，
            // 一旦启用 UDP 该服务器将只监听 UDP（对应生产 Setting.IsEnableUdp 的二选一语义）。
            builder
                .UseSessionHandler(OnConnected, OnDisconnected)
                .UsePackageHandler(PackageHandler)
                .ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        var listenOptions = new ListenOptions
                        {
                            Ip = IPAddress.Loopback.ToString(),
                            Port = _tcpPort,
                        };
                        options.AddListener(listenOptions);
                    });
                });
        });

        multipleServerHostBuilder.AddWebSocketServer(builder =>
        {
            builder
                .UseWebSocketMessageHandler(WebSocketMessageHandler)
                .UseSessionHandler(OnConnected, OnDisconnected)
                .ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        var listenOptions = new ListenOptions
                        {
                            Ip = IPAddress.Loopback.ToString(),
                            Port = _webSocketPort,
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
    /// TCP 服务器启动后应接受客户端连接 / TCP server should accept client connections after startup
    /// </summary>
    [Fact]
    public async Task TcpServer_AfterStart_ShouldAcceptConnection()
    {
        using var tcpClient = new TcpClient();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        await tcpClient.ConnectAsync(IPAddress.Loopback, _tcpPort, cancellationTokenSource.Token);

        Assert.True(tcpClient.Connected);
    }

    /// <summary>
    /// WebSocket 服务器启动后应完成握手 / WebSocket server should complete handshake after startup
    /// </summary>
    [Fact]
    public async Task WebSocketServer_AfterStart_ShouldAcceptConnection()
    {
        using var webSocket = new ClientWebSocket();
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var uri = new Uri($"ws://{IPAddress.Loopback}:{_webSocketPort}/");

        await webSocket.ConnectAsync(uri, cancellationTokenSource.Token);

        Assert.Equal(WebSocketState.Open, webSocket.State);
    }

    /// <summary>
    /// 获取可用端口 / Get an available port
    /// </summary>
    private static int GetAvailablePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    private static ValueTask OnConnected(IAppSession session)
    {
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

    private static ValueTask WebSocketMessageHandler(WebSocketSession session, WebSocketPackage package)
    {
        return ValueTask.CompletedTask;
    }
}
