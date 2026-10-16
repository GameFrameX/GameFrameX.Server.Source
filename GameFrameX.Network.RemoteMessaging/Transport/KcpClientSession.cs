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
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Net;
using System.Net.Sockets;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// 服务器间 KCP 客户端会话：独占一个 UDP Socket 与一个 conv，承担 UDP 生命周期
/// （接收循环投递 <see cref="KcpPipeConnection.InputUdpPacketAsync"/>）、KCP 更新循环启动与关闭语义。
/// </summary>
/// <remarks>
/// Inter-server KCP client session: owns one UDP socket and one conversation id, and carries the
/// UDP lifecycle (a receive loop feeding <see cref="KcpPipeConnection.InputUdpPacketAsync"/>), the KCP
/// update-loop startup, and the close semantics. The server side keys its session by
/// <c>RemoteEndPoint:Conv</c>, so the client-chosen non-zero conv is sufficient for uniqueness.
/// <para>参数缺省值与客户端接入面（StartUp <c>ConfigureKcpServer</c>）的实时游戏推荐配置一致：
/// NoDelay + Interval 10 + Mtu 1400 + 显式窗口 128；消息模式（内核缺省，与客户端接入面同路径）
/// 整帧交付。单帧上限 <see cref="MaxFrameBytes"/>（实测内核多段吞吐病态边界，见类内注释）。</para>
/// </remarks>
internal sealed class KcpClientSession : IDisposable
{
    /// <summary>KCP 段头长度（conv 4 + cmd 1 + frg 1 + wnd 2 + ts 4 + sn 4 + una 4 + len 4）。</summary>
    internal const int KcpSegmentHeaderLength = 24;

    /// <summary>最大传输单元（与客户端接入面一致；实际 UDP 报文 = Mtu）。</summary>
    internal const int Mtu = 1400;

    /// <summary>
    /// 单帧上限 = 8 段（每段载荷 <see cref="Mtu"/> - <see cref="KcpSegmentHeaderLength"/>）。
    /// 实测边界（回环基准，notes.md）：≤8 段（11KB）亚秒完成；≥20 段（27KB+）触发内核
    /// RTO 退避级联（分钟级吞吐病态）。超限帧由 <see cref="KcpStream.WriteAsync"/> 显式拒绝
    /// （IOException，RPC 层按连接级失败处理），更大帧应声明 tcp 端点。
    /// </summary>
    internal const int MaxFrameBytes = 8 * (Mtu - KcpSegmentHeaderLength);

    /// <summary>UDP 单个数据报接收缓冲上限（含 IP 头后的载荷上限）。</summary>
    private const int DatagramBufferSize = 65535;

    private readonly Socket _socket;
    private readonly IPEndPoint _remoteEndPoint;
    private readonly RemoteMessagingKcpConnection _connection;

    private readonly KcpStream _stream;
    private volatile bool _disposed;

    /// <summary>
    /// 初始化 KCP 客户端会话：创建独占 UDP Socket、分配 conv、装配 KCP 连接并启动更新循环与接收循环。
    /// </summary>
    /// <remarks>
    /// Initializes the KCP client session: creates the dedicated UDP socket, allocates the
    /// conversation id, wires the KCP connection, and starts the update loop and the receive loop.
    /// </remarks>
    /// <param name="remoteEndPoint">远端服务器间 KCP 监听端点 / The remote inter-server KCP listener endpoint</param>
    internal KcpClientSession(IPEndPoint remoteEndPoint)
    {
        _remoteEndPoint = remoteEndPoint;
        _socket = new Socket(remoteEndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        _socket.Bind(new IPEndPoint(remoteEndPoint.AddressFamily == AddressFamily.InterNetwork ? IPAddress.Any : IPAddress.IPv6Any, 0));

        // conv 客户端侧分配且非 0：内核对 conv=0 回落选项缺省 0，服务端按 EndPoint:Conv 建会话
        var conv = (uint)Random.Shared.Next(1, int.MaxValue);
        // 消息模式（内核缺省 StreamMode=false，与客户端接入面同路径）：单帧 ≤ MaxFrameBytes 时
        // 整帧交付，不依赖管线过滤器的部分帧重组。窗口显式 128：内核缺省 32 在多段帧下
        // 接收窗口枯竭（实测 ≥20 段触发 RTO 退避级联），128 覆盖 8 段帧上限并留裕量。
        var kcpOptions = new KcpConnectionOptions
        {
            NoDelay = true,
            Interval = 10,
            Mtu = Mtu,
            SendWindow = 128,
            ReceiveWindow = 128,
        };
        _connection = new RemoteMessagingKcpConnection(_socket, remoteEndPoint, $"kcp-client-{conv}", conv, new ConnectionOptions(), kcpOptions, ownsSocket: true);
        _connection.StartUpdateLoop();
        // 接收循环为会话生命周期常驻任务（丢弃引用：由调度器根持，Socket 关闭时自然退出）
        _ = Task.Run(ReceiveLoopAsync);
        _stream = new KcpStream(this);
    }

    /// <summary>
    /// 获取 KCP 应用层读面（Input 管道读端），供 <see cref="KcpStream"/> 读侧消费。
    /// </summary>
    /// <remarks>
    /// Gets the KCP application-level read face (the input pipe reader), consumed by the read side
    /// of <see cref="KcpStream"/>.
    /// </remarks>
    internal System.IO.Pipelines.PipeReader Reader
    {
        get { return _connection.Reader; }
    }

    /// <summary>
    /// 获取本会话的双工流适配（读 = KCP Input 管道，写 = 分块 KCP Send）。
    /// </summary>
    /// <remarks>
    /// Gets the duplex stream adaptation of this session (reads from the KCP input pipe,
    /// writes as chunked KCP sends).
    /// </remarks>
    internal Stream Stream
    {
        get { return _stream; }
    }

    /// <summary>
    /// 获取会话是否仍可用（连接未关闭且会话未释放）。
    /// </summary>
    /// <remarks>
    /// Gets whether the session is still usable (connection not closed and session not disposed).
    /// </remarks>
    internal bool IsAlive
    {
        get { return !_disposed && !_connection.IsClosed; }
    }

    /// <summary>
    /// 获取最近活跃时间（陈旧淘汰判据，镜像 TCP 池的存活检查面）。
    /// </summary>
    /// <remarks>
    /// Gets the last active time (the staleness criterion, mirroring the TCP pool's liveness face).
    /// </remarks>
    internal DateTimeOffset LastActiveTime
    {
        get { return _connection.LastActiveTime; }
    }

    /// <summary>
    /// 发送一块应用层载荷（单次 <see cref="KcpPipeConnection.SendAsync(ReadOnlyMemory{byte}, System.Threading.CancellationToken)"/> 整帧发送，由 <see cref="KcpStream"/> 的单帧上限守卫保证 ≤ <see cref="MaxFrameBytes"/>）。
    /// </summary>
    /// <remarks>
    /// Sends one application payload frame via a single
    /// <see cref="KcpPipeConnection.SendAsync(ReadOnlyMemory{byte}, System.Threading.CancellationToken)"/> call
    /// (bounded to <see cref="MaxFrameBytes"/> by <see cref="KcpStream"/>'s frame-size guard).
    /// </remarks>
    /// <param name="buffer">载荷帧 / The payload frame</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    internal ValueTask SendChunkAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        return _connection.SendAsync(buffer, cancellationToken);
    }

    /// <summary>
    /// 释放会话：关闭 KCP 连接（连带释放独占 Socket 并完成 Input 管道写端），接收循环随 Socket 关闭退出。
    /// </summary>
    /// <remarks>
    /// Releases the session: closes the KCP connection (which disposes the owned socket and
    /// completes the input pipe writer); the receive loop exits as the socket closes.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            // CloseAsync 关闭连接：置 _closed、取消更新循环、Complete(Input.Writer)（读侧随之见到流结束），
            // ownsSocket=true 时释放 Socket；接收循环在下一次收包时以异常退出
            _connection.CloseAsync(CloseReason.LocalClosing).AsTask().GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // 关闭路径不抛：会话已死时的残留异常按已关闭处理
        }
    }

    /// <summary>
    /// UDP 接收循环：把本 Socket 收到的数据报原样投递给 KCP 协议栈（服务端地址即远端监听端点）。
    /// </summary>
    /// <remarks>
    /// UDP receive loop: feeds every datagram received on this socket into the KCP stack unchanged
    /// (the peer is exactly the remote listener endpoint).
    /// </remarks>
    private async Task ReceiveLoopAsync()
    {
        var buffer = new byte[DatagramBufferSize];
        try
        {
            while (!_disposed)
            {
                var result = await _socket.ReceiveFromAsync(buffer, SocketFlags.None, _remoteEndPoint);
                await _connection.InputUdpPacketAsync(buffer.AsMemory(0, result.ReceivedBytes));
            }
        }
        catch (Exception)
        {
            // Socket 关闭（ObjectDisposedException/SocketException）按会话终止处理
        }
    }
}
