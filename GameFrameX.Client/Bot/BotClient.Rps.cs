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
//   please refer to the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development of this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Client.Bot;

/// <summary>
/// <see cref="BotClient"/> 的石头剪刀布（RPS）房间对局脚本流 partial 实现。
/// 通过 <c>--rps-role=owner|joiner</c> 启用；未启用时不参与任何消息处理与轮询，行为与历史完全一致。
/// </summary>
public sealed partial class BotClient
{
    /// <summary>
    /// RPS 脚本流阶段。
    /// </summary>
    private enum RpsStage
    {
        /// <summary>未启用/未开始。</summary>
        None = 0,

        /// <summary>owner：已发创建房间，等待 RespCreateRoom。</summary>
        WaitCreate = 1,

        /// <summary>owner：房间已创建，等待成员满员（轮询房间列表 + NotifyRoomChanged）。</summary>
        WaitMemberFull = 2,

        /// <summary>joiner：轮询房间列表，等待出现可加入房间。</summary>
        WaitRoomFound = 3,

        /// <summary>joiner：已发加入房间，等待 RespJoinRoom。</summary>
        WaitJoin = 4,

        /// <summary>双方：已在房间，等待 owner 开局（NotifyRoomChanged Started）。</summary>
        WaitStart = 5,

        /// <summary>对局进行中：按局出拳，直到终局。</summary>
        Playing = 6,

        /// <summary>终局/失败，流程结束。</summary>
        Finished = 7,
    }

    /// <summary>连续无进展轮询上限（500ms 间隔下约 60 秒）。</summary>
    private const int RpsMaxNoProgressPolls = 120;

    private readonly object _rpsGate = new();
    private readonly TaskCompletionSource _rpsCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private RpsStage _rpsStage;
    private long _rpsRoomId;
    private RoomInfo _rpsRoomInfo;
    private RockPaperScissorsGameInfo _rpsGameInfo;
    private int _rpsLastSubmittedRound;
    private int _rpsNoProgressCount;
    private int _rpsPollStarted;
    private int _rpsExitCode;
    private volatile bool _rpsFinished;

    /// <summary>
    /// RPS 脚本流完成任务。仅在 <c>--rps-role</c> 启用时会完成；供 Program 等待并决定退出码。
    /// </summary>
    public Task RpsCompletion
    {
        get
        {
            return _rpsCompletion.Task;
        }
    }

    /// <summary>
    /// RPS 脚本流退出码：0 = 终局正常完成；非 0 = 失败/超时。仅在 <see cref="RpsCompletion"/> 完成后有意义。
    /// </summary>
    public int RpsExitCode
    {
        get
        {
            return _rpsExitCode;
        }
    }

    /// <summary>
    /// 当前机器人是否为 RPS 房主角色。
    /// </summary>
    private bool IsRpsOwner
    {
        get
        {
            return string.Equals(_options.RpsRole, BotRunOptions.RpsRoleOwner, StringComparison.OrdinalIgnoreCase);
        }
    }

    #region RPS 消息接收

    private void OnRpsCreateRoomResponse(RespCreateRoom msg)
    {
        if (!IsRpsActiveStage(RpsStage.WaitCreate))
        {
            return;
        }

        if (msg.ErrorCode != 0 || msg.Room == null)
        {
            FailRps(Localization.Keys.Client.Bot.RpsCreateRoomFailed, msg.ErrorCode);
            return;
        }

        lock (_rpsGate)
        {
            _rpsRoomId = msg.Room.RoomId;
            _rpsRoomInfo = msg.Room;
            _rpsStage = RpsStage.WaitMemberFull;
            _rpsNoProgressCount = 0;
        }

        // Localization: Client.Bot.RpsRoomCreated - 机器人-{0}RPS场景-创建房间成功，房间ID:{1}，等待对手加入。
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsRoomCreated, m_BotName, _rpsRoomId));
    }

    private void OnRpsRoomListResponse(RespRoomList msg)
    {
        if (!IsRpsActiveStage(RpsStage.WaitMemberFull) && !IsRpsActiveStage(RpsStage.WaitRoomFound))
        {
            return;
        }

        if (msg.ErrorCode != 0)
        {
            CountRpsNoProgress();
            return;
        }

        if (IsRpsActiveStage(RpsStage.WaitRoomFound))
        {
            HandleJoinerRoomList(msg.Rooms);
            return;
        }

        HandleOwnerRoomList(msg.Rooms);
    }

    private void HandleOwnerRoomList(List<RoomInfo> rooms)
    {
        var room = rooms?.FirstOrDefault(r => r.RoomId == _rpsRoomId);
        if (room == null)
        {
            CountRpsNoProgress();
            return;
        }

        lock (_rpsGate)
        {
            _rpsRoomInfo = room;
        }

        if (IsRpsRoomMemberFull(room) || room.Status >= RoomStatus.Ready)
        {
            StartRpsGame();
            return;
        }

        CountRpsNoProgress();
    }

    private void HandleJoinerRoomList(List<RoomInfo> rooms)
    {
        var room = rooms?.FirstOrDefault(IsJoinableRpsRoom);
        if (room == null)
        {
            CountRpsNoProgress();
            return;
        }

        lock (_rpsGate)
        {
            _rpsRoomId = room.RoomId;
            _rpsRoomInfo = room;
            _rpsStage = RpsStage.WaitJoin;
            _rpsNoProgressCount = 0;
        }

        SendToTransport(new ReqJoinRoom { RoomId = room.RoomId });
    }

    private void OnRpsJoinRoomResponse(RespJoinRoom msg)
    {
        if (!IsRpsActiveStage(RpsStage.WaitJoin))
        {
            return;
        }

        if (msg.ErrorCode != 0 || msg.Room == null)
        {
            FailRps(Localization.Keys.Client.Bot.RpsJoinRoomFailed, _rpsRoomId, msg.ErrorCode);
            return;
        }

        lock (_rpsGate)
        {
            _rpsRoomInfo = msg.Room;
            _rpsStage = msg.Room.Status == RoomStatus.Playing ? RpsStage.Playing : RpsStage.WaitStart;
            _rpsNoProgressCount = 0;
        }

        // Localization: Client.Bot.RpsJoinedRoom - 机器人-{0}RPS场景-已加入房间:{1}，等待开局。
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsJoinedRoom, m_BotName, _rpsRoomId));
        SendToTransport(new ReqRockPaperScissorsGameInfo { RoomId = _rpsRoomId });
    }

    private void OnRpsStartRoomGameResponse(RespStartRoomGame msg)
    {
        if (!IsRpsActiveStage(RpsStage.WaitMemberFull))
        {
            return;
        }

        if (msg.ErrorCode != 0 || msg.Room == null)
        {
            FailRps(Localization.Keys.Client.Bot.RpsStartGameFailed, msg.ErrorCode);
            return;
        }

        EnterRpsPlaying(msg.Room);
    }

    private void OnRpsRoomChangedNotify(NotifyRoomChanged msg)
    {
        if (msg?.Room == null || msg.Room.RoomId != _rpsRoomId)
        {
            return;
        }

        if (IsRpsActiveStage(RpsStage.WaitMemberFull) && IsRpsOwner && (IsRpsRoomMemberFull(msg.Room) || msg.Room.Status >= RoomStatus.Ready))
        {
            lock (_rpsGate)
            {
                _rpsRoomInfo = msg.Room;
            }

            StartRpsGame();
            return;
        }

        if (msg.Room.Status == RoomStatus.Playing && (IsRpsActiveStage(RpsStage.WaitStart) || IsRpsActiveStage(RpsStage.WaitJoin)))
        {
            EnterRpsPlaying(msg.Room);
            return;
        }

        if (msg.Room.Status is RoomStatus.Settling or RoomStatus.Settled or RoomStatus.Closed or RoomStatus.Disbanded)
        {
            lock (_rpsGate)
            {
                _rpsRoomInfo = msg.Room;
            }

            TryFinishRps();
        }
    }

    private void OnRpsSubmitGestureResponse(RespSubmitRockPaperScissorsGesture msg)
    {
        if (msg.ErrorCode != 0)
        {
            FailRps(Localization.Keys.Client.Bot.RpsSubmitFailed, msg.ErrorCode);
            return;
        }

        ApplyRpsGameInfo(msg.GameInfo);
    }

    private void OnRpsGameInfoResponse(object msg)
    {
        var gameInfo = msg switch
        {
            RespRockPaperScissorsGameInfo resp when resp.ErrorCode == 0 => resp.GameInfo,
            NotifyRockPaperScissorsGameChanged notify => notify.GameInfo,
            _ => null,
        };
        if (gameInfo == null)
        {
            CountRpsNoProgress();
            return;
        }

        ApplyRpsGameInfo(gameInfo);
    }

    private void ApplyRpsGameInfo(RockPaperScissorsGameInfo gameInfo)
    {
        if (gameInfo == null || gameInfo.RoomId != _rpsRoomId)
        {
            return;
        }

        lock (_rpsGate)
        {
            _rpsGameInfo = gameInfo;
            _rpsNoProgressCount = 0;
        }

        if (gameInfo.WinnerPlayerId != 0)
        {
            TryFinishRps();
            return;
        }

        if (IsRpsActiveStage(RpsStage.Playing))
        {
            TrySubmitRpsGesture(gameInfo);
            return;
        }

        if (IsRpsActiveStage(RpsStage.WaitStart) && gameInfo.Round > 0)
        {
            lock (_rpsGate)
            {
                _rpsStage = RpsStage.Playing;
            }

            TrySubmitRpsGesture(gameInfo);
        }
    }

    #endregion

    #region RPS 流程驱动

    private void StartRpsScenario()
    {
        if (_playerId <= 0 || !_options.HasRpsRole || _options.RpsGestures.Count == 0)
        {
            // Localization: Client.Bot.RpsScenarioInvalidOptions - 机器人-{0}RPS场景启动失败，玩家ID:{1}，出拳序列数量:{2}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsScenarioInvalidOptions, m_BotName, _playerId, _options.RpsGestures.Count));
            FinishRps(1);
            return;
        }

        lock (_rpsGate)
        {
            _rpsStage = IsRpsOwner ? RpsStage.WaitCreate : RpsStage.WaitRoomFound;
        }

        if (IsRpsOwner)
        {
            SendToTransport(new ReqCreateRoom
            {
                GameType = GameType.RockPaperScissors,
                Name = $"{m_BotName}-RPS",
            });
        }
        else
        {
            // Localization: Client.Bot.RpsJoinerWaiting - 机器人-{0}RPS场景-开始轮询可加入的石头剪刀布房间。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsJoinerWaiting, m_BotName));
        }

        StartRpsPollLoop();
    }

    /// <summary>
    /// 启动轮询后台任务。整个 RPS 流程共用一个循环：按当前阶段选择请求（房间列表 / 对局信息），终局后自然退出。
    /// </summary>
    private void StartRpsPollLoop()
    {
        if (Interlocked.CompareExchange(ref _rpsPollStarted, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_rpsFinished)
                {
                    await Task.Delay(Math.Max(50, _options.RpsPollIntervalMs));
                    if (_rpsFinished)
                    {
                        return;
                    }

                    RpsStage stage;
                    long roomId;
                    lock (_rpsGate)
                    {
                        stage = _rpsStage;
                        roomId = _rpsRoomId;
                    }

                    switch (stage)
                    {
                        case RpsStage.WaitCreate:
                        case RpsStage.WaitMemberFull:
                        case RpsStage.WaitRoomFound:
                        case RpsStage.WaitJoin:
                        case RpsStage.WaitStart:
                            SendToTransport(new ReqRoomList
                            {
                                GameType = GameType.RockPaperScissors,
                                IncludeClosed = false,
                            });
                            break;
                        case RpsStage.Playing:
                            SendToTransport(new ReqRockPaperScissorsGameInfo { RoomId = roomId });
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                // Localization: Client.Bot.RpsPollLoopError - 机器人-{0}RPS场景-轮询循环异常:{1}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsPollLoopError, m_BotName, e.Message));
                FinishRps(1);
            }
        });
    }

    private void StartRpsGame()
    {
        lock (_rpsGate)
        {
            if (_rpsStage != RpsStage.WaitMemberFull)
            {
                return;
            }

            _rpsStage = RpsStage.WaitStart;
        }

        SendToTransport(new ReqStartRoomGame { RoomId = _rpsRoomId });
    }

    private void EnterRpsPlaying(RoomInfo room)
    {
        lock (_rpsGate)
        {
            _rpsRoomInfo = room;
            _rpsStage = RpsStage.Playing;
            _rpsNoProgressCount = 0;
        }

        // Localization: Client.Bot.RpsGameStarted - 机器人-{0}RPS场景-对局开始，房间ID:{1}。
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsGameStarted, m_BotName, _rpsRoomId));
        SendToTransport(new ReqRockPaperScissorsGameInfo { RoomId = _rpsRoomId });
    }

    /// <summary>
    /// 出拳：当前局自己未出拳且对局进行中时，按序列取当前局的拳并发送。
    /// </summary>
    private void TrySubmitRpsGesture(RockPaperScissorsGameInfo gameInfo)
    {
        if (gameInfo.Round <= 0)
        {
            return;
        }

        var self = gameInfo.Players?.FirstOrDefault(p => p.PlayerId == _playerId);
        if (self == null || self.HasGesture)
        {
            return;
        }

        lock (_rpsGate)
        {
            if (_rpsLastSubmittedRound == gameInfo.Round)
            {
                return;
            }

            _rpsLastSubmittedRound = gameInfo.Round;
        }

        var gestureValue = _options.RpsGestures[(gameInfo.Round - 1) % _options.RpsGestures.Count];
        var gesture = (RockPaperScissorsGesture)gestureValue;

        // Localization: Client.Bot.RpsSubmitGesture - 机器人-{0}RPS场景-第{1}局出拳:{2}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsSubmitGesture, m_BotName, gameInfo.Round, gesture));
        SendToTransport(new ReqSubmitRockPaperScissorsGesture
        {
            RoomId = _rpsRoomId,
            Gesture = gesture,
        });
    }

    /// <summary>
    /// 终局判定：对局信息 WinnerPlayerId != 0，或房间进入结算/关闭/解散。
    /// </summary>
    private void TryFinishRps()
    {
        RockPaperScissorsGameInfo gameInfo;
        lock (_rpsGate)
        {
            gameInfo = _rpsGameInfo;
        }

        if (gameInfo != null && gameInfo.WinnerPlayerId != 0)
        {
            var playersText = string.Join(",", gameInfo.Players?.Select(p => $"{p.PlayerId}={(p.HasGesture ? p.Gesture.ToString() : "None")}") ?? Array.Empty<string>());
            // Localization: Client.Bot.RpsGameFinished - 机器人-{0}RPS场景-对局终局，总局数:{1}，胜者玩家ID:{2}，玩家出拳:{3}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsGameFinished, m_BotName, gameInfo.Round, gameInfo.WinnerPlayerId, playersText));
            FinishRps(0);
            return;
        }

        var status = _rpsRoomInfo?.Status ?? RoomStatus.None;
        if (status is RoomStatus.Settling or RoomStatus.Settled or RoomStatus.Closed or RoomStatus.Disbanded)
        {
            // Localization: Client.Bot.RpsGameFinishedByRoomStatus - 机器人-{0}RPS场景-房间状态终局:{1}，总局数:{2}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsGameFinishedByRoomStatus, m_BotName, status, gameInfo?.Round ?? 0));
            FinishRps(0);
        }
    }

    /// <summary>
    /// 轮询无进展计数：任何推进（阶段变化/新 GameInfo）都会清零；达到上限则失败退出。
    /// </summary>
    private void CountRpsNoProgress()
    {
        int count;
        lock (_rpsGate)
        {
            if (_rpsFinished)
            {
                return;
            }

            _rpsNoProgressCount++;
            count = _rpsNoProgressCount;
        }

        if (count >= RpsMaxNoProgressPolls)
        {
            // Localization: Client.Bot.RpsPollNoProgress - 机器人-{0}RPS场景-连续{1}次轮询无进展，终止。
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.RpsPollNoProgress, m_BotName, count));
            FinishRps(1);
        }
    }

    private void FailRps(string localizationKey, params object[] args)
    {
        // Localization: 通过传入的键记录失败原因（键均为 Client.Bot.Rps* 系列）
        LogHelper.Error(LocalizationService.GetString(localizationKey, args));
        FinishRps(1);
    }

    private void FinishRps(int exitCode)
    {
        lock (_rpsGate)
        {
            if (_rpsFinished)
            {
                return;
            }

            _rpsFinished = true;
            _rpsStage = RpsStage.Finished;
            _rpsExitCode = exitCode;
        }

        DisconnectTransport();
        _rpsCompletion.TrySetResult();
    }

    private bool IsRpsActiveStage(RpsStage stage)
    {
        lock (_rpsGate)
        {
            return _rpsStage == stage && !_rpsFinished;
        }
    }

    private static bool IsRpsRoomMemberFull(RoomInfo room)
    {
        var max = room.MaxPlayerCount > 0 ? room.MaxPlayerCount : 2;
        return room.PlayerCount >= max;
    }

    private static bool IsJoinableRpsRoom(RoomInfo room)
    {
        if (room.GameType != GameType.RockPaperScissors)
        {
            return false;
        }

        if (room.Status is not (RoomStatus.Waiting or RoomStatus.Ready))
        {
            return false;
        }

        var max = room.MaxPlayerCount > 0 ? room.MaxPlayerCount : 2;
        return room.PlayerCount < max;
    }

    #endregion
}
