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

namespace GameFrameX.Capability.Online.Persistence.Assets;

using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 不可变资产账本条目持久化行（一级 store：Assets 域 → GameDb 适配的落库形态）。
/// <para>
/// 资产红线：账本只追加——适配层仅经插入路径写本表（<c>SaveOneAsync</c>），结构性不提供任何
/// 修改/删除调用；<see cref="EntryId"/> 单列唯一索引兜底条目级幂等。玩家维度账本序
/// <see cref="SequenceNumber"/> 由 <c>NextSequenceAsync</c> 玩家键控序列分配
/// （"tenant:app:player"，原子单调、重启不复用——接管 InMemory 内存计数器）。
/// 账本是余额/库存对账的事实源（快照为派生面，崩溃残留由对账服务重算兜底）。
/// </para>
/// </summary>
public sealed class OnlineLedgerEntryState : BaseCacheState
{
    /// <summary>
    /// 获取或设置账本条目标识（<c>led-</c> 前缀；唯一索引列）。
    /// </summary>
    [EntityIndex("ux_online_ledger_entry_id", Unique = true)]
    public string EntryId { get; set; }

    /// <summary>
    /// 获取或设置所属交易标识。
    /// </summary>
    public string TransactionId { get; set; }

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
    /// 获取或设置归属服标识。
    /// </summary>
    public long HomeServerId { get; set; }

    /// <summary>
    /// 获取或设置发起服标识。
    /// </summary>
    public long InitiatingServerId { get; set; }

    /// <summary>
    /// 获取或设置变更来源（枚举数值落库）。
    /// </summary>
    public int Source { get; set; }

    /// <summary>
    /// 获取或设置操作类型（枚举数值落库）。
    /// </summary>
    public int Operation { get; set; }

    /// <summary>
    /// 获取或设置变更原因。
    /// </summary>
    public string Reason { get; set; }

    /// <summary>
    /// 获取或设置业务单号。
    /// </summary>
    public string BusinessOrderId { get; set; }

    /// <summary>
    /// 获取或设置操作者。
    /// </summary>
    public string OperatorId { get; set; }

    /// <summary>
    /// 获取或设置资产类别（枚举数值落库）。
    /// </summary>
    public int AssetKind { get; set; }

    /// <summary>
    /// 获取或设置资产标识。
    /// </summary>
    public string AssetId { get; set; }

    /// <summary>
    /// 获取或设置变更前数量。
    /// </summary>
    public long AmountBefore { get; set; }

    /// <summary>
    /// 获取或设置带符号变更数额。
    /// </summary>
    public long Delta { get; set; }

    /// <summary>
    /// 获取或设置变更后数量。
    /// </summary>
    public long AmountAfter { get; set; }

    /// <summary>
    /// 获取或设置补偿指向的原交易标识（普通条目为空字符串）。
    /// </summary>
    public string CompensatesTransactionId { get; set; }

    /// <summary>
    /// 获取或设置玩家维度账本序（单调递增；游标分页稳定排序键）。
    /// </summary>
    public long SequenceNumber { get; set; }

    /// <summary>
    /// 获取或设置落账时刻（Unix 毫秒）。
    /// </summary>
    public long OccurredTime { get; set; }
}
