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

using System.Collections.Generic;
using GameFrameX.Online.Presence;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 在线状态机转换表测试（vault:C3 VC-2.5：全部合法边逐一断言 + 全矩阵非法转换拒绝，覆盖率 100%）。
    /// </summary>
    public class OnlinePresenceStateMachineTests
    {
        /// <summary>
        /// 验证全部 35 条合法边逐一判定为合法（转换表固化断言）。
        /// </summary>
        [Theory]
        [InlineData(OnlinePresenceState.Offline, OnlinePresenceState.Online)]
        [InlineData(OnlinePresenceState.Offline, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.Idle)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.Matching)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.InParty)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.InMatch)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.Reconnecting)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.Offline)]
        [InlineData(OnlinePresenceState.Online, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.Idle, OnlinePresenceState.Online)]
        [InlineData(OnlinePresenceState.Idle, OnlinePresenceState.Reconnecting)]
        [InlineData(OnlinePresenceState.Idle, OnlinePresenceState.Offline)]
        [InlineData(OnlinePresenceState.Idle, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.Matching, OnlinePresenceState.Online)]
        [InlineData(OnlinePresenceState.Matching, OnlinePresenceState.InParty)]
        [InlineData(OnlinePresenceState.Matching, OnlinePresenceState.InMatch)]
        [InlineData(OnlinePresenceState.Matching, OnlinePresenceState.Reconnecting)]
        [InlineData(OnlinePresenceState.Matching, OnlinePresenceState.Offline)]
        [InlineData(OnlinePresenceState.Matching, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.InParty, OnlinePresenceState.Online)]
        [InlineData(OnlinePresenceState.InParty, OnlinePresenceState.Matching)]
        [InlineData(OnlinePresenceState.InParty, OnlinePresenceState.InMatch)]
        [InlineData(OnlinePresenceState.InParty, OnlinePresenceState.Reconnecting)]
        [InlineData(OnlinePresenceState.InParty, OnlinePresenceState.Offline)]
        [InlineData(OnlinePresenceState.InParty, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.InMatch, OnlinePresenceState.Online)]
        [InlineData(OnlinePresenceState.InMatch, OnlinePresenceState.InParty)]
        [InlineData(OnlinePresenceState.InMatch, OnlinePresenceState.Reconnecting)]
        [InlineData(OnlinePresenceState.InMatch, OnlinePresenceState.Offline)]
        [InlineData(OnlinePresenceState.InMatch, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.Reconnecting, OnlinePresenceState.Online)]
        [InlineData(OnlinePresenceState.Reconnecting, OnlinePresenceState.InMatch)]
        [InlineData(OnlinePresenceState.Reconnecting, OnlinePresenceState.Offline)]
        [InlineData(OnlinePresenceState.Reconnecting, OnlinePresenceState.Blocked)]
        [InlineData(OnlinePresenceState.Blocked, OnlinePresenceState.Offline)]
        public void TryTransition_ShouldAcceptAllLegalEdges(OnlinePresenceState from, OnlinePresenceState to)
        {
            // Act
            var isLegal = OnlinePresenceStateMachine.TryTransition(from, to);

            // Assert
            Assert.True(isLegal, "合法边被拒绝：" + from + " → " + to);
        }

        /// <summary>
        /// 验证合法边总数为 35（转换表规模变化必须显式更新本断言与边清单）。
        /// </summary>
        [Fact]
        public void GetAllEdges_ShouldContainExactly35Edges()
        {
            // Act
            var edges = OnlinePresenceStateMachine.GetAllEdges();

            // Assert
            Assert.Equal(35, edges.Count);
        }

        /// <summary>
        /// 验证 8×8 全矩阵中除合法边外全部拒绝（表外转换一律非法）且查询口径与判定口径一致。
        /// </summary>
        [Fact]
        public void TryTransition_ShouldRejectEveryIllegalPairInFullMatrix()
        {
            // Arrange
            var legalEdges = new HashSet<string>();
            foreach (var edge in OnlinePresenceStateMachine.GetAllEdges())
            {
                legalEdges.Add(edge.From + ":" + edge.To);
            }

            var allStates = (OnlinePresenceState[])System.Enum.GetValues(typeof(OnlinePresenceState));

            // Act & Assert
            foreach (var from in allStates)
            {
                foreach (var to in allStates)
                {
                    var isLegal = OnlinePresenceStateMachine.TryTransition(from, to);
                    var isEnumerated = legalEdges.Contains(from + ":" + to);
                    Assert.Equal(isEnumerated, isLegal);

                    var legalTargets = OnlinePresenceStateMachine.GetLegalTargets(from);
                    Assert.Equal(isLegal, System.Array.IndexOf(legalTargets.ToArray(), to) >= 0);
                }
            }
        }
    }
}
