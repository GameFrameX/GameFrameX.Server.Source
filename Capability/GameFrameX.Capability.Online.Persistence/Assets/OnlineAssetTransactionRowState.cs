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
/// 资产交易记录持久化行（C249 一级 store：Assets 域 → GameDb 适配的落库形态）。
/// <para>
/// 键编码约定（T1 裁决 1）：<see cref="TransactionId"/> 单列唯一索引（交易幂等边界的键面）；
/// 行 Id 由 <c>NextSequenceAsync</c> 分配；状态迁移按统一入口全量覆盖写（终态不可再迁移由
/// 调用方状态机保证，与 InMemory 整体覆盖语义同构）。<see cref="AppliedLedgerEntryIds"/>
/// 为账本条目外键列表（追溯/恢复用）。
/// </para>
/// </summary>
public sealed class OnlineAssetTransactionRowState : BaseCacheState
{
    /// <summary>
    /// 获取或设置交易标识（唯一索引列）。
    /// </summary>
    [EntityIndex("ux_online_asset_transaction_id", Unique = true)]
    public string TransactionId { get; set; }

    /// <summary>
    /// 获取或设置幂等键。
    /// </summary>
    public string IdempotencyKey { get; set; }

    /// <summary>
    /// 获取或设置请求摘要。
    /// </summary>
    public string RequestDigest { get; set; }

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
    /// 获取或设置预期变更行数。
    /// </summary>
    public int ExpectedChangeCount { get; set; }

    /// <summary>
    /// 获取或设置交易状态（枚举数值落库）。
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// 获取或设置已落账本条目标识列表。
    /// </summary>
    public List<string> AppliedLedgerEntryIds { get; set; }

    /// <summary>
    /// 获取或设置结算时刻（Unix 毫秒；0 = 未结算）。
    /// </summary>
    public long SettledTime { get; set; }

    /// <summary>
    /// 获取或设置失败信息。
    /// </summary>
    public string FailureMessage { get; set; }
}
