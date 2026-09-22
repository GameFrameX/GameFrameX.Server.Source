// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Messages;
using GameFrameX.NetWork.RemoteMessaging.Unified;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.Foundation.Logger;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 本地 envelope 复投器（C143e：case 1 命中时把 envelope 解包成本服本地投递）。
/// </summary>
/// <remarks>
/// The local envelope dispatcher for D3 case 1 (C143e). When the routing seam
/// hands an envelope to the local dispatcher, the receiving end is responsible
/// for unpacking the embedded <see cref="RoleRouteEnvelopeMessage.InnerMessageId"/>
/// and <see cref="RoleRouteEnvelopeMessage.InnerMessageBytes"/> into a concrete
/// <see cref="MessageObject"/> and re-entering the local delivery pipeline via
/// the injected <see cref="IPlayerLocalSender"/> — which is exactly what the
/// existing <c>SendToPlayerInnerHandler</c> does for the RPC envelope path. We
/// mirror that semantic on the cross-process envelope path so the two transports
/// stay interchangeable.
/// ponytail: 失败时不抛（默认 Drop 策略）；保证路由缝不会因 envelope 解包异常而把整个目标服
/// 拖进无限重试。仅日志 warning，由下一次发件方轮询 / TTL 兜底。
/// </remarks>
public sealed class LocalEnvelopeDispatcher : ILocalRoleMessageDispatcher
{
    private readonly IPlayerLocalSender _localSender;
    private readonly PlayerOfflineStrategy _offlineStrategy;

    /// <summary>
    /// 初始化本地 envelope 复投器。
    /// </summary>
    /// <remarks>
    /// Initializes the dispatcher with the local sender used for the actual
    /// delivery and the strategy to apply when the target player is no longer
    /// online (rare race window: heartbeat says Active but SessionManager has
    /// already removed the session between send and receive).
    /// </remarks>
    /// <param name="localSender">本服玩家发送器 / The local player sender</param>
    /// <param name="offlineStrategy">离线处理策略（默认 Drop：仅日志，不抛）/ Offline handling strategy (defaults to Drop: log only)</param>
    public LocalEnvelopeDispatcher(IPlayerLocalSender localSender, PlayerOfflineStrategy offlineStrategy = PlayerOfflineStrategy.Discard)
    {
        ArgumentNullException.ThrowIfNull(localSender, nameof(localSender));
        _localSender = localSender;
        _offlineStrategy = offlineStrategy;
    }

    /// <inheritdoc />
    public async Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope, nameof(envelope));

        var envelopeMessage = envelope.Message as RoleRouteEnvelopeMessage;
        if (envelopeMessage == null)
        {
            LogHelper.Warning("[LocalEnvelopeDispatcher] envelope message is not RoleRouteEnvelopeMessage (actual type: {actual}); dropping", envelope.Message?.GetType().FullName ?? "null");
            return;
        }

        var innerType = MessageProtoHelper.GetMessageTypeById(envelopeMessage.InnerMessageId);
        if (innerType == null)
        {
            LogHelper.Warning("[LocalEnvelopeDispatcher] inner message id {innerMessageId} is not registered in MessageProtoHelper; dropping envelope for target {targetActorId}", envelopeMessage.InnerMessageId, envelope.TargetActorId);
            return;
        }

        MessageObject innerMessage;
        try
        {
            innerMessage = (MessageObject)ProtoBufSerializerHelper.Deserialize(envelopeMessage.InnerMessageBytes ?? Array.Empty<byte>(), innerType);
        }
        catch (Exception exception)
        {
            LogHelper.Error(exception, "[LocalEnvelopeDispatcher] failed to deserialize inner message for target {targetActorId} (innerMessageId={innerMessageId}); dropping", envelope.TargetActorId, envelopeMessage.InnerMessageId);
            return;
        }

        if (envelope.TargetActorId <= 0)
        {
            LogHelper.Warning("[LocalEnvelopeDispatcher] envelope has no TargetActorId (sender bug?); dropping inner message id={innerMessageId}", envelopeMessage.InnerMessageId);
            return;
        }

        if (!_localSender.IsPlayerOnline(envelope.TargetActorId))
        {
            switch (_offlineStrategy)
            {
                case PlayerOfflineStrategy.StoreOffline:
                    LogHelper.Info("[LocalEnvelopeDispatcher] target {targetActorId} is offline; StoreOffline strategy placeholder — message dropped (no offline store yet)", envelope.TargetActorId);
                    break;
                case PlayerOfflineStrategy.Discard:
                    LogHelper.Info("[LocalEnvelopeDispatcher] target {targetActorId} is offline; Discard strategy — message dropped", envelope.TargetActorId);
                    break;
                default:
                    LogHelper.Info("[LocalEnvelopeDispatcher] target {targetActorId} is offline; default strategy — message dropped", envelope.TargetActorId);
                    break;
            }

            return;
        }

        await _localSender.SendToLocalPlayerAsync(envelope.TargetActorId, innerMessage).ConfigureAwait(false);
    }
}
