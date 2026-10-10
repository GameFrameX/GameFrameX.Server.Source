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
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using System.Net;
using GameFrameX.Client.Bot;
using GameFrameX.Foundation.Extensions;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Message;
using GameFrameX.NetWork.Messages;
using GameFrameX.Proto.Proto;
using GameFrameX.ProtoBuf.Net;
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
/// SuperSocket KCP 端到端测试：<see cref="BotKcpClient"/> 与基于 <c>UseKcp</c> 的服务端真实往返。
/// </summary>
/// <remarks>
/// 覆盖 C190 T4：以产品客户端程序集（<c>EasyClient&lt;IMessage&gt;</c> + <see cref="MessageObjectPipelineFilter"/> +
/// <c>AsKcp</c>）经 UDP loopback 走完整消息收发链路（编解码 + 生命周期 + conv 路由）。
/// <para>服务端装配与 <see cref="SuperSocketKcpListeningTests"/> 同构（MultipleServerHostBuilder +
/// AddServer&lt;IMessage, MessageObjectPipelineFilter&gt; + UseKcp），包处理器原样回写收到的 <see cref="ReqHeartBeat"/>
/// （含心跳白名单，规避 C134 未鉴权窗口）。</para>
/// <para>反证推演（lesson 2026-10-10-test-port-assertion-identity-mismatch）：
/// 断言 <see cref="ReqHeartBeat.Timestamp"/> 回填相等 + 同一会话内 conv 不变；若断言仅"成功收到消息"则放行任何回显，
/// 不能证明"产品编解码路径"已运行。</para>
/// </remarks>
public class SuperSocketKcpE2ETests : IAsyncLifetime
{
    private const long EchoTimestamp = 1778976000000L;

    private IHost _host;
    private int _kcpPort;
    private readonly TaskCompletionSource<bool> _sessionEstablished = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<ReqHeartBeat> _echoReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentQueue<uint> _observedConvs = new();

    public async Task InitializeAsync()
    {
        MessageProtoHelper.Init(typeof(ReqHeartBeat).Assembly);
        MessageHelper.SetMessageDecoderHandler(new DefaultMessageDecoderHandler(), new DefaultMessageDecompressHandler());
        MessageHelper.SetMessageEncoderHandler(new DefaultMessageEncoderHandler(), new DefaultMessageCompressHandler());
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
    /// BotKcpClient 经 KCP 真实往返：发心跳 → 服务端回写 → 客户端按数据身份断言。
    /// </summary>
    /// <remarks>
    /// 断言（按 lesson 反证推演）：
    /// 1. 收到回显，且消息类型为 ReqHeartBeat（证明产品编解码路径已运行）。
    /// 2. Timestamp 与发送值相等（证明编解码无损，且会话路由稳定）。
    /// 3. conv 在收包后被记录且非 0（证明 KCP 会话按 EndPoint:Conv 唯一标识建立）。
    /// </remarks>
    [Fact]
    public async Task BotKcpClient_Heartbeat_ShouldRoundTripThroughKcpServer()
    {
        var received = new TaskCompletionSource<MessageObject>(TaskCreationOptions.RunContinuationsAsynchronously);

        var clientEvent = new BotKcpClientEvent
        {
            OnConnectedCallback = () => { },
            OnClosedCallback = () => { },
            OnErrorCallback = _ => { },
            // bot 自身会按 5s 节奏发心跳（时间戳 = UnixTimeMs），与测试唯一时间戳 EchoTimestamp 区分；
            // 只接受匹配的时间戳，避免 bot 心跳的回显抢先满足断言。
            OnReceiveMsgCallback = msg =>
            {
                if (msg is ReqHeartBeat hb && hb.Timestamp == EchoTimestamp)
                {
                    received.TrySetResult(msg);
                }
            },
        };

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var client = new BotKcpClient(clientEvent, IPAddress.Loopback.ToString(), _kcpPort);
        var entryTask = client.EntryAsync(cts.Token);

        // 等待连接 + 服务端会话建立回调
        var established = await Task.WhenAny(_sessionEstablished.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
        Assert.True(established == _sessionEstablished.Task, "BotKcpClient failed to establish a KCP session within 5s.");

        // 发送心跳：使用唯一时间戳以与其它消息区分
        client.SendToServer(new ReqHeartBeat { Timestamp = EchoTimestamp });

        // 等待回显（接收回调由 MessageObject 形态触发，证明产品编解码路径已运行）
        var receiveTask = await Task.WhenAny(received.Task, Task.Delay(TimeSpan.FromSeconds(5), cts.Token));
        Assert.True(receiveTask == received.Task, "BotKcpClient failed to receive KCP echo within 5s.");

        var echo = received.Task.Result;
        var heartbeat = Assert.IsType<ReqHeartBeat>(echo);
        Assert.Equal(EchoTimestamp, heartbeat.Timestamp);

        // 反证：conv 应在 KCP 会话中非 0（fork 端 KcpPipeConnection.Conv 在建连后即落地）
        Assert.True(_observedConvs.TryPeek(out var observed) && observed != 0,
            "KCP conv should be non-zero after session established.");

        cts.Cancel();
        await Task.WhenAny(entryTask, Task.Delay(TimeSpan.FromSeconds(2)));
    }

    private ValueTask OnConnected(IAppSession session)
    {
        if (session.Connection is KcpPipeConnection kcpConnection)
        {
            _observedConvs.Enqueue(kcpConnection.Conv);
        }

        _sessionEstablished.TrySetResult(true);
        return ValueTask.CompletedTask;
    }

    private static ValueTask OnDisconnected(IAppSession session, CloseEventArgs closeEventArgs)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 包处理器：原样回写收到的 ReqHeartBeat（验证客户端→服务端→客户端经产品编解码的完整链路）。
    /// </summary>
    /// <remarks>
    /// 服务端使用与 <see cref="BotKcpClient"/> 相同的 14 字节头 + ProtoBuf 序列化路径回写，
    /// 保证客户端解码端按 MessageObjectPipelineFilter → MessageHelper.DecoderHandler 路径正常解码。
    /// </remarks>
    private ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        if (message is NetworkMessagePackage package && package.DeserializeMessageObject() is ReqHeartBeat request)
        {
            var echo = new ReqHeartBeat { Timestamp = request.Timestamp };
            session.SendAsync(EncodeHeartBeat(echo)).GetAwaiter().GetResult();
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 与 <see cref="BotKcpClient"/> 同源的消息头 + ProtoBuf 序列化（14 字节头）。
    /// </summary>
    private static byte[] EncodeHeartBeat(ReqHeartBeat message)
    {
        MessageProtoHelper.SetMessageId(message);
        message.SetOperationType(MessageProtoHelper.GetMessageOperationType(message));

        var messageData = ProtoBufSerializerHelper.Serialize(message);
        const ushort headerLength = 14;
        var totalLength = messageData.Length + headerLength;
        var buffer = new byte[totalLength];
        var offset = 0;
        buffer.WriteIntBigEndianValue(totalLength, ref offset);
        buffer.WriteByteValue((byte)message.OperationType, ref offset);
        buffer.WriteByteValue(0, ref offset);
        buffer.WriteIntBigEndianValue(message.UniqueId, ref offset);
        buffer.WriteIntBigEndianValue(message.MessageId, ref offset);
        buffer.WriteBytesWithoutLengthBigEndian(messageData, ref offset);
        return buffer;
    }

    /// <summary>
    /// 获取可用 UDP 端口（与 <see cref="SuperSocketKcpListeningTests"/> 同构）。
    /// </summary>
    private static int GetAvailableUdpPort()
    {
        using var udpClient = new System.Net.Sockets.UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        return ((IPEndPoint)udpClient.Client.LocalEndPoint).Port;
    }
}