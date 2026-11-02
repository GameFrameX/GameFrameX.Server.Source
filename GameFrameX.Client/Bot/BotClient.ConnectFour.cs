// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and international regulations.
// 
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source tree for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规的禁止行为！
//  or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Client.Bot;

/// <summary>
/// <see cref="BotClient"/> 的四子棋房间对局脚本流 partial 实现（加入者角色）。
/// 通过 <c>--scenario=connectfour</c> 启用；未启用时不参与任何消息处理与轮询，行为与历史完全一致。
/// <para>
/// 落子采用中心优先的确定性列偏好（3,2,4,1,5,0,6 取第一个未满列）——与联调冒烟对局中真人玩家的
/// 策略一致，在该策略组合下先手必胜，供双客户端联调（Unity 冒烟等）作为确定性玩家 B。
/// 退出条件：完成指定局数（缺省 2 局，<c>--connectfour-exit-rounds</c> 可调）后的终局。
/// </para>
/// </summary>
public sealed partial class BotClient
{
    /// <summary>
    /// 四子棋脚本流阶段。
    /// </summary>
    private enum ConnectFourStage
    {
        /// <summary>未启用/未开始。</summary>
        None = 0,

        /// <summary>加入者：轮询房间列表，等待出现可加入房间。</summary>
        WaitRoomFound = 1,

        /// <summary>加入者：已发加入房间，等待 RespJoinRoom。</summary>
        WaitJoin = 2,

        /// <summary>已在房间，等待房主开局（NotifyRoomChanged Started）。</summary>
        WaitStart = 3,

        /// <summary>对局进行中：轮到该玩家时按确定性列偏好落子，直到完成指定局数。</summary>
        Playing = 4,

        /// <summary>终局/失败，流程结束。</summary>
        Finished = 5,
    }

    /// <summary>连续无进展轮询上限（500ms 间隔下约 120 秒，覆盖双局与断线重连窗口）。</summary>
    private const int ConnectFourMaxNoProgressPolls = 240;

    /// <summary>棋盘列数。</summary>
    private const int ConnectFourColumnCount = 7;

    /// <summary>棋盘行数。</summary>
    private const int ConnectFourRowCount = 6;

    /// <summary>中心优先的确定性落子列偏好（与联调冒烟对局中真人玩家策略一致）。</summary>
    private static readonly int[] ConnectFourColumnPreference = { 3, 2, 4, 1, 5, 0, 6 };

    private readonly object _connectFourGate = new();
    private readonly TaskCompletionSource _connectFourCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private ConnectFourStage _connectFourStage;
    private long _connectFourRoomId;
    private RoomInfo _connectFourRoomInfo;
    private ConnectFourGameInfo _connectFourGameInfo;
    private int _connectFourSubmittedRound = -1;
    private int _connectFourSubmittedMoveCount = -1;
    private int _connectFourLoggedFinishedRound;
    private int _connectFourNoProgressCount;
    private int _connectFourPollStarted;
    private int _connectFourExitCode;
    private volatile bool _connectFourFinished;

    /// <summary>
    /// 四子棋脚本流完成任务。仅在 <c>--scenario=connectfour</c> 启用时会完成；供 Program 等待并决定退出码。
    /// </summary>
    public Task ConnectFourCompletion
    {
        get
        {
            return _connectFourCompletion.Task;
        }
    }

    /// <summary>
    /// 四子棋脚本流退出码：0 = 完成指定局数正常结束；非 0 = 失败/超时。仅在 <see cref="ConnectFourCompletion"/> 完成后有意义。
    /// </summary>
    public int ConnectFourExitCode
    {
        get
        {
            return _connectFourExitCode;
        }
    }

    #region 四子棋消息接收

    private void OnConnectFourRoomListResponse(RespRoomList msg)
    {
        if (!IsConnectFourActiveStage(ConnectFourStage.WaitRoomFound))
        {
            return;
        }

        if (msg.ErrorCode != 0)
        {
            CountConnectFourNoProgress();
            return;
        }

        var room = msg.Rooms?.FirstOrDefault(IsJoinableConnectFourRoom);
        if (room == null)
        {
            CountConnectFourNoProgress();
            return;
        }

        lock (_connectFourGate)
        {
            _connectFourRoomId = room.RoomId;
            _connectFourRoomInfo = room;
            _connectFourStage = ConnectFourStage.WaitJoin;
            _connectFourNoProgressCount = 0;
        }

        SendToTransport(new ReqJoinRoom { RoomId = room.RoomId });
    }

    private void OnConnectFourJoinRoomResponse(RespJoinRoom msg)
    {
        if (!IsConnectFourActiveStage(ConnectFourStage.WaitJoin))
        {
            return;
        }

        if (msg.ErrorCode != 0 || msg.Room == null)
        {
            // 房间被满员/关闭等竞态：回到找房阶段继续轮询，由无进展计数兜底。
            // Localization: Client.Bot.ConnectFourJoinRoomFailed - 机器人-{0}四子棋场景-加入房间失败，房间ID:{1}，错误码:{2}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourJoinRoomFailed, m_BotName, _connectFourRoomId, msg.ErrorCode));
            lock (_connectFourGate)
            {
                _connectFourStage = ConnectFourStage.WaitRoomFound;
            }

            CountConnectFourNoProgress();
            return;
        }

        lock (_connectFourGate)
        {
            _connectFourRoomInfo = msg.Room;
            _connectFourStage = msg.Room.Status == RoomStatus.Playing ? ConnectFourStage.Playing : ConnectFourStage.WaitStart;
            _connectFourNoProgressCount = 0;
        }

        // Localization: Client.Bot.ConnectFourJoinedRoom - 机器人-{0}四子棋场景-已加入房间:{1}，等待开局。
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourJoinedRoom, m_BotName, _connectFourRoomId));
        SendToTransport(new ReqConnectFourGameInfo { RoomId = _connectFourRoomId });
    }

    private void OnConnectFourRoomChangedNotify(NotifyRoomChanged msg)
    {
        if (msg?.Room == null || msg.Room.RoomId != _connectFourRoomId)
        {
            return;
        }

        if (msg.Room.Status == RoomStatus.Playing
            && (IsConnectFourActiveStage(ConnectFourStage.WaitStart) || IsConnectFourActiveStage(ConnectFourStage.WaitJoin)))
        {
            lock (_connectFourGate)
            {
                _connectFourRoomInfo = msg.Room;
                _connectFourStage = ConnectFourStage.Playing;
                _connectFourNoProgressCount = 0;
            }

            // Localization: Client.Bot.ConnectFourGameStarted - 机器人-{0}四子棋场景-对局开始，房间ID:{1}。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourGameStarted, m_BotName, _connectFourRoomId));
            SendToTransport(new ReqConnectFourGameInfo { RoomId = _connectFourRoomId });
            return;
        }

        lock (_connectFourGate)
        {
            _connectFourRoomInfo = msg.Room;
        }
    }

    private void OnConnectFourGameInfoResponse(object msg)
    {
        var gameInfo = msg switch
        {
            RespConnectFourGameInfo resp when resp.ErrorCode == 0 => resp.GameInfo,
            NotifyConnectFourGameChanged notify => notify.GameInfo,
            _ => null,
        };
        if (gameInfo == null)
        {
            CountConnectFourNoProgress();
            return;
        }

        ApplyConnectFourGameInfo(gameInfo);
    }

    private void OnConnectFourMoveResponse(RespSubmitConnectFourMove msg)
    {
        if (msg.ErrorCode != 0)
        {
            // 回合竞态（对手抢先/视图滞后）按无进展处理：等待下一次轮询对齐后重试，不立即失败。
            CountConnectFourNoProgress();
            return;
        }

        ApplyConnectFourGameInfo(msg.GameInfo);
    }

    private void ApplyConnectFourGameInfo(ConnectFourGameInfo gameInfo)
    {
        if (gameInfo == null || gameInfo.RoomId != _connectFourRoomId)
        {
            return;
        }

        lock (_connectFourGate)
        {
            _connectFourGameInfo = gameInfo;
            _connectFourNoProgressCount = 0;
        }

        var isGameOver = gameInfo.WinnerPlayerId != 0 || gameInfo.IsDraw;
        if (isGameOver)
        {
            if (gameInfo.Round > _connectFourLoggedFinishedRound)
            {
                _connectFourLoggedFinishedRound = gameInfo.Round;
                // Localization: Client.Bot.ConnectFourRoundFinished - 机器人-{0}四子棋场景-第{1}局终局，胜者:{2}，平局:{3}
                LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourRoundFinished, m_BotName, gameInfo.Round, gameInfo.WinnerPlayerId, gameInfo.IsDraw));
            }

            if (gameInfo.Round >= _options.ConnectFourExitRounds)
            {
                // Localization: Client.Bot.ConnectFourScenarioCompleted - 机器人-{0}四子棋场景执行完成（共{1}局）。
                LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourScenarioCompleted, m_BotName, gameInfo.Round));
                FinishConnectFour(0);
                return;
            }

            // 未达退出局数：留在 Playing 继续轮询，等待房主「再来一局」开启新局。
            return;
        }

        if (IsConnectFourActiveStage(ConnectFourStage.WaitStart) && gameInfo.Round > 0 && gameInfo.CurrentPlayerId != 0)
        {
            lock (_connectFourGate)
            {
                _connectFourStage = ConnectFourStage.Playing;
            }
        }

        if (IsConnectFourActiveStage(ConnectFourStage.Playing) && gameInfo.CurrentPlayerId == _playerId)
        {
            TrySubmitConnectFourMove(gameInfo);
        }
    }

    #endregion

    #region 四子棋流程驱动

    private void StartConnectFourScenario()
    {
        if (_playerId <= 0)
        {
            FinishConnectFour(1);
            return;
        }

        lock (_connectFourGate)
        {
            _connectFourStage = ConnectFourStage.WaitRoomFound;
        }

        // Localization: Client.Bot.ConnectFourJoinerWaiting - 机器人-{0}四子棋场景-开始轮询可加入的四子棋房间。
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourJoinerWaiting, m_BotName));
        StartConnectFourPollLoop();
    }

    /// <summary>
    /// 启动轮询后台任务。整个四子棋流程共用一个循环：按当前阶段选择请求（房间列表 / 对局信息），完成后自然退出。
    /// </summary>
    private void StartConnectFourPollLoop()
    {
        if (Interlocked.CompareExchange(ref _connectFourPollStarted, 1, 0) != 0)
        {
            return;
        }

        _ = Task.Run(async () =>
        {
            try
            {
                while (!_connectFourFinished)
                {
                    await Task.Delay(Math.Max(50, _options.ConnectFourPollIntervalMs));
                    if (_connectFourFinished)
                    {
                        return;
                    }

                    ConnectFourStage stage;
                    long roomId;
                    lock (_connectFourGate)
                    {
                        stage = _connectFourStage;
                        roomId = _connectFourRoomId;
                    }

                    switch (stage)
                    {
                        case ConnectFourStage.WaitRoomFound:
                        case ConnectFourStage.WaitJoin:
                        case ConnectFourStage.WaitStart:
                            SendToTransport(new ReqRoomList
                            {
                                GameType = GameType.ConnectFour,
                                IncludeClosed = false,
                            });
                            break;
                        case ConnectFourStage.Playing:
                            SendToTransport(new ReqConnectFourGameInfo { RoomId = roomId });
                            break;
                    }
                }
            }
            catch (Exception e)
            {
                // Localization: Client.Bot.ConnectFourPollLoopError - 机器人-{0}四子棋场景-轮询循环异常:{1}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourPollLoopError, m_BotName, e.Message));
                FinishConnectFour(1);
            }
        });
    }

    /// <summary>
    /// 落子：轮到自己且对局进行中时，按中心优先列偏好取第一个未满列发送（以 局数+落子总数 去重，防竞态重发）。
    /// </summary>
    private void TrySubmitConnectFourMove(ConnectFourGameInfo gameInfo)
    {
        lock (_connectFourGate)
        {
            if (_connectFourSubmittedRound == gameInfo.Round && _connectFourSubmittedMoveCount == gameInfo.MoveCount)
            {
                return;
            }

            _connectFourSubmittedRound = gameInfo.Round;
            _connectFourSubmittedMoveCount = gameInfo.MoveCount;
        }

        var column = PickConnectFourColumn(gameInfo);
        if (column < 0)
        {
            return;
        }

        // Localization: Client.Bot.ConnectFourMoveSubmitted - 机器人-{0}四子棋场景-第{1}局落子列:{2}（第{3}手）
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourMoveSubmitted, m_BotName, gameInfo.Round, column, gameInfo.MoveCount + 1));
        SendToTransport(new ReqSubmitConnectFourMove
        {
            RoomId = _connectFourRoomId,
            Column = column,
        });
    }

    /// <summary>
    /// 确定性策略：按中心优先列偏好取第一个仍有空格的列（棋盘行主序，row 0 为底行）。
    /// </summary>
    private static int PickConnectFourColumn(ConnectFourGameInfo gameInfo)
    {
        var board = gameInfo.Board;
        if (board == null || board.Count < ConnectFourColumnCount * ConnectFourRowCount)
        {
            return -1;
        }

        foreach (var column in ConnectFourColumnPreference)
        {
            for (var row = ConnectFourRowCount - 1; row >= 0; row--)
            {
                if (board[row * ConnectFourColumnCount + column] == 0)
                {
                    return column;
                }
            }
        }

        return -1;
    }

    /// <summary>
    /// 轮询无进展计数：任何推进（新 GameInfo / 阶段变化）都会清零；达到上限则失败退出。
    /// </summary>
    private void CountConnectFourNoProgress()
    {
        int count;
        lock (_connectFourGate)
        {
            if (_connectFourFinished)
            {
                return;
            }

            _connectFourNoProgressCount++;
            count = _connectFourNoProgressCount;
        }

        if (count >= ConnectFourMaxNoProgressPolls)
        {
            // Localization: Client.Bot.ConnectFourPollNoProgress - 机器人-{0}四子棋场景-连续{1}次轮询无进展，终止。
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Client.Bot.ConnectFourPollNoProgress, m_BotName, count));
            FinishConnectFour(1);
        }
    }

    private void FinishConnectFour(int exitCode)
    {
        lock (_connectFourGate)
        {
            if (_connectFourFinished)
            {
                return;
            }

            _connectFourFinished = true;
            _connectFourStage = ConnectFourStage.Finished;
            _connectFourExitCode = exitCode;
        }

        DisconnectTransport();
        _connectFourCompletion.TrySetResult();
    }

    private bool IsConnectFourActiveStage(ConnectFourStage stage)
    {
        lock (_connectFourGate)
        {
            return _connectFourStage == stage && !_connectFourFinished;
        }
    }

    private static bool IsJoinableConnectFourRoom(RoomInfo room)
    {
        if (room.GameType != GameType.ConnectFour)
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
