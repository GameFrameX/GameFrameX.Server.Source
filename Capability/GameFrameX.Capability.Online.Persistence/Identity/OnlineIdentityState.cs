// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Identity;

using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 外源身份持久化行（Identity 域 → GameDb 适配的落库形态）。
/// <para>
/// 键编码约定：<see cref="UniqueKey"/> = 物化复合键（无损拼接原文，与 InMemory
/// <c>BuildIdentityKey</c> 同构），单列唯一索引承接四元组 (TenantId, AppId, Kind, Identifier) 唯一性；
/// 分隔符冒号与业务串内冒号的歧义语义与 InMemory 键逐字节同构（保真优先）。行 Id 无自然主键，
/// 由 <c>NextSequenceAsync</c> 分配；业务字段保留为独立列供等值/归属查询。
/// </para>
/// </summary>
public sealed class OnlineIdentityState : BaseCacheState
{
    /// <summary>
    /// 获取或设置物化复合键（"tenant:app:kind:identifier"；唯一索引列）。
    /// </summary>
    [EntityIndex("ux_online_identity_unique_key", Unique = true)]
    public string UniqueKey { get; set; }

    /// <summary>
    /// 获取或设置实体标识（服务端生成的字符串 Id，仅回读保真，不作查找键）。
    /// </summary>
    public string EntityId { get; set; }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置身份类型（枚举数值落库）。
    /// </summary>
    public int Kind { get; set; }

    /// <summary>
    /// 获取或设置身份标识串。
    /// </summary>
    public string Identifier { get; set; }

    /// <summary>
    /// 获取或设置归属游戏账号标识。
    /// </summary>
    public long GameAccountId { get; set; }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime { get; set; }

    /// <summary>
    /// 获取或设置最近更新时间（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime { get; set; }

    /// <summary>
    /// 获取或设置解绑时间（Unix 毫秒；0 = 绑定中）。
    /// </summary>
    public long UnboundAtTime { get; set; }
}
