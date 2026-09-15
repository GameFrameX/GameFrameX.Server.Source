// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Collections.Generic;
using GameFrameX.Online.Match;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 对局生命周期状态机测试（vault:C6 S5.1 / VC-5.10 / VC-5.11：迁移固化、终态唯一、无僵尸对局）。
    /// </summary>
    public class OnlineMatchStateMachineTests
    {
        /// <summary>枚举全量（用于遍历断言，避免逐个硬编码漏项）。</summary>
        private static readonly OnlineMatchState[] AllStates = (OnlineMatchState[])Enum.GetValues(typeof(OnlineMatchState));

        /// <summary>
        /// 验证 VC-5.10：除唯一终态外每个状态都有合法后继，不存在「进得去出不来」的死状态。
        /// </summary>
        [Fact]
        public void EveryNonTerminalState_ShouldHaveLegalTarget()
        {
            foreach (var state in AllStates)
            {
                if (OnlineMatchStateMachine.IsTerminal(state))
                {
                    continue;
                }

                Assert.True(OnlineMatchStateMachine.GetLegalTargets(state).Count > 0, state + " 无合法后继，会成为死状态");
            }
        }

        /// <summary>
        /// 验证 VC-5.7 / VC-5.10：运行中全员离场可按「取消」收束（原因码须准确，不能记成超时）。
        /// </summary>
        [Fact]
        public void Running_ShouldAllowCancellation()
        {
            Assert.True(OnlineMatchStateMachine.TryTransition(OnlineMatchState.Running, OnlineMatchState.Cancelled));
        }

        /// <summary>
        /// 验证 VC-5.11：唯一无出边的终态是 Closed（运行时只释放该状态）。
        /// </summary>
        [Fact]
        public void Closed_ShouldBeOnlyTerminalState()
        {
            foreach (var state in AllStates)
            {
                Assert.Equal(state == OnlineMatchState.Closed, OnlineMatchStateMachine.IsTerminal(state));
            }

            Assert.Empty(OnlineMatchStateMachine.GetLegalTargets(OnlineMatchState.Closed));
        }

        /// <summary>
        /// 验证 VC-5.2：唯一接受玩家输入的阶段是 Running。
        /// </summary>
        [Fact]
        public void Running_ShouldBeOnlyPlayableState()
        {
            foreach (var state in AllStates)
            {
                Assert.Equal(state == OnlineMatchState.Running, OnlineMatchStateMachine.IsPlayable(state));
            }
        }

        /// <summary>
        /// 验证 VC-5.11：从任一状态出发都能到达 Closed——否则对局将永远占用 Actor（僵尸对局）。
        /// </summary>
        [Fact]
        public void EveryState_ShouldReachClosed()
        {
            foreach (var state in AllStates)
            {
                Assert.True(CanReachClosed(state), state + " 无法到达 Closed，违反 VC-5.11");
            }
        }

        /// <summary>
        /// 验证结算失败态的释放路径：SettlementFailed 是结束态且保留期后可释放。
        /// </summary>
        [Fact]
        public void SettlementFailed_ShouldBeEndedAndReleasable()
        {
            Assert.True(OnlineMatchStateMachine.IsEnded(OnlineMatchState.SettlementFailed));
            Assert.Contains(OnlineMatchState.Closed, OnlineMatchStateMachine.GetLegalTargets(OnlineMatchState.SettlementFailed));
        }

        /// <summary>
        /// 锁定关键边：正常对局路径与结算失败回退路径必须存在。
        /// </summary>
        [Theory]
        [InlineData(OnlineMatchState.Created, OnlineMatchState.Waiting)]
        [InlineData(OnlineMatchState.Waiting, OnlineMatchState.Ready)]
        [InlineData(OnlineMatchState.Ready, OnlineMatchState.Running)]
        [InlineData(OnlineMatchState.Ready, OnlineMatchState.Waiting)]
        [InlineData(OnlineMatchState.Running, OnlineMatchState.Settling)]
        [InlineData(OnlineMatchState.Settling, OnlineMatchState.Completed)]
        [InlineData(OnlineMatchState.Settling, OnlineMatchState.SettlementFailed)]
        [InlineData(OnlineMatchState.SettlementFailed, OnlineMatchState.Settling)]
        [InlineData(OnlineMatchState.Completed, OnlineMatchState.Closed)]
        public void LegalEdge_ShouldBeAccepted(OnlineMatchState from, OnlineMatchState to)
        {
            Assert.True(OnlineMatchStateMachine.TryTransition(from, to));
        }

        /// <summary>
        /// 锁定非法边：跳过准备直接开局、终态复活、越过结算直接完成都不允许。
        /// </summary>
        [Theory]
        [InlineData(OnlineMatchState.Created, OnlineMatchState.Running)]
        [InlineData(OnlineMatchState.Waiting, OnlineMatchState.Running)]
        [InlineData(OnlineMatchState.Running, OnlineMatchState.Completed)]
        [InlineData(OnlineMatchState.Closed, OnlineMatchState.Running)]
        [InlineData(OnlineMatchState.Completed, OnlineMatchState.Running)]
        [InlineData(OnlineMatchState.Closed, OnlineMatchState.Closed)]
        public void IllegalEdge_ShouldBeRejected(OnlineMatchState from, OnlineMatchState to)
        {
            Assert.False(OnlineMatchStateMachine.TryTransition(from, to));
            Assert.DoesNotContain(to, OnlineMatchStateMachine.GetLegalTargets(from));
        }

        /// <summary>
        /// 守护：GetLegalTargets 与 GetAllEdges 必须自洽（两份视图不得漂移）。
        /// </summary>
        [Fact]
        public void LegalTargets_ShouldMatchEdgeList()
        {
            var edges = OnlineMatchStateMachine.GetAllEdges();
            foreach (var edge in edges)
            {
                Assert.Contains(edge.To, OnlineMatchStateMachine.GetLegalTargets(edge.From));
            }

            var expectedEdgeCount = 0;
            foreach (var state in AllStates)
            {
                expectedEdgeCount += OnlineMatchStateMachine.GetLegalTargets(state).Count;
            }

            Assert.Equal(expectedEdgeCount, edges.Count);
        }

        /// <summary>
        /// 判定某状态是否能（沿合法边）到达 Closed。
        /// </summary>
        /// <param name="start">起始状态。</param>
        /// <returns>可到达返回 <c>true</c>。</returns>
        private static bool CanReachClosed(OnlineMatchState start)
        {
            var visited = new HashSet<OnlineMatchState>();
            var pending = new Queue<OnlineMatchState>();
            pending.Enqueue(start);
            visited.Add(start);

            while (pending.Count > 0)
            {
                var current = pending.Dequeue();
                if (current == OnlineMatchState.Closed)
                {
                    return true;
                }

                foreach (var next in OnlineMatchStateMachine.GetLegalTargets(current))
                {
                    if (visited.Add(next))
                    {
                        pending.Enqueue(next);
                    }
                }
            }

            return false;
        }
    }
}
