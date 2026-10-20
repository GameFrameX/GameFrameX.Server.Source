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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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


using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Hotfix.Logic.Game.RockPaperScissors;
using GameFrameX.Hotfix.Logic.ServerRole.Match;
using GameFrameX.Hotfix.Logic.Player.Friend;


/// <summary>
/// 测试
/// http://localhost:20001/game/api/test
/// </summary>
[HttpMessageMapping(typeof(TestHttpHandler), null, typeof(HttpTestResponse))]
[Description("测试通讯接口。没有实际用途")]
public sealed class TestHttpHandler : BaseHttpHandler
{
    /// <summary>
    /// 测试入口，支持普通回包与跨进程通讯示例。
    /// </summary>
    /// <param name="context"></param>
    /// <returns></returns>
    public override async Task<string> Action(HttpActionContext context)
    {
        var paramMap = context.Parameters;
        if (TryGetMode(paramMap, out var mode) && string.Equals(mode, "cross-process-friend", StringComparison.OrdinalIgnoreCase))
        {
            // C244 起好友查询统一经好友域桥（旧 Social Inner 转发链退役）；显式 playerId 为验证请求主体。
            return HandleFriendDomainQuery(paramMap, "cross-process-friend");
        }

        // C243 验证入口（C244 起与上一模式同路径）：好友域桥穿透复核（显式 playerId 为验证请求主体；
        // 作用域三元组仍由 lease 授权供给）。
        if (TryGetMode(paramMap, out var bridgeMode) && string.Equals(bridgeMode, "online-bridge-friend", StringComparison.OrdinalIgnoreCase))
        {
            return HandleFriendDomainQuery(paramMap, "online-bridge-friend");
        }

        // C245 验证入口：对局桥穿透复核（显式 playerId 为验证请求主体；roomId/round/gesture 定位对局与意图；
        // 作用域三元组仍由 lease 授权供给）。
        if (TryGetMode(paramMap, out var matchBridgeMode) && string.Equals(matchBridgeMode, "online-bridge-match", StringComparison.OrdinalIgnoreCase))
        {
            return HandleOnlineBridgeMatch(paramMap);
        }

        // C246 验证入口：匹配域桥穿透复核（显式 playerId 为验证请求主体；action 定位入队/取消/状态/入场；
        // 与 420 协议入口相同的薄适配器代码路径）。
        if (TryGetMode(paramMap, out var matchDomainMode) && string.Equals(matchDomainMode, "online-bridge-matchdomain", StringComparison.OrdinalIgnoreCase))
        {
            return HandleOnlineBridgeMatchDomain(paramMap);
        }


        var response = new HttpTestResponse
        {
            Message = "hello",
        };
        return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
    }

    /// <summary>
    /// 获取测试模式参数。
    /// </summary>
    /// <param name="parameters">请求参数</param>
    /// <param name="mode">模式值</param>
    /// <returns>是否获取成功</returns>
    private static bool TryGetMode(Dictionary<string, object> parameters, out string mode)
    {
        mode = string.Empty;
        if (!parameters.TryGetValue("mode", out var modeValue) || modeValue == null)
        {
            return false;
        }

        mode = modeValue.ToString() ?? string.Empty;
        return !string.IsNullOrWhiteSpace(mode);
    }

    /// <summary>
    /// 好友域桥穿透验证入口（C244）：以显式 <c>playerId</c> 为验证请求主体走域桥路径
    ///（与协议入口 ReqFriendList 相同的薄适配器代码路径）；playerId 缺省或非法（≤0）时显式回
    /// 会话无效错误码，不伪装成功。
    /// </summary>
    /// <param name="parameters">请求参数。</param>
    /// <param name="modeName">回显模式名。</param>
    /// <returns>序列化响应。</returns>
    private static string HandleFriendDomainQuery(Dictionary<string, object> parameters, string modeName)
    {
        long playerId = 0;
        if (parameters.TryGetValue("playerId", out var playerIdValue) && playerIdValue != null)
        {
            long.TryParse(playerIdValue.ToString(), out playerId);
        }

        // 写路径验证（C244）：action + peer 参数存在时走 660 族薄适配器代码路径（request/accept/reject/remove/relations）。
        if (parameters.TryGetValue("action", out var actionValue) && actionValue != null && !string.IsNullOrWhiteSpace(actionValue.ToString()))
        {
            return HandleFriendDomainAction(parameters, modeName, playerId, actionValue.ToString());
        }

        var bridgeResponse = new RespFriendList();
        if (playerId > 0)
        {
            FriendDomainBridge.QueryFriendListAsync(playerId, bridgeResponse).GetAwaiter().GetResult();
        }
        else
        {
            bridgeResponse.Friends = new List<FriendInfo>();
            bridgeResponse.ErrorCode = -(int)OnlineErrorCode.SessionInvalid;
        }

        var response = new HttpOnlineBridgeFriendResponse
        {
            Mode = modeName,
            PlayerId = playerId,
            BridgeEnabled = playerId > 0,
            ErrorCode = bridgeResponse.ErrorCode,
            FriendCount = bridgeResponse.Friends?.Count ?? 0,
            Friends = bridgeResponse.Friends ?? new List<FriendInfo>(),
        };
        return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
    }

    /// <summary>
    /// 好友域桥写路径验证入口（C244）：以显式 <c>playerId</c> 为验证请求主体、<c>peer</c> 为对方，
    /// <c>action</c> 路由 660 族薄适配器（SendRequest/Accept/Reject/Remove/ListRelations 同一映射代码路径）。
    /// </summary>
    /// <param name="parameters">请求参数。</param>
    /// <param name="modeName">回显模式名。</param>
    /// <param name="playerId">验证请求主体。</param>
    /// <param name="action">动作名。</param>
    /// <returns>序列化响应。</returns>
    private static string HandleFriendDomainAction(Dictionary<string, object> parameters, string modeName, long playerId, string action)
    {
        long peerId = 0;
        if (parameters.TryGetValue("peer", out var peerValue) && peerValue != null)
        {
            long.TryParse(peerValue.ToString(), out peerId);
        }

        var errorCode = playerId > 0 ? 0 : -(int)OnlineErrorCode.SessionInvalid;
        List<long> friends = null;
        List<long> incoming = null;
        List<long> outgoing = null;
        if (playerId > 0)
        {
            switch (action.ToLowerInvariant())
            {
                case "request":
                {
                    var response = new RespFriendSendRequest();
                    FriendDomainBridge.SendRequestAsync(playerId, peerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    break;
                }

                case "accept":
                {
                    var response = new RespFriendAccept();
                    FriendDomainBridge.AcceptAsync(playerId, peerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    break;
                }

                case "reject":
                {
                    var response = new RespFriendReject();
                    FriendDomainBridge.RejectAsync(playerId, peerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    break;
                }

                case "remove":
                {
                    var response = new RespFriendRemove();
                    FriendDomainBridge.RemoveAsync(playerId, peerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    break;
                }

                case "relations":
                {
                    var response = new RespFriendRelationList();
                    FriendDomainBridge.ListRelationsAsync(playerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    friends = response.Friends;
                    incoming = response.IncomingRequests;
                    outgoing = response.OutgoingRequests;
                    break;
                }

                default:
                    errorCode = -(int)OnlineErrorCode.ParameterInvalid;
                    break;
            }
        }

        var result = new HttpOnlineFriendActionResponse
        {
            Mode = modeName,
            Action = action,
            PlayerId = playerId,
            PeerId = peerId,
            ErrorCode = errorCode,
            Friends = friends,
            IncomingRequests = incoming,
            OutgoingRequests = outgoing,
        };
        return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(result));
    }

    /// <summary>
    /// 对局桥穿透验证入口（C245）：以显式 <c>playerId</c> 为验证请求主体、<c>roomId</c>/<c>round</c> 定位对局，
    /// <c>gesture</c> 提交出拳（缺省仅查询视图）——与协议入口相同的薄适配器代码路径；
    /// 桥开关未开启时显式回 <c>BridgeEnabled=false</c>，不伪装成功。
    /// </summary>
    /// <param name="parameters">请求参数。</param>
    /// <returns>序列化响应。</returns>
    private static string HandleOnlineBridgeMatch(Dictionary<string, object> parameters)
    {
        long playerId = 0;
        long roomId = 0;
        var round = 1;
        var gesture = 0;
        if (parameters.TryGetValue("playerId", out var playerIdValue) && playerIdValue != null)
        {
            long.TryParse(playerIdValue.ToString(), out playerId);
        }

        if (parameters.TryGetValue("roomId", out var roomIdValue) && roomIdValue != null)
        {
            long.TryParse(roomIdValue.ToString(), out roomId);
        }

        if (parameters.TryGetValue("round", out var roundValue) && int.TryParse(roundValue?.ToString(), out var parsedRound) && parsedRound > 0)
        {
            round = parsedRound;
        }

        if (parameters.TryGetValue("gesture", out var gestureValue) && int.TryParse(gestureValue?.ToString(), out var parsedGesture))
        {
            gesture = parsedGesture;
        }

        var bridgeEnabled = RockPaperScissorsMatchBridge.IsBridgeEnabled;
        var handled = false;
        OnlineMatchBridgeGameView view = null;
        var errorCode = 0;
        if (bridgeEnabled && playerId > 0 && roomId > 0)
        {
            if (gesture > 0)
            {
                var bridgeResponse = new RespSubmitRockPaperScissorsGesture();
                (handled, view) = RockPaperScissorsMatchBridge.TrySubmitGestureAsync(playerId, roomId, round, gesture, bridgeResponse).GetAwaiter().GetResult();
                errorCode = bridgeResponse.ErrorCode;
            }
            else
            {
                var bridgeResponse = new RespRockPaperScissorsGameInfo();
                (handled, view) = RockPaperScissorsMatchBridge.TryGetGameInfoAsync(playerId, roomId, round, bridgeResponse).GetAwaiter().GetResult();
                errorCode = bridgeResponse.ErrorCode;
            }
        }

        var response = new HttpOnlineBridgeMatchResponse
        {
            Mode = "online-bridge-match",
            PlayerId = playerId,
            RoomId = roomId,
            Round = round,
            BridgeEnabled = bridgeEnabled,
            Handled = handled,
            ErrorCode = errorCode,
            GameState = view == null ? null : view.State.ToString(),
            MatchId = view?.MatchId,
            ViewRound = view?.Round ?? 0,
            WinnerPlayerId = view?.WinnerPlayerId ?? 0,
            Players = view == null
                ? new List<HttpOnlineBridgeMatchPlayer>()
                : view.Players.Select(player => new HttpOnlineBridgeMatchPlayer
                {
                    PlayerId = player.PlayerId,
                    HasGesture = player.HasGesture,
                    Gesture = player.Gesture,
                    Wins = player.Wins,
                }).ToList(),
        };
        return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
    }

    /// <summary>
    /// 匹配域桥穿透验证入口（C246）：以显式 <c>playerId</c> 为验证请求主体、<c>action</c> 定位
    /// enqueue/cancel/status/enter（enter 携 <c>matchId</c>；enqueue 携 mode/rating）——
    /// 与 420 协议入口相同的薄适配器代码路径；桥不可用时显式回错误码，不伪装成功。
    /// </summary>
    /// <param name="parameters">请求参数。</param>
    /// <returns>序列化响应。</returns>
    private static string HandleOnlineBridgeMatchDomain(Dictionary<string, object> parameters)
    {
        long playerId = 0;
        long matchId = 0;
        var mode = 1;
        var rating = 1000;
        var action = "status";
        if (parameters.TryGetValue("playerId", out var playerIdValue) && playerIdValue != null)
        {
            long.TryParse(playerIdValue.ToString(), out playerId);
        }

        if (parameters.TryGetValue("matchId", out var matchIdValue) && matchIdValue != null)
        {
            long.TryParse(matchIdValue.ToString(), out matchId);
        }

        if (parameters.TryGetValue("mode", out var modeValue) && int.TryParse(modeValue?.ToString(), out var parsedMode) && parsedMode > 0)
        {
            mode = parsedMode;
        }

        if (parameters.TryGetValue("rating", out var ratingValue) && int.TryParse(ratingValue?.ToString(), out var parsedRating) && parsedRating > 0)
        {
            rating = parsedRating;
        }

        if (parameters.TryGetValue("action", out var actionValue) && actionValue != null && !string.IsNullOrWhiteSpace(actionValue.ToString()))
        {
            action = actionValue.ToString();
        }

        var errorCode = 0;
        var queuing = false;
        var started = false;
        if (playerId > 0)
        {
            switch (action.ToLowerInvariant())
            {
                case "enqueue":
                {
                    var response = new RespMatchEnqueue();
                    MatchDomainBridge.EnqueueAsync(playerId, new ReqMatchEnqueue { Mode = (MatchMode)mode, Rating = rating }, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    matchId = response.MatchId;
                    break;
                }

                case "cancel":
                {
                    var response = new RespMatchCancel();
                    MatchDomainBridge.CancelAsync(playerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    break;
                }

                case "enter":
                {
                    var response = new RespMatchEnter();
                    MatchDomainBridge.EnterAsync(playerId, new ReqMatchEnter { MatchId = matchId }, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    matchId = response.MatchId;
                    started = response.Started;
                    break;
                }

                default:
                {
                    var response = new RespMatchStatus();
                    MatchDomainBridge.StatusAsync(playerId, response).GetAwaiter().GetResult();
                    errorCode = response.ErrorCode;
                    queuing = response.Queuing;
                    matchId = response.MatchId;
                    break;
                }
            }
        }
        else
        {
            errorCode = (int)MatchErrorCode.NotLoggedIn;
        }

        var result = new HttpOnlineBridgeMatchDomainResponse
        {
            Mode = "online-bridge-matchdomain",
            Action = action,
            PlayerId = playerId,
            MatchId = matchId,
            Queuing = queuing,
            Started = started,
            ErrorCode = errorCode,
        };
        return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(result));
    }
}

public sealed class HttpOnlineBridgeMatchDomainResponse : HttpMessageResponseBase
{
    [Description("测试模式")] public string Mode { get; set; }
    [Description("动作名")] public string Action { get; set; }
    [Description("验证请求主体")] public long PlayerId { get; set; }
    [Description("数值对局标识（排队中为 0）")] public long MatchId { get; set; }
    [Description("是否排队中（status 动作）")] public bool Queuing { get; set; }
    [Description("对局是否运行态（enter 动作）")] public bool Started { get; set; }
    [Description("错误码")] public int ErrorCode { get; set; }
}

public sealed class HttpTestResponse : HttpMessageResponseBase
{
    [Description("返回信息")] public string Message { get; set; }
}

public sealed class HttpOnlineBridgeFriendResponse : HttpMessageResponseBase
{
    [Description("测试模式")] public string Mode { get; set; }
    [Description("验证请求主体")] public long PlayerId { get; set; }
    [Description("桥开关是否开启并接管")] public bool BridgeEnabled { get; set; }
    [Description("错误码")] public int ErrorCode { get; set; }
    [Description("好友数量")] public int FriendCount { get; set; }
    [Description("好友列表")] public List<FriendInfo> Friends { get; set; }
}

public sealed class HttpOnlineFriendActionResponse : HttpMessageResponseBase
{
    [Description("测试模式")] public string Mode { get; set; }
    [Description("动作名")] public string Action { get; set; }
    [Description("验证请求主体")] public long PlayerId { get; set; }
    [Description("对方玩家")] public long PeerId { get; set; }
    [Description("错误码")] public int ErrorCode { get; set; }
    [Description("好友列表（relations 动作）")] public List<long> Friends { get; set; }
    [Description("收到申请（relations 动作）")] public List<long> IncomingRequests { get; set; }
    [Description("发出申请（relations 动作）")] public List<long> OutgoingRequests { get; set; }
}

public sealed class HttpOnlineBridgeMatchResponse : HttpMessageResponseBase
{
    [Description("测试模式")] public string Mode { get; set; }
    [Description("验证请求主体")] public long PlayerId { get; set; }
    [Description("房间标识")] public long RoomId { get; set; }
    [Description("请求局号")] public int Round { get; set; }
    [Description("桥开关是否开启")] public bool BridgeEnabled { get; set; }
    [Description("桥是否接管本次请求")] public bool Handled { get; set; }
    [Description("错误码")] public int ErrorCode { get; set; }
    [Description("对局状态")] public string GameState { get; set; }
    [Description("平台对局标识")] public string MatchId { get; set; }
    [Description("视图局数")] public int ViewRound { get; set; }
    [Description("整局胜者（未终局为 0）")] public long WinnerPlayerId { get; set; }
    [Description("玩家视图（保密投影）")] public List<HttpOnlineBridgeMatchPlayer> Players { get; set; }
}

public sealed class HttpOnlineBridgeMatchPlayer
{
    [Description("玩家ID")] public long PlayerId { get; set; }
    [Description("本局是否已出拳")] public bool HasGesture { get; set; }
    [Description("出拳（未结算局为 0）")] public int Gesture { get; set; }
    [Description("累计胜局数")] public int Wins { get; set; }
}
