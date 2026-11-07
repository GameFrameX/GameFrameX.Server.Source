using GameFrameX.Network.Abstractions;

namespace GameFrameX.Core.Session;

/// <summary>
/// 通过注入的会话管理器向本进程玩家投递消息，不依赖发现、路由或远程消息设施。
/// </summary>
public sealed class LocalPlayerMessageSender
{
    private readonly IPlayerSessionManager _sessionManager;

    /// <summary>创建本地玩家发送器。</summary>
    /// <param name="sessionManager">本进程使用的玩家会话管理器。</param>
    public LocalPlayerMessageSender(IPlayerSessionManager sessionManager)
    {
        ArgumentNullException.ThrowIfNull(sessionManager);
        _sessionManager = sessionManager;
    }

    /// <summary>
    /// 向玩家当前连接写入网络消息，保留成功、离线、失败和取消的区别。
    /// </summary>
    /// <remarks>
    /// 通道仅支持 INetworkMessage，其他 IMessage 实现返回失败。
    /// 底层通道没有取消参数：取消会终止等待，但不能撤回已经开始的通道写入。
    /// </remarks>
    /// <param name="playerId">目标玩家标识。</param>
    /// <param name="message">待投递的消息。</param>
    /// <param name="cancellationToken">发送前检查并用于取消写入等待的令牌。</param>
    /// <returns>真实通道写入结果。</returns>
    /// <exception cref="ArgumentNullException">消息为空。</exception>
    /// <exception cref="OperationCanceledException">调用方取消等待或底层写入取消。</exception>
    public async Task<PlayerSendResult> SendAsync(long playerId, IMessage message, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        if (message is not INetworkMessage networkMessage)
        {
            return new PlayerSendResult
            {
                PlayerId = playerId,
                Status = PlayerSendStatus.Failed,
                ErrorMessage = "Message must implement INetworkMessage.",
            };
        }

        try
        {
            var session = _sessionManager.GetByPlayerId(playerId);
            if (session?.WorkChannel == null || session.WorkChannel.IsClosed() || session.WorkChannel.GameAppSession is { IsConnected: false })
            {
                return new PlayerSendResult { PlayerId = playerId, Status = PlayerSendStatus.Offline };
            }

            cancellationToken.ThrowIfCancellationRequested();
            await session.WriteAsync(networkMessage).WaitAsync(cancellationToken);
            return new PlayerSendResult { PlayerId = playerId, Status = PlayerSendStatus.LocalDelivered };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new PlayerSendResult
            {
                PlayerId = playerId,
                Status = PlayerSendStatus.Failed,
                ErrorMessage = exception.Message,
            };
        }
    }
}
