// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏公共秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of any legal persons as prohibited by law!
//   因基于本项目二次开发所产生的一切纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织不承担任何责任，开发者应自行承担。
//   The organization and contributors shall not be responsible; developers shall bear the responsibility themselves.
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Bridges;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Hotfix.Logic.Game.RockPaperScissors;
using GameFrameX.Proto.Proto;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局样例桥 Hotfix 薄适配器测试：开关关闭不接管、lease 失效显式错误码、
    /// 契约失败码映射、异常兜底、作用域与参数透传、视图字段级映射（出拳保密 / Wins 不外发）
    /// 与 Room 状态映射决策矩阵。
    /// </summary>
    public class RockPaperScissorsMatchBridgeTests : IDisposable
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 1;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 1001;

        /// <summary>请求主体（玩家位唯一来源）。</summary>
        private const long ActorId = 2001;

        /// <summary>测试用房间标识。</summary>
        private const long RoomId = 30001;

        /// <summary>测试用局号。</summary>
        private const int Round = 2;

        public RockPaperScissorsMatchBridgeTests()
        {
            OnlineMatchGameBridgeHolder.Retract();
        }

        public void Dispose()
        {
            OnlineMatchGameBridgeHolder.Retract();
            EnableBridge(false);
        }

        /// <summary>
        /// 设置对局桥开关（经进程设置真实写路径；进程级字段保持缺省，避免与既有设置冲突）。
        /// </summary>
        private static void EnableBridge(bool enabled)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting
            {
                Online = new OnlineCapabilityOptions { EnableMatchGameBridge = enabled },
            });
        }

        /// <summary>
        /// 构造保密投影视图（当前局未结算：Gesture=None + HasGesture=true）。
        /// </summary>
        private static OnlineMatchBridgeGameView SecretView()
        {
            return new OnlineMatchBridgeGameView
            {
                RoomId = RoomId,
                MatchId = "rps-s1-r30001-g2",
                Mode = 410,
                State = OnlineMatchBridgeGameState.Running,
                Round = Round,
                WinnerPlayerId = 0,
                Players = new List<OnlineMatchBridgePlayerView>
                {
                    new OnlineMatchBridgePlayerView { PlayerId = ActorId, HasGesture = true, Wins = 1, Gesture = 0 },
                    new OnlineMatchBridgePlayerView { PlayerId = 2002, HasGesture = false, Wins = 0, Gesture = 0 },
                },
            };
        }

        /// <summary>
        /// 开关关闭：四个 Try* 一律返回未接管（Handled=false / false），响应对象不被触碰。
        /// </summary>
        [Fact]
        public async Task BridgeDisabled_AllTryMethods_ReturnNotHandledAndLeaveResponseUntouched()
        {
            EnableBridge(false);

            var startResponse = new RespStartRoomGame();
            var startHandled = await RockPaperScissorsMatchBridge.TryStartRoomGameAsync(ActorId, RoomId, Round, new[] { ActorId, 2002L }, startResponse);
            Assert.False(startHandled);
            Assert.Equal(0, startResponse.ErrorCode);
            Assert.Null(startResponse.Room);

            var submitResponse = new RespSubmitRockPaperScissorsGesture();
            var submit = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(ActorId, RoomId, Round, 1, submitResponse);
            Assert.False(submit.Handled);
            Assert.Null(submit.View);
            Assert.Equal(0, submitResponse.ErrorCode);
            Assert.Null(submitResponse.GameInfo);

            var infoResponse = new RespRockPaperScissorsGameInfo();
            var info = await RockPaperScissorsMatchBridge.TryGetGameInfoAsync(ActorId, RoomId, Round, infoResponse);
            Assert.False(info.Handled);
            Assert.Null(info.View);
            Assert.Equal(0, infoResponse.ErrorCode);
            Assert.Null(infoResponse.GameInfo);

            var restartResponse = new RespRestartRockPaperScissorsGame();
            var restart = await RockPaperScissorsMatchBridge.TryStartNextRoundAsync(ActorId, RoomId, Round + 1, new[] { ActorId, 2002L }, restartResponse);
            Assert.False(restart.Handled);
            Assert.Null(restart.View);
            Assert.Equal(0, restartResponse.ErrorCode);
            Assert.Null(restartResponse.GameInfo);
        }

        /// <summary>
        /// 开关开启而 lease 未发布：显式 -(int)DependencyUnavailable，不静默成功、不抛异常。
        /// </summary>
        [Fact]
        public async Task BridgeEnabled_WithoutLease_ReturnsExplicitDependencyUnavailable()
        {
            EnableBridge(true);

            var startResponse = new RespStartRoomGame();
            var startHandled = await RockPaperScissorsMatchBridge.TryStartRoomGameAsync(ActorId, RoomId, Round, new[] { ActorId, 2002L }, startResponse);
            Assert.True(startHandled);
            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, startResponse.ErrorCode);

            var submitResponse = new RespSubmitRockPaperScissorsGesture();
            var submit = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(ActorId, RoomId, Round, 1, submitResponse);
            Assert.True(submit.Handled);
            Assert.Null(submit.View);
            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, submitResponse.ErrorCode);
            Assert.Null(submitResponse.GameInfo);

            var infoResponse = new RespRockPaperScissorsGameInfo();
            var info = await RockPaperScissorsMatchBridge.TryGetGameInfoAsync(ActorId, RoomId, Round, infoResponse);
            Assert.True(info.Handled);
            Assert.Null(info.View);
            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, infoResponse.ErrorCode);
            Assert.Null(infoResponse.GameInfo);
        }

        /// <summary>
        /// 开关开启且 lease 已撤销（holder 当前位清空）：同样显式 DependencyUnavailable——
        /// 逐请求读取 holder，撤销后旧引用不存活。
        /// </summary>
        [Fact]
        public async Task BridgeEnabled_AfterRetract_ReturnsExplicitDependencyUnavailable()
        {
            EnableBridge(true);
            var stub = new StubMatchBridge();
            OnlineMatchGameBridgeHolder.Publish(stub, new OnlineScope(TenantId, AppId, ServerId));
            OnlineMatchGameBridgeHolder.Retract();
            Assert.False(OnlineMatchGameBridgeHolder.IsPublished);

            var handled = await RockPaperScissorsMatchBridge.TryStartRoomGameAsync(ActorId, RoomId, Round, new[] { ActorId, 2002L }, new RespStartRoomGame());
            Assert.True(handled);
            Assert.Equal(0, stub.StartCallCount); // 未触达契约面

            var submitResponse = new RespSubmitRockPaperScissorsGesture();
            var submit = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(ActorId, RoomId, Round, 1, submitResponse);
            Assert.True(submit.Handled);
            Assert.Equal(-(int)OnlineErrorCode.DependencyUnavailable, submitResponse.ErrorCode);
            Assert.Null(submitResponse.GameInfo);
        }

        /// <summary>
        /// 契约失败码映射：ParameterInvalid / ScopeDenied / StateEnded / DuplicateRequest
        /// 逐一映射为对应负值段（≤ -1001，与旧链 -1..-99 不重叠）。
        /// </summary>
        [Theory]
        [InlineData(OnlineErrorCode.ScopeDenied)]
        [InlineData(OnlineErrorCode.StateEnded)]
        [InlineData(OnlineErrorCode.DuplicateRequest)]
        public async Task ContractFailure_MapsToExplicitNegativeCode(OnlineErrorCode code)
        {
            EnableBridge(true);
            var stub = new StubMatchBridge { SubmitResult = () => OnlineResult<OnlineMatchBridgeGameView>.Fail(code, "契约面失败") };
            OnlineMatchGameBridgeHolder.Publish(stub, new OnlineScope(TenantId, AppId, ServerId));

            var response = new RespSubmitRockPaperScissorsGesture();
            var result = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(ActorId, RoomId, Round, 1, response);
            Assert.True(result.Handled);
            Assert.Null(result.View);
            Assert.Equal(-(int)code, response.ErrorCode);
            Assert.True(response.ErrorCode <= -1001);
            Assert.Null(response.GameInfo);
        }

        /// <summary>
        /// 契约面抛异常：兜底 -2（不外抛、不静默成功）。
        /// </summary>
        [Fact]
        public async Task ContractThrows_ReturnsInternalFallbackCode()
        {
            EnableBridge(true);
            var stub = new StubMatchBridge { SubmitResult = () => throw new InvalidOperationException("boom") };
            OnlineMatchGameBridgeHolder.Publish(stub, new OnlineScope(TenantId, AppId, ServerId));

            var response = new RespSubmitRockPaperScissorsGesture();
            var result = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(ActorId, RoomId, Round, 1, response);
            Assert.True(result.Handled);
            Assert.Null(result.View);
            Assert.Equal(-2, response.ErrorCode);
            Assert.Null(response.GameInfo);
        }

        /// <summary>
        /// 可信身份与参数透传：作用域三元组只取 lease 授权值、玩家位只取请求主体 ActorId；
        /// roomId / round / gesture / playerIds 原样到达契约面。
        /// </summary>
        [Fact]
        public async Task SubmitGesture_PassesAuthorizedScopeTripleAndSessionSubject()
        {
            EnableBridge(true);
            var stub = new StubMatchBridge();
            OnlineMatchGameBridgeHolder.Publish(stub, new OnlineScope(TenantId, AppId, ServerId));
            var playerIds = new[] { ActorId, 2002L };
            var startResponse = new RespStartRoomGame();
            var startHandled = await RockPaperScissorsMatchBridge.TryStartRoomGameAsync(ActorId, RoomId, Round, playerIds, startResponse);
            Assert.True(startHandled);
            Assert.Equal(0, startResponse.ErrorCode);
            Assert.Equal(1, stub.StartCallCount);
            Assert.Equal(TenantId, stub.LastStartScope.TenantId);
            Assert.Equal(AppId, stub.LastStartScope.AppId);
            Assert.Equal(ServerId, stub.LastStartScope.ServerId);
            Assert.Equal(ActorId, stub.LastStartScope.PlayerId);
            Assert.Equal(RoomId, stub.LastStartRoomId);
            Assert.Equal(Round, stub.LastStartRound);
            Assert.Equal(playerIds, stub.LastStartPlayerIds);

            var submitResponse = new RespSubmitRockPaperScissorsGesture();
            var submit = await RockPaperScissorsMatchBridge.TrySubmitGestureAsync(ActorId, RoomId, Round, 2, submitResponse);
            Assert.True(submit.Handled);
            Assert.NotNull(submit.View);
            Assert.Equal(0, submitResponse.ErrorCode);
            Assert.NotNull(submitResponse.GameInfo);
            Assert.Equal(ActorId, stub.LastSubmitScope.PlayerId);
            Assert.Equal(RoomId, stub.LastSubmitRoomId);
            Assert.Equal(Round, stub.LastSubmitRound);
            Assert.Equal(2, stub.LastSubmitGesture);
        }

        /// <summary>
        /// MapGameInfo 字段级投影：未结算局 Gesture=None 且 HasGesture=true（保密）；
        /// 已结算局回显真实出拳；WinnerPlayerId 终局回显；Wins 不外发（协议无字段）。
        /// </summary>
        [Fact]
        public void MapGameInfo_FieldLevelProjection_KeepsSecrecyAndDropsWins()
        {
            var secret = RockPaperScissorsMatchBridge.MapGameInfo(SecretView());
            Assert.Equal(RoomId, secret.RoomId);
            Assert.Equal(Round, secret.Round);
            Assert.Equal(0, secret.WinnerPlayerId);
            Assert.Equal(2, secret.Players.Count);
            Assert.Equal(ActorId, secret.Players[0].PlayerId);
            Assert.True(secret.Players[0].HasGesture);
            Assert.Equal(RockPaperScissorsGesture.None, secret.Players[0].Gesture); // 保密：未结算不回显
            Assert.False(secret.Players[1].HasGesture);
            Assert.Equal(RockPaperScissorsGesture.None, secret.Players[1].Gesture);

            var settled = new OnlineMatchBridgeGameView
            {
                RoomId = RoomId,
                Round = Round,
                State = OnlineMatchBridgeGameState.Completed,
                WinnerPlayerId = ActorId,
                Players = new List<OnlineMatchBridgePlayerView>
                {
                    new OnlineMatchBridgePlayerView { PlayerId = ActorId, HasGesture = true, Wins = 2, Gesture = 3 },
                    new OnlineMatchBridgePlayerView { PlayerId = 2002, HasGesture = true, Wins = 1, Gesture = 2 },
                },
            };
            var mapped = RockPaperScissorsMatchBridge.MapGameInfo(settled);
            Assert.Equal(ActorId, mapped.WinnerPlayerId);
            Assert.Equal(RockPaperScissorsGesture.Paper, mapped.Players[0].Gesture); // 已结算回显真实出拳
            Assert.Equal(RockPaperScissorsGesture.Scissors, mapped.Players[1].Gesture);

            // Wins 不外发：协议玩家条目无 Wins 字段（样例能力边界，视图携带但映射丢弃）。
            Assert.Null(typeof(RockPaperScissorsPlayerInfo).GetProperty("Wins"));
        }

        /// <summary>
        /// Room 状态映射决策矩阵：Settling+Playing → EnterSettling；
        /// 终态（Completed/SettlementFailed/Terminated）+ Playing/Settling → MarkSettled；
        /// 其余组合不推进。
        /// </summary>
        [Theory]
        [InlineData(OnlineMatchBridgeGameState.Settling, RoomStatus.Playing, RockPaperScissorsMatchRoomAction.EnterSettling)]
        [InlineData(OnlineMatchBridgeGameState.Settling, RoomStatus.Settling, RockPaperScissorsMatchRoomAction.None)]
        [InlineData(OnlineMatchBridgeGameState.Settling, RoomStatus.Settled, RockPaperScissorsMatchRoomAction.None)]
        [InlineData(OnlineMatchBridgeGameState.Settling, RoomStatus.Ready, RockPaperScissorsMatchRoomAction.None)]
        [InlineData(OnlineMatchBridgeGameState.Completed, RoomStatus.Playing, RockPaperScissorsMatchRoomAction.MarkSettled)]
        [InlineData(OnlineMatchBridgeGameState.Completed, RoomStatus.Settling, RockPaperScissorsMatchRoomAction.MarkSettled)]
        [InlineData(OnlineMatchBridgeGameState.Completed, RoomStatus.Settled, RockPaperScissorsMatchRoomAction.None)]
        [InlineData(OnlineMatchBridgeGameState.SettlementFailed, RoomStatus.Playing, RockPaperScissorsMatchRoomAction.MarkSettled)]
        [InlineData(OnlineMatchBridgeGameState.Terminated, RoomStatus.Settling, RockPaperScissorsMatchRoomAction.MarkSettled)]
        [InlineData(OnlineMatchBridgeGameState.Terminated, RoomStatus.Settled, RockPaperScissorsMatchRoomAction.None)]
        [InlineData(OnlineMatchBridgeGameState.Running, RoomStatus.Playing, RockPaperScissorsMatchRoomAction.None)]
        [InlineData(OnlineMatchBridgeGameState.Waiting, RoomStatus.Ready, RockPaperScissorsMatchRoomAction.None)]
        public void ComputeRoomAction_DecisionMatrix(OnlineMatchBridgeGameState viewState, RoomStatus roomStatus, RockPaperScissorsMatchRoomAction expected)
        {
            Assert.Equal(expected, RockPaperScissorsMatchBridge.ComputeRoomAction(viewState, roomStatus));
        }

        /// <summary>
        /// 契约面桩：记录调用参数，可注入结果工厂或异常。
        /// </summary>
        private sealed class StubMatchBridge : IOnlineMatchGameBridge
        {
            /// <summary>开局调用计数。</summary>
            public int StartCallCount;

            /// <summary>提交结果工厂（缺省返回保密视图）。</summary>
            public Func<OnlineResult<OnlineMatchBridgeGameView>> SubmitResult = () => OnlineResult<OnlineMatchBridgeGameView>.Ok(SecretView());

            /// <summary>最近一次开局作用域。</summary>
            public OnlineScope LastStartScope;

            /// <summary>最近一次开局房间标识。</summary>
            public long LastStartRoomId;

            /// <summary>最近一次开局局号。</summary>
            public int LastStartRound;

            /// <summary>最近一次开局玩家集合。</summary>
            public IReadOnlyList<long> LastStartPlayerIds;

            /// <summary>最近一次提交作用域。</summary>
            public OnlineScope LastSubmitScope;

            /// <summary>最近一次提交房间标识。</summary>
            public long LastSubmitRoomId;

            /// <summary>最近一次提交局号。</summary>
            public int LastSubmitRound;

            /// <summary>最近一次提交出拳值。</summary>
            public int LastSubmitGesture;

            /// <inheritdoc />
            public Task<OnlineResult<OnlineMatchBridgeGameView>> StartRoomGameAsync(OnlineScope scope, long roomId, int round, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
            {
                StartCallCount++;
                LastStartScope = scope;
                LastStartRoomId = roomId;
                LastStartRound = round;
                LastStartPlayerIds = playerIds;
                return Task.FromResult(OnlineResult<OnlineMatchBridgeGameView>.Ok(SecretView()));
            }

            /// <inheritdoc />
            public Task<OnlineResult<OnlineMatchBridgeGameView>> SubmitGestureAsync(OnlineScope scope, long roomId, int round, int gesture, CancellationToken cancellationToken = default)
            {
                LastSubmitScope = scope;
                LastSubmitRoomId = roomId;
                LastSubmitRound = round;
                LastSubmitGesture = gesture;
                return Task.FromResult(SubmitResult());
            }

            /// <inheritdoc />
            public Task<OnlineResult<OnlineMatchBridgeGameView>> GetGameInfoAsync(OnlineScope scope, long roomId, int round, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineResult<OnlineMatchBridgeGameView>.Ok(SecretView()));
            }
        }
    }
}
