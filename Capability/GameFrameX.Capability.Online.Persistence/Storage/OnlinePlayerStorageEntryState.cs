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

namespace GameFrameX.Capability.Online.Persistence.Storage;

using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 玩家云存储条目持久化行（一级 store：Storage 域 → GameDb 适配的落库形态）。
/// <para>
/// 键编码与版本约定：<see cref="UniqueKey"/> = 物化复合键（五元组无损拼接，
/// 与 InMemory <c>BuildKey</c> 同构）单列唯一索引，承接「expectedVersion == 0 仅创建」的互斥；
/// 行 Id 由 <c>NextSequenceAsync</c> 分配。版本换算：<see cref="BaseCacheState"/> 的 RowVersion
/// 从 0 起（插入 = 0、每次 CAS 递增），实体 <c>Version</c> 从 1 起——恒等式
/// <c>Version = RowVersion + 1</c>，CAS 目标行版本 = expectedVersion - 1（适配层内换算）。
/// 软删可见性：<see cref="DeletedAtTime"/> 业务字段为单一事实源，DB 级软删（IsDeleted）恒不置位，
/// 行物理长存（复活按 CAS 版本接管的契约前提）。
/// </para>
/// </summary>
public sealed class OnlinePlayerStorageEntryState : BaseCacheState
{
    /// <summary>
    /// 获取或设置物化复合键（"tenant:app:player:collection:key"；唯一索引列）。
    /// </summary>
    [EntityIndex("ux_online_storage_entry_unique_key", Unique = true)]
    public string UniqueKey { get; set; }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// 获取或设置集合名。
    /// </summary>
    public string Collection { get; set; }

    /// <summary>
    /// 获取或设置条目键（集合内唯一）。
    /// </summary>
    public string Key { get; set; }

    /// <summary>
    /// 获取或设置负载数据（原始字节）。
    /// </summary>
    public byte[] Payload { get; set; }

    /// <summary>
    /// 获取或设置创建时间（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime { get; set; }

    /// <summary>
    /// 获取或设置创建者。
    /// </summary>
    public string CreatedBy { get; set; }

    /// <summary>
    /// 获取或设置最近更新时间（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime { get; set; }

    /// <summary>
    /// 获取或设置最近更新者。
    /// </summary>
    public string UpdatedBy { get; set; }

    /// <summary>
    /// 获取或设置过期时刻（Unix 毫秒；0 = 永不过期）。
    /// </summary>
    public long ExpiresAtTime { get; set; }

    /// <summary>
    /// 获取或设置软删时刻（Unix 毫秒；0 = 未删除；业务可见性单一事实源）。
    /// </summary>
    public long DeletedAtTime { get; set; }
}
