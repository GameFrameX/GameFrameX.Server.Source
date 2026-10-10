// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整 license 文本请参见源代码根目录下的 LICENSE 文件。
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

using System.Buffers;
using System.Net;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Utility;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Message;
using GameFrameX.NetWork.Messages;
using GameFrameX.Proto.Proto;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.SuperSocket.Client;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;

namespace GameFrameX.Client.Bot;

/// <summary>
/// 机器人 KCP 客户端事件结构体,包含各种回调事件。
/// </summary>
/// <remarks>
/// 与 <see cref="BotTcpClientEvent"/> 同构，便于 <see cref="BotClient"/> 在两种传输间共享事件接线，
/// 不为 KCP 引入独立事件签名。命名沿用 BotTcpClientEvent 以保持与现有代码一致。
/// </remarks>
public struct BotKcpClientEvent
{
    /// <summary>
    /// 连接成功时的回调
    /// </summary>
    public Action OnConnectedCallback { get; set; }

    /// <summary>
    /// 连接关闭时的回调
    /// </summary>
    public Action OnClosedCallback { get; set; }

    /// <summary>
    /// 发生错误时的回调
    /// </summary>
    public Action<Exception> OnErrorCallback { get; set; }

    /// <summary>
    /// 接收到消息时的回调
    /// </summary>
    public Action<MessageObject> OnReceiveMsgCallback { get; set; }
}

/// <summary>
/// 机器人 KCP 客户端类,用于处理与服务器的 KCP 连接和消息收发。
/// </summary>
/// <remarks>
/// 与 <see cref="BotTcpClient"/> 同构的产品客户端 KCP 验证载体：通过 <c>GameFrameX.SuperSocket.Kcp</c>
/// 的 <c>EasyClient.AsKcp</c> 接入 KCP 传输，使用 <see cref="MessageObjectPipelineFilter"/> 产品编解码
/// 与服务端 <c>ConfigureKcpServer</c>（<c>AddServer&lt;IMessage, MessageObjectPipelineFilter&gt;</c>）保持一致。
/// 生命周期常量与状态机结构对齐 <see cref="BotTcpClient"/>：重试 5 次指数退避、心跳 5s、连接超时 15s。
/// </remarks>
public sealed class BotKcpClient
{
    private const int DelayTimes = 1000;
    private const int MaxRetryCount = 5;
    private const int ConnectingTimeoutMs = 15000;
    private const int HeartbeatIntervalMs = 5000;
    private const int ConnectStaggerMs = 1000;
    private const int InitialRetryDelayMs = 5000;
    private const ushort InnerPackageHeaderLength = 14;

    private readonly EasyClient<IMessage> m_KcpClient;
    private readonly IEasyClient<IMessage> m_KcpClientApi;
    private readonly BotKcpClientEvent m_BotKcpClientEvent;
    private readonly IMessageCompressHandler messageCompressHandler;
    private readonly string _serverHost;
    private readonly int _serverPort;

    private int m_RetryCount;
    private int m_RetryDelay = InitialRetryDelayMs;
    private DateTime? _connectingSince;
    private CancellationTokenSource _receiveCts;
    private volatile bool _isConnectionAlive;

    /// <summary>
    /// 初始化机器人 KCP 客户端。地址解析延迟到 <see cref="TryConnectAsync"/>（构造期不触发 DNS，
    /// 保证仅用 TCP 传输时非法 KCP 主机名不影响 BotClient 装配）。
    /// </summary>
    /// <param name="clientEvent">客户端事件回调结构体</param>
    /// <param name="serverHost">KCP 服务器地址（IP 或主机名）</param>
    /// <param name="serverPort">KCP 服务器端口</param>
    public BotKcpClient(BotKcpClientEvent clientEvent, string serverHost, int serverPort)
    {
        m_BotKcpClientEvent = clientEvent;
        _serverHost = serverHost;
        _serverPort = serverPort;

        m_KcpClient = new EasyClient<IMessage>(new MessageObjectPipelineFilter());
        m_KcpClient.Closed += OnKcpClientOnClosed;
        m_KcpClientApi = m_KcpClient.AsClient();
        messageCompressHandler = new DefaultMessageCompressHandler();
    }

    /// <summary>
    /// 启动客户端并尝试连接服务器。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>异步任务</returns>
    public async Task EntryAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (await HandleConnectionTickAsync(cancellationToken))
                {
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // no-op
        }
        finally
        {
            Disconnect();
        }
    }

    /// <summary>
    /// 按当前连接状态执行一次轮询,返回是否应终止重连循环。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true 表示已达最大重试次数应退出循环;否则 false</returns>
    private async Task<bool> HandleConnectionTickAsync(CancellationToken cancellationToken)
    {
        if (IsConnected)
        {
            return await HandleConnectedStateAsync(cancellationToken);
        }

        if (IsConnecting)
        {
            return await HandleConnectingStateAsync(cancellationToken);
        }

        return await HandleDisconnectedStateAsync(cancellationToken);
    }

    /// <summary>
    /// 当前是否已建立 KCP 连接（IEasyClient 不暴露连接句柄，用本地标志追踪 ConnectAsync 成功状态）。
    /// </summary>
    private bool IsConnected
    {
        get
        {
            return _isConnectionAlive;
        }
    }

    /// <summary>
    /// 当前是否处于连接等待阶段（用 _connectingSince 时间戳近似追踪）。
    /// </summary>
    private bool IsConnecting
    {
        get
        {
            return _connectingSince.HasValue && !IsConnected;
        }
    }

    /// <summary>
    /// 处理已连接状态：启动接收循环、重置重试计数、发送心跳并按节奏轮询。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>始终返回 false（已连接状态不终止循环）</returns>
    private async Task<bool> HandleConnectedStateAsync(CancellationToken cancellationToken)
    {
        m_RetryCount = 0;
        m_RetryDelay = InitialRetryDelayMs;
        _connectingSince = null;
        EnsureReceiveLoopStarted(cancellationToken);
        SendHeartBeat();
        await Task.Delay(HeartbeatIntervalMs, cancellationToken);
        return false;
    }

    /// <summary>
    /// 处理连接中状态：持续发送探测心跳并判定是否卡死超时,超时则放弃当前会话并按上限重试或终止。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true 表示已达最大重试次数应退出循环;否则 false</returns>
    private async Task<bool> HandleConnectingStateAsync(CancellationToken cancellationToken)
    {
        _connectingSince ??= DateTime.UtcNow;
        SendConnectProbe();
        if ((DateTime.UtcNow - _connectingSince.Value).TotalMilliseconds <= ConnectingTimeoutMs)
        {
            await Task.Delay(ConnectStaggerMs, cancellationToken);
            return false;
        }

        // Localization: Client.Bot.Kcp.ConnectingTimeoutAbandonSession - 连接中状态超时（{0}毫秒），放弃当前会话
        LogHelper.Warning(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.Bot.Kcp.ConnectingTimeoutAbandonSession, ConnectingTimeoutMs));
        await AbandonConnectingSessionAsync();
        _connectingSince = null;

        if (m_RetryCount >= MaxRetryCount)
        {
            // Localization: Client.MaxRetryReached - 重连次数已达到上限，停止尝试。
            LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.MaxRetryReached));
            return true;
        }

        m_RetryCount++;
        // Localization: Client.RetryConnect - 未连接到服务器, 尝试重连 (尝试次数: {0}/{1})
        LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.RetryConnect, m_RetryCount, MaxRetryCount));
        TryConnectAsync(cancellationToken);
        await Task.Delay(DelayTimes, cancellationToken);
        return false;
    }

    /// <summary>
    /// 处理断开状态：尝试连接服务器,失败则按退避策略重试或达上限终止。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>true 表示已达最大重试次数应退出循环;否则 false</returns>
    private async Task<bool> HandleDisconnectedStateAsync(CancellationToken cancellationToken)
    {
        _connectingSince = null;
        // Localization: Client.AttemptingToConnect - 尝试连接到服务器...
        LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.AttemptingToConnect));
        TryConnectAsync(cancellationToken);
        await Task.Delay(DelayTimes, cancellationToken);

        if (IsConnected || IsConnecting)
        {
            return false;
        }

        if (m_RetryCount >= MaxRetryCount)
        {
            // Localization: Client.MaxRetryReached - 重连次数已达到上限，停止尝试。
            LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.MaxRetryReached));
            return true;
        }

        m_RetryCount++;
        // Localization: Client.RetryConnect - 未连接到服务器, 尝试重连 (尝试次数: {0}/{1})
        LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.RetryConnect, m_RetryCount, MaxRetryCount));
        await Task.Delay(m_RetryDelay, cancellationToken);
        m_RetryDelay *= 2;
        return false;
    }

    /// <summary>
    /// 主动断开连接，模拟客户端离线。
    /// </summary>
    /// <remarks>无论已连接还是连接中都执行关闭：KCP 连接中阶段同样持有 UDP socket 与本地会话，须一并释放。</remarks>
    public void Disconnect()
    {
        DisposeReceiveCts();
        try
        {
            m_KcpClientApi.CloseAsync().GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // Localization: Client.Bot.DisconnectFailed - 断开连接失败: {0}
            LogHelper.Warning(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.Bot.DisconnectFailed, e.Message));
        }
        finally
        {
            _isConnectionAlive = false;
        }
    }

    /// <summary>
    /// 发送心跳包到服务器。
    /// </summary>
    private void SendHeartBeat()
    {
        ReqHeartBeat req = new ReqHeartBeat
        {
            Timestamp = TimerHelper.UnixTimeMilliseconds(),
        };
        SendToServer(req);
    }

    /// <summary>
    /// 发送消息到服务器。
    /// </summary>
    /// <param name="messageObject">要发送的消息对象</param>
    public void SendToServer(MessageObject messageObject)
    {
        var buffer = Handler(messageObject);
        if (buffer != null)
        {
            try
            {
                if (!IsConnected)
                {
                    return;
                }

                m_KcpClientApi.SendAsync(buffer).GetAwaiter().GetResult();
            }
            catch (Exception e)
            {
                // Localization: Client.Bot.Kcp.SendToServerFailed - 发送消息到服务器失败: {0}
                LogHelper.Warning(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.Bot.Kcp.SendToServerFailed, e.Message));
            }
        }
    }

    /// <summary>
    /// 解析服务端终结点：IP 字符串直接解析，主机名经 DNS 解析（优先 IPv4）。
    /// </summary>
    /// <remarks>延迟到连接期执行而非构造期：仅用 TCP 传输时 KCP 主机名不触发 DNS，DNS 失败也只走 KCP 重试而非构造异常。</remarks>
    /// <returns>解析后的服务端终结点</returns>
    private IPEndPoint ResolveServerEndPoint()
    {
        if (IPAddress.TryParse(_serverHost, out var address))
        {
            return new IPEndPoint(address, _serverPort);
        }

        var addresses = Dns.GetHostAddresses(_serverHost);
        address = Array.Find(addresses, candidate => candidate.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
        if (address == null)
        {
            address = addresses.Length > 0 ? addresses[0] : throw new InvalidOperationException($"Unable to resolve KCP server host: {_serverHost}");
        }

        return new IPEndPoint(address, _serverPort);
    }

    /// <summary>
    /// 执行一次 KCP 连接初始化：调用 <c>EasyClient.AsKcp</c> 配置传输、启动接收循环并发送首个探测心跳，进入连接中状态。
    /// </summary>
    /// <remarks>
    /// KCP 无 TCP 三次握手，<c>AsKcp</c> 只完成本地 UDP socket 绑定 + KCP 状态机初始化，不等待服务端响应——
    /// 因此此处<b>不</b>置已连接标志、<b>不</b>触发 OnConnectedCallback：连接中状态由首个探测心跳驱动服务端建连
    /// （服务端对未知 <c>EndPoint:Conv</c> 的合法 KCP 包即建会话），收到服务端首个应用层响应（心跳应答
    /// <c>NotifyHeartBeat</c>）后才由 <see cref="ReceiveLoopAsync"/> 确认连接。服务端不可达时收不到任何响应，
    /// <see cref="HandleConnectingStateAsync"/> 的 ConnectingTimeoutMs 超时路径正常触发，关闭会话并进入重试。
    /// 不调用 <c>StartReceive</c>（会启动一个与本类 ReceiveLoopAsync 冲突的后台 ReceiveAsync，导致
    /// "transition a task to a final state" 异常），也不调用 <c>ConnectAsync</c>（KCP 模式无握手语义）。
    /// </remarks>
    /// <param name="cancellationToken">取消令牌</param>
    private void TryConnectAsync(CancellationToken cancellationToken)
    {
        try
        {
            var kcpOptions = new KcpConnectionOptions
            {
                NoDelay = true,
                Interval = 10,
                Mtu = 1400,
                IdleTimeout = 60000,
            };
            m_KcpClient.AsKcp(ResolveServerEndPoint(), kcpOptions, ArrayPool<byte>.Shared, 4096);
            EnsureReceiveLoopStarted(cancellationToken);
            _connectingSince = DateTime.UtcNow;
            SendConnectProbe();
        }
        catch (Exception e)
        {
            // Localization: Client.ErrorOccurred - 客户端发生错误: {0}
            LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.ErrorOccurred, e.Message));
            m_BotKcpClientEvent.OnErrorCallback?.Invoke(e);
        }
    }

    /// <summary>
    /// 发送连接探测心跳：驱动服务端按 <c>EndPoint:Conv</c> 建立 KCP 会话并应答 <c>NotifyHeartBeat</c>。
    /// </summary>
    /// <remarks>探测发送不受 <see cref="IsConnected"/> 门槛约束（连接确认恰恰依赖探测触发服务端响应）。</remarks>
    private void SendConnectProbe()
    {
        try
        {
            var probe = new ReqHeartBeat
            {
                Timestamp = TimerHelper.UnixTimeMilliseconds(),
            };
            m_KcpClientApi.SendAsync(Handler(probe)).GetAwaiter().GetResult();
        }
        catch (Exception e)
        {
            // 探测失败（如服务端不可达导致的本地异常）不打断连接中状态：由超时路径统一兜底重试。
            // Localization: Client.Bot.Kcp.ConnectProbeFailed - KCP 连接探测失败: {0}
            LogHelper.Warning(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.Bot.Kcp.ConnectProbeFailed, e.Message));
        }
    }

    /// <summary>
    /// 放弃当前卡死的连接握手：关闭本地会话句柄并停止接收循环。
    /// </summary>
    /// <remarks>连接中阶段 <see cref="_isConnectionAlive"/> 尚未置位，关闭不以此为门槛。</remarks>
    private async Task AbandonConnectingSessionAsync()
    {
        DisposeReceiveCts();
        try
        {
            await m_KcpClientApi.CloseAsync();
        }
        catch
        {
            // 放弃卡死连接时关闭失败可忽略
        }
        finally
        {
            _isConnectionAlive = false;
        }
    }

    /// <summary>
    /// 确保接收循环已启动（连接初始化时启动一次，连接中与已连接两阶段共用）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    private void EnsureReceiveLoopStarted(CancellationToken cancellationToken)
    {
        if (_receiveCts != null)
        {
            return;
        }

        _receiveCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        // 先捕获 token 再进闭包：避免 Task.Run 延迟读取 _receiveCts 字段与 Disconnect 的释放竞态。
        var receiveToken = _receiveCts.Token;
        _ = Task.Run(() => ReceiveLoopAsync(receiveToken), receiveToken);
    }

    /// <summary>
    /// 接收循环：轮询 EasyClient.ReceiveAsync，连接中收到服务端首个应用层响应即确认连接，直到连接关闭或取消。
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested && (IsConnected || IsConnecting))
        {
            try
            {
                var message = await m_KcpClientApi.ReceiveAsync();
                if (message == null)
                {
                    await Task.Delay(50, cancellationToken);
                    continue;
                }

                if (!IsConnected)
                {
                    MarkSessionEstablished();
                }

                DispatchReceivedMessage(message);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception e)
            {
                // Localization: Client.Bot.Kcp.ReceiveError - KCP 接收错误: {0}
                LogHelper.Warning(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.Bot.Kcp.ReceiveError, e.Message));
                m_BotKcpClientEvent.OnErrorCallback?.Invoke(e);
                await Task.Delay(100, cancellationToken);
            }
        }
    }

    /// <summary>
    /// 确认连接建立：收到服务端首个应用层响应（心跳应答），置已连接标志并触发连接成功回调。
    /// </summary>
    private void MarkSessionEstablished()
    {
        _isConnectionAlive = true;
        _connectingSince = null;
        m_BotKcpClientEvent.OnConnectedCallback?.Invoke();
    }

    /// <summary>
    /// 释放接收循环的 CancellationTokenSource。
    /// </summary>
    private void DisposeReceiveCts()
    {
        if (_receiveCts != null)
        {
            try
            {
                _receiveCts.Cancel();
            }
            catch
            {
                // 取消已释放的 CTS 可忽略
            }

            _receiveCts.Dispose();
            _receiveCts = null;
        }
    }

    /// <summary>
    /// 将接收到的 IMessage 派发为 MessageObject 事件回调（与 BotTcpClient 回调形状一致）。
    /// </summary>
    /// <param name="message">接收到的 IMessage（来自 MessageObjectPipelineFilter，实际为 NetworkMessagePackage）</param>
    private void DispatchReceivedMessage(IMessage message)
    {
        if (message is NetworkMessagePackage package && package.DeserializeMessageObject() is MessageObject messageObject)
        {
            m_BotKcpClientEvent.OnReceiveMsgCallback?.Invoke(messageObject);
        }
    }

    /// <summary>
    /// 处理客户端连接关闭事件。
    /// </summary>
    private void OnKcpClientOnClosed(object sender, EventArgs e)
    {
        _isConnectionAlive = false;
        // Localization: Client.Disconnected - 客户端断开连接
        LogHelper.Info(LocalizationService.GetString(GameFrameX.Localization.Keys.Client.Disconnected));
        m_BotKcpClientEvent.OnClosedCallback?.Invoke();
    }

    /// <summary>
    /// 处理要发送的消息,将消息对象转换为字节数组。
    /// </summary>
    /// <param name="message">要处理的消息对象</param>
    /// <returns>处理后的字节数组</returns>
    private byte[] Handler(MessageObject message)
    {
        MessageProtoHelper.SetMessageId(message);
        message.SetOperationType(MessageProtoHelper.GetMessageOperationType(message));

        var messageData = ProtoBufSerializerHelper.Serialize(message);
        byte zipFlag = 0;
        if (messageData.Length > 512)
        {
            messageData = messageCompressHandler.Handler(messageData);
            zipFlag = 1;
        }

        var totalLength = messageData.Length + InnerPackageHeaderLength;
        var buffer = new byte[totalLength];
        var offset = 0;
        buffer.WriteIntBigEndianValue(totalLength, ref offset);
        buffer.WriteByteValue(message.OperationType, ref offset);
        buffer.WriteByteValue(zipFlag, ref offset);
        buffer.WriteIntBigEndianValue(message.UniqueId, ref offset);
        buffer.WriteIntBigEndianValue(message.MessageId, ref offset);
        buffer.WriteBytesWithoutLengthBigEndian(messageData, ref offset);
        return buffer;
    }
}