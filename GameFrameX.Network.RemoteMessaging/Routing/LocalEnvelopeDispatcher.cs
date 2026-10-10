// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================


using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Messages;
using GameFrameX.Network.RemoteMessaging.Unified;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.Foundation.Logger;

namespace GameFrameX.Network.RemoteMessaging.Routing;

/// <summary>
/// 本地 envelope 复投器（case 1 命中时把 envelope 解包成本服本地投递）。
/// </summary>
/// <remarks>
/// The local envelope dispatcher for case 1. When the routing seam
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
        ArgumentNullException.ThrowIfNull(localSender);
        _localSender = localSender;
        _offlineStrategy = offlineStrategy;
    }

    /// <summary>
    /// 把跨进程信封解包为本服本地投递。
    /// </summary>
    /// <remarks>
    /// Unpacks the cross-process envelope for local delivery: the envelope
    /// message must be a <see cref="RoleRouteEnvelopeMessage"/> (anything else is
    /// dropped with a warning); the embedded
    /// <see cref="RoleRouteEnvelopeMessage.InnerMessageId"/> is resolved through
    /// <c>MessageProtoHelper</c>, the
    /// <see cref="RoleRouteEnvelopeMessage.InnerMessageBytes"/> are deserialized
    /// into the concrete message, and — when <see cref="MessageEnvelope.TargetActorId"/>
    /// is valid and the player is still online — the message is re-delivered via the
    /// injected <see cref="IPlayerLocalSender"/>. Unregistered ids, deserialization
    /// failures, missing targets and offline players never throw: the envelope is
    /// dropped after logging, per the best-effort "never block the route seam"
    /// semantics.
    /// </remarks>
    /// <param name="envelope">路由信封（消息须为 RoleRouteEnvelopeMessage）/ The routing envelope (message must be a RoleRouteEnvelopeMessage)</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="envelope"/> 为 null 时抛出 / Thrown when <paramref name="envelope"/> is null</exception>
    public async Task DispatchAsync(MessageEnvelope envelope, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        var envelopeMessage = envelope.Message as RoleRouteEnvelopeMessage;
        if (envelopeMessage == null)
        {
            // Localization: RemoteMessaging.Routing.EnvelopeMessageWrongType - [LocalEnvelopeDispatcher] 信封消息不是 RoleRouteEnvelopeMessage（实际类型：{0}）；丢弃
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.EnvelopeMessageWrongType, envelope.Message?.GetType().FullName ?? "null"));
            return;
        }

        var innerType = MessageProtoHelper.GetMessageTypeById(envelopeMessage.InnerMessageId);
        if (innerType == null)
        {
            // Localization: RemoteMessaging.Routing.InnerMessageIdNotRegistered - [LocalEnvelopeDispatcher] 内层消息 ID {0} 未在 MessageProtoHelper 注册；丢弃发往目标 {1} 的信封
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.InnerMessageIdNotRegistered, envelopeMessage.InnerMessageId, envelope.TargetActorId));
            return;
        }

        MessageObject innerMessage;
        try
        {
            innerMessage = (MessageObject)ProtoBufSerializerHelper.Deserialize(envelopeMessage.InnerMessageBytes ?? Array.Empty<byte>(), innerType);
        }
        catch (Exception exception)
        {
            // Localization: RemoteMessaging.Routing.InnerMessageDeserializeFailed - [LocalEnvelopeDispatcher] 为目标 {0} 反序列化内层消息失败（innerMessageId={1}）；丢弃
            LogHelper.Error(exception, LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.InnerMessageDeserializeFailed, envelope.TargetActorId, envelopeMessage.InnerMessageId));
            return;
        }

        if (envelope.TargetActorId <= 0)
        {
            // Localization: RemoteMessaging.Routing.EnvelopeMissingTargetActorId - [LocalEnvelopeDispatcher] 信封缺少 TargetActorId（发件方 bug？）；丢弃内层消息 id={0}
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.EnvelopeMissingTargetActorId, envelopeMessage.InnerMessageId));
            return;
        }

        if (!_localSender.IsPlayerOnline(envelope.TargetActorId))
        {
            switch (_offlineStrategy)
            {
                case PlayerOfflineStrategy.StoreOffline:
                    // Localization: RemoteMessaging.Routing.TargetOfflineStoreOfflineDropped - [LocalEnvelopeDispatcher] 目标 {0} 已离线；StoreOffline 策略占位——消息已丢弃（尚无离线存储）
                    LogHelper.Info(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.TargetOfflineStoreOfflineDropped, envelope.TargetActorId));
                    break;
                case PlayerOfflineStrategy.Discard:
                    // Localization: RemoteMessaging.Routing.TargetOfflineDiscardDropped - [LocalEnvelopeDispatcher] 目标 {0} 已离线；Discard 策略——消息已丢弃
                    LogHelper.Info(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.TargetOfflineDiscardDropped, envelope.TargetActorId));
                    break;
                default:
                    // Localization: RemoteMessaging.Routing.TargetOfflineDefaultDropped - [LocalEnvelopeDispatcher] 目标 {0} 已离线；默认策略——消息已丢弃
                    LogHelper.Info(LocalizationService.GetString(Localization.Keys.RemoteMessaging.Routing.TargetOfflineDefaultDropped, envelope.TargetActorId));
                    break;
            }

            return;
        }

        await _localSender.SendToLocalPlayerAsync(envelope.TargetActorId, innerMessage).ConfigureAwait(false);
    }
}