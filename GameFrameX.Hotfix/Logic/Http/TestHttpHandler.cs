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
            var friendComponentAgent = await ActorManager.GetComponentAgent<FriendComponentAgent>();
            var friendResponse = new RespFriendList();
            await friendComponentAgent.OnFriendList(null, new ReqFriendList(), friendResponse);
            var crossResponse = new HttpCrossProcessFriendResponse
            {
                Mode = mode,
                ErrorCode = friendResponse.ErrorCode,
                FriendCount = friendResponse.Friends?.Count ?? 0,
                Friends = friendResponse.Friends ?? new List<FriendInfo>(),
            };
            return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(crossResponse));
        }

        // C243 验证入口：好友查询桥穿透复核（显式 playerId 为验证请求主体；作用域三元组仍由 lease 授权供给）。
        if (TryGetMode(paramMap, out var bridgeMode) && string.Equals(bridgeMode, "online-bridge-friend", StringComparison.OrdinalIgnoreCase))
        {
            return HandleOnlineBridgeFriend(paramMap);
        }

        // C245 验证入口：对局桥穿透复核（显式 playerId 为验证请求主体；roomId/round/gesture 定位对局与意图；
        // 作用域三元组仍由 lease 授权供给）。
        if (TryGetMode(paramMap, out var matchBridgeMode) && string.Equals(matchBridgeMode, "online-bridge-match", StringComparison.OrdinalIgnoreCase))
        {
            return HandleOnlineBridgeMatch(paramMap);
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
    /// 好友查询桥穿透验证入口（C243）：以显式 <c>playerId</c> 为验证请求主体走桥路径
    ///（与协议入口 ReqFriendList 相同的薄适配器代码路径）；桥开关未开启时显式回
    /// <c>BridgeEnabled=false</c>，不伪装成功。
    /// </summary>
    /// <param name="parameters">请求参数。</param>
    /// <returns>序列化响应。</returns>
    private static string HandleOnlineBridgeFriend(Dictionary<string, object> parameters)
    {
        long playerId = 0;
        if (parameters.TryGetValue("playerId", out var playerIdValue) && playerIdValue != null)
        {
            long.TryParse(playerIdValue.ToString(), out playerId);
        }

        var bridgeResponse = new RespFriendList();
        var handled = playerId > 0 && FriendQueryBridge.TryQueryFriendListAsync(playerId, bridgeResponse).GetAwaiter().GetResult();
        var response = new HttpOnlineBridgeFriendResponse
        {
            Mode = "online-bridge-friend",
            PlayerId = playerId,
            BridgeEnabled = handled,
            ErrorCode = handled ? bridgeResponse.ErrorCode : 0,
            FriendCount = handled ? bridgeResponse.Friends?.Count ?? 0 : 0,
            Friends = handled ? bridgeResponse.Friends ?? new List<FriendInfo>() : new List<FriendInfo>(),
        };
        return HttpJsonResultData<string>.SuccessString(JsonHelper.Serialize(response));
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
}

public sealed class HttpTestResponse : HttpMessageResponseBase
{
    [Description("返回信息")] public string Message { get; set; }
}

public sealed class HttpCrossProcessFriendResponse : HttpMessageResponseBase
{
    [Description("测试模式")] public string Mode { get; set; }
    [Description("错误码")] public int ErrorCode { get; set; }
    [Description("好友数量")] public int FriendCount { get; set; }
    [Description("好友列表")] public List<FriendInfo> Friends { get; set; }
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
