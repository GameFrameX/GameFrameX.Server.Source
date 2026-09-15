//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 游戏账号实体（vault:C3 S2.2：Tenant → App → Identity / GameAccount → Player 层级中的账号层）。
/// <para>
/// 维护约束：账号是身份与玩家的归属聚合根；一个 GameAccount 可拥有多个 Player，
/// 但每个 Player 必须有明确的 App/Server 归属；合并（Merged）与注销（Deactivated）
/// 为显式操作，注销后的数据保留期由 <see cref="DataRetentionUntilTime"/> 承载，
/// 保留期内不得物理删除（数据保留红线）。
/// </para>
/// </summary>
public sealed class OnlineGameAccount
{
    /// <summary>
    /// 获取或设置账号标识（服务端生成，全局唯一；存量迁移时沿用 LoginState.Id）。
    /// </summary>
    public long Id
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置账号生命周期状态。
    /// </summary>
    public OnlineGameAccountStatus Status
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近更新时间（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置注销时间（Unix 毫秒；0 = 未注销）。
    /// </summary>
    public long DeactivatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置数据保留截止时间（Unix 毫秒；0 = 按默认保留策略；注销后保留期内数据不可物理删除）。
    /// </summary>
    public long DataRetentionUntilTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置合并目标账号标识（Status == Merged 时必填；0 = 未合并）。
    /// </summary>
    public long MergedIntoAccountId
    {
        get;
        set;
    }
}
