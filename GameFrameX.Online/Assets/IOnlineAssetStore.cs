// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Online.Assets;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 资产存储接口（vault:C4 S3.3：钱包/库存/不可变账本的读写契约；写路径只有
/// <see cref="ApplyAsync"/> 一个原子入口——批次构造仅限本程序集，旁路写入结构性不可达，X5）。
/// <para>
/// 维护约束（红线）：<see cref="ApplyAsync"/> 必须在单个玩家分片事务内先全量校验后统一落账
/// （余额/库存下限 0，无部分应用，VC-3.5/3.6）；账本只追加——本接口不提供任何账本修改/删除方法
/// （VC-3.7 不可变性由契约面保证，纠正只能追加反转条目）；账本序按玩家单调递增，
/// 列举以 <see cref="OnlineLedgerEntry.SequenceNumber"/> 稳定排序（游标分页基础）。
/// 生产装配以持久化实现替换（按玩家分片事务对齐 vault 风险缓解），内存实现为本仓默认。
/// </para>
/// </summary>
public interface IOnlineAssetStore
{
    /// <summary>查找货币账户快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="currencyId">货币代码。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照；从未发生过变更返回 null（视余额为 0）。</returns>
    Task<OnlineWalletAccount> FindWalletAsync(long tenantId, long appId, long playerId, string currencyId, CancellationToken cancellationToken = default);

    /// <summary>查找道具库存堆栈快照。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="itemId">道具标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照；从未发生过变更返回 null（视数量为 0）。</returns>
    Task<OnlineInventoryStack> FindInventoryAsync(long tenantId, long appId, long playerId, string itemId, CancellationToken cancellationToken = default);

    /// <summary>列举玩家全部货币账户快照（对账与 Admin 查询输入）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账户快照列表（按货币代码字典序）。</returns>
    Task<IReadOnlyList<OnlineWalletAccount>> ListWalletAccountsAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>列举玩家全部道具库存快照（对账与 Admin 查询输入）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>库存快照列表（按道具标识字典序）。</returns>
    Task<IReadOnlyList<OnlineInventoryStack>> ListInventoryStacksAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>原子应用变更批次（玩家分片事务：先全量校验下限 0，后统一追加账本并同步快照）。</summary>
    /// <param name="batch">变更批次（仅限本程序集统一入口构造）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>应用结果（失败时净效应为 0）。</returns>
    Task<OnlineAssetApplyResult> ApplyAsync(OnlineAssetChangeBatch batch, CancellationToken cancellationToken = default);

    /// <summary>按账本序分页列举玩家账本条目（稳定排序键 = SequenceNumber，升序）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="afterSequenceNumber">游标（起始序，不含；0 = 从头列举）。</param>
    /// <param name="maxCount">最大返回条数。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账本条目列表（按序升序）。</returns>
    Task<IReadOnlyList<OnlineLedgerEntry>> ListLedgerEntriesAsync(long tenantId, long appId, long playerId, long afterSequenceNumber, int maxCount, CancellationToken cancellationToken = default);

    /// <summary>按交易标识反查账本条目（VC-3.14 追溯：来源/原因/单号/操作者/前后值全字段）。</summary>
    /// <param name="transactionId">交易标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>该交易的全部账本条目（含反转条目）。</returns>
    Task<IReadOnlyList<OnlineLedgerEntry>> FindLedgerEntriesByTransactionIdAsync(string transactionId, CancellationToken cancellationToken = default);

    /// <summary>聚合玩家账本带符号数额（对账的账本侧事实源；键 = 类别前缀 + 资产标识）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>各资产账本累加（键：<c>c:</c> 货币 / <c>i:</c> 道具 + 资产标识）。</returns>
    Task<IReadOnlyDictionary<string, long>> SumLedgerDeltasByAssetAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>列举 (租户, App) 下发生过资产活动的玩家（全量对账的遍历输入）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家标识列表（升序去重）。</returns>
    Task<IReadOnlyList<long>> ListPlayerIdsAsync(long tenantId, long appId, CancellationToken cancellationToken = default);
}
