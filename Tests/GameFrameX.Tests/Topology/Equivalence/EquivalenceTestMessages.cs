// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.Messages;

namespace GameFrameX.Tests.Topology.Equivalence;

/// <summary>
/// 语义等价用例集的测试消息定义（C143c D9）。
/// </summary>
/// <remarks>
/// Test messages for the topology equivalence suite (C143c D9).
/// Four representative cross-role chains need request/response pairs; they are nested
/// in one static class because they are pure test payloads with no behavior of their own.
/// Values are deterministic (derived from player ids) so both topologies can be compared
/// through identical state snapshots. The messages are never serialized in this suite,
/// so no protobuf contracts are required.
/// </remarks>
public static class EquivalenceTestMessages
{
    /// <summary>
    /// 会话建立请求（Gate→Game）。
    /// </summary>
    public sealed class SessionEstablishRequest : MessageObject
    {
        /// <summary>玩家 Id / The player id</summary>
        public long PlayerId { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            PlayerId = 0;
        }
    }

    /// <summary>
    /// 会话建立应答（Game→Gate）。
    /// </summary>
    public sealed class SessionEstablishResponse : MessageObject
    {
        /// <summary>玩家 Id / The player id</summary>
        public long PlayerId { get; set; }

        /// <summary>是否已接受 / Whether the session was accepted</summary>
        public bool Accepted { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            PlayerId = 0;
            Accepted = false;
        }
    }

    /// <summary>
    /// 好友列表查询请求（Game→Social）。
    /// </summary>
    public sealed class FriendListRequest : MessageObject
    {
        /// <summary>玩家 Id / The player id</summary>
        public long PlayerId { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            PlayerId = 0;
        }
    }

    /// <summary>
    /// 好友列表查询应答（Social→Game）。
    /// </summary>
    public sealed class FriendListResponse : MessageObject
    {
        /// <summary>玩家 Id / The player id</summary>
        public long PlayerId { get; set; }

        /// <summary>好友 Id 列表（确定性构造）/ The friend id list (deterministically built)</summary>
        public long[] FriendPlayerIds { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            PlayerId = 0;
            FriendPlayerIds = null;
        }
    }

    /// <summary>
    /// 匹配加入请求（Game→Match）。
    /// </summary>
    public sealed class MatchJoinRequest : MessageObject
    {
        /// <summary>玩家 Id / The player id</summary>
        public long PlayerId { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            PlayerId = 0;
        }
    }

    /// <summary>
    /// 匹配加入应答（Match→Game）。
    /// </summary>
    public sealed class MatchJoinResponse : MessageObject
    {
        /// <summary>玩家 Id / The player id</summary>
        public long PlayerId { get; set; }

        /// <summary>匹配票据（由玩家 Id 确定性生成）/ The match ticket (deterministically derived from the player id)</summary>
        public string TicketId { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            PlayerId = 0;
            TicketId = null;
        }
    }

    /// <summary>
    /// 结算回调（Match→Game）。
    /// </summary>
    public sealed class SettlementCallbackMessage : MessageObject
    {
        /// <summary>对局 Id / The match id</summary>
        public long MatchId { get; set; }

        /// <summary>胜者玩家 Id / The winner player id</summary>
        public long WinnerPlayerId { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            MatchId = 0;
            WinnerPlayerId = 0;
        }
    }

    /// <summary>
    /// 结算确认（Game→Match）。
    /// </summary>
    public sealed class SettlementAckMessage : MessageObject
    {
        /// <summary>对局 Id / The match id</summary>
        public long MatchId { get; set; }

        /// <summary>已落地的胜者玩家 Id / The applied winner player id</summary>
        public long WinnerPlayerId { get; set; }

        /// <inheritdoc />
        public override void Clear()
        {
            MatchId = 0;
            WinnerPlayerId = 0;
        }
    }
}
