//  ==========================================================================================
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
//  ==========================================================================================

namespace GameFrameX.Online.Party;

/// <summary>
/// 队伍邀请（vault:C5 S4.3：邀请含过期语义，过期不改写队伍状态）。
/// <para>
/// 维护约束：邀请是独立生命周期对象，其过期**不**直接迁移队伍状态——队伍状态只由成员集合变化驱动
/// （见 <see cref="OnlinePartyService.SweepExpiredAsync"/>：邀请过期只置邀请终态，队伍状态在成员集合
/// 未变时保持不变）。一个被邀请人同一时刻最多一条 <see cref="OnlinePartyInviteState.Pending"/> 邀请。
/// </para>
/// </summary>
public sealed class OnlinePartyInvite
{
    /// <summary>
    /// 获取或设置邀请标识。
    /// </summary>
    public string InviteId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置队伍标识。
    /// </summary>
    public string PartyId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请发起人（队长）。
    /// </summary>
    public long InviterId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被邀请玩家。
    /// </summary>
    public long InviteeId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请状态。
    /// </summary>
    public OnlinePartyInviteState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置过期时刻（UTC 毫秒；到点由 <see cref="OnlinePartyService.SweepExpiredAsync"/> 置为过期）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制邀请条目（存储层防御性深拷贝使用）。
    /// </summary>
    /// <returns>邀请副本。</returns>
    public OnlinePartyInvite Copy()
    {
        return new OnlinePartyInvite
        {
            InviteId = InviteId,
            PartyId = PartyId,
            TenantId = TenantId,
            AppId = AppId,
            InviterId = InviterId,
            InviteeId = InviteeId,
            State = State,
            CreatedAtTime = CreatedAtTime,
            ExpiresAtTime = ExpiresAtTime,
        };
    }
}
