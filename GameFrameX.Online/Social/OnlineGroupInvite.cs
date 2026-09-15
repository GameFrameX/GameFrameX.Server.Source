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

namespace GameFrameX.Online.Social;

/// <summary>
/// 群邀请（vault:C7 S6.4：邀请含过期语义，且不独立成聚合——邀请集合存在于群记录内）。
/// <para>
/// 维护约束：邀请随群记录整体 CAS 提交，因此「邀请状态」与「成员集合」永远同版本，不存在
/// 「邀请已接受但成员未入群」的中间态残留。邀请过期**不**改写群组状态（群组状态只由群主解散驱动），
/// 只把该条邀请置为终态。</para>
/// </summary>
public sealed class OnlineGroupInvite
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
    /// 获取或设置所属群组标识（邀请集合内冗余承载，便于事件载荷自解释，不参与唯一键）。
    /// </summary>
    public string GroupId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请发起人（发出邀请时必须是群成员）。
    /// </summary>
    public long InviterId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被邀请玩家（答复权限的唯一归属方；他人答复按反预言返回未找到）。
    /// </summary>
    public long InviteeId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请状态。
    /// </summary>
    public OnlineGroupInviteState State
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
    /// 获取或设置失效时刻（UTC 毫秒；到点由扫描或答复路径置为过期，不放过期邀请入群）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次裁决时刻（UTC 毫秒；未裁决与超期置位保持 0——超期是时间流逝而非答复）。
    /// </summary>
    public long RespondedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制邀请条目（群记录深拷贝使用；邀请随群记录整体提交，不共享引用）。
    /// </summary>
    /// <returns>邀请副本。</returns>
    public OnlineGroupInvite Copy()
    {
        return new OnlineGroupInvite
        {
            InviteId = InviteId,
            TenantId = TenantId,
            AppId = AppId,
            GroupId = GroupId,
            InviterId = InviterId,
            InviteeId = InviteeId,
            State = State,
            CreatedAtTime = CreatedAtTime,
            ExpiresAtTime = ExpiresAtTime,
            RespondedAtTime = RespondedAtTime,
        };
    }
}
