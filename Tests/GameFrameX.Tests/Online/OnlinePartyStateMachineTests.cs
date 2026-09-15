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

using GameFrameX.Online.Party;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 队伍状态机测试（vault:C5 S4.3 生命周期图：合法边固化、终态不可复活、非法跳转被拒）。
    /// </summary>
    public class OnlinePartyStateMachineTests
    {
        /// <summary>
        /// 验证主生命周期链路的每一跳都是合法边。
        /// </summary>
        /// <param name="from">起态。</param>
        /// <param name="to">目标态。</param>
        [Theory]
        [InlineData(OnlinePartyState.Created, OnlinePartyState.Inviting)]
        [InlineData(OnlinePartyState.Inviting, OnlinePartyState.Formed)]
        [InlineData(OnlinePartyState.Formed, OnlinePartyState.Ready)]
        [InlineData(OnlinePartyState.Ready, OnlinePartyState.Matching)]
        [InlineData(OnlinePartyState.Matching, OnlinePartyState.Matched)]
        public void TryTransition_MainLifecycle_ShouldBeAllowed(OnlinePartyState from, OnlinePartyState to)
        {
            // Act
            var allowed = OnlinePartyStateMachine.TryTransition(from, to);

            // Assert
            Assert.True(allowed);
        }

        /// <summary>
        /// 验证跨阶段跳转被拒（vault:C5 S4.3：迁移由状态机裁决，不由调用方任意指定）。
        /// </summary>
        /// <param name="from">起态。</param>
        /// <param name="to">目标态。</param>
        [Theory]
        [InlineData(OnlinePartyState.Created, OnlinePartyState.Ready)]
        [InlineData(OnlinePartyState.Created, OnlinePartyState.Matched)]
        [InlineData(OnlinePartyState.Inviting, OnlinePartyState.Ready)]
        [InlineData(OnlinePartyState.Formed, OnlinePartyState.Matched)]
        [InlineData(OnlinePartyState.Ready, OnlinePartyState.Matched)]
        public void TryTransition_SkippingStage_ShouldBeRejected(OnlinePartyState from, OnlinePartyState to)
        {
            // Act
            var allowed = OnlinePartyStateMachine.TryTransition(from, to);

            // Assert
            Assert.False(allowed);
        }

        /// <summary>
        /// 验证终态集合与生命周期末端一致（Matched/Disbanded/Expired/Cancelled/Failed 无出边）。
        /// </summary>
        /// <param name="state">待判定状态。</param>
        /// <param name="expected">期望是否终态。</param>
        [Theory]
        [InlineData(OnlinePartyState.Matched, true)]
        [InlineData(OnlinePartyState.Disbanded, true)]
        [InlineData(OnlinePartyState.Expired, true)]
        [InlineData(OnlinePartyState.Cancelled, true)]
        [InlineData(OnlinePartyState.Failed, true)]
        [InlineData(OnlinePartyState.Created, false)]
        [InlineData(OnlinePartyState.Inviting, false)]
        [InlineData(OnlinePartyState.Formed, false)]
        [InlineData(OnlinePartyState.Ready, false)]
        [InlineData(OnlinePartyState.Matching, false)]
        [InlineData(OnlinePartyState.Left, false)]
        public void IsTerminal_ShouldMatchLifecycleEnds(OnlinePartyState state, bool expected)
        {
            // Act
            var terminal = OnlinePartyStateMachine.IsTerminal(state);

            // Assert
            Assert.Equal(expected, terminal);
        }

        /// <summary>
        /// 验证终态无合法出边——「取消/解散后不可复活」由状态机而非调用方保证。
        /// </summary>
        /// <param name="state">终态。</param>
        [Theory]
        [InlineData(OnlinePartyState.Matched)]
        [InlineData(OnlinePartyState.Disbanded)]
        [InlineData(OnlinePartyState.Expired)]
        [InlineData(OnlinePartyState.Cancelled)]
        [InlineData(OnlinePartyState.Failed)]
        public void GetLegalTargets_ForTerminalState_ShouldBeEmpty(OnlinePartyState state)
        {
            // Act
            var targets = OnlinePartyStateMachine.GetLegalTargets(state);

            // Assert
            Assert.Empty(targets);
        }

        /// <summary>
        /// 验证匹配中状态可分别走向成功与失败两条分支（匹配失败队伍可回到 Ready 重排）。
        /// </summary>
        [Fact]
        public void GetLegalTargets_ForMatching_ShouldContainMatchedFailedAndReady()
        {
            // Act
            var targets = OnlinePartyStateMachine.GetLegalTargets(OnlinePartyState.Matching);

            // Assert
            Assert.Contains(OnlinePartyState.Matched, targets);
            Assert.Contains(OnlinePartyState.Failed, targets);
            Assert.Contains(OnlinePartyState.Ready, targets);
            Assert.Contains(OnlinePartyState.Cancelled, targets);
        }
    }
}
