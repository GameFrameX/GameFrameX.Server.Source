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

namespace GameFrameX.Online.Storage;

using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 玩家云存储接口（vault:C3 S2.6：CAS 乐观锁内建于存储层——<see cref="UpsertAsync"/> 为原子比较交换）。
/// <para>
/// 维护约束：键 = (TenantId, AppId, PlayerId, Collection, Key)；<see cref="UpsertAsync"/> 的
/// <c>expectedVersion</c> == 0 表示仅创建（已存在即失败），&gt; 0 表示版本必须匹配；软删条目对列举不可见
/// 但物理保留（复活按 CAS 版本接管）；列举按键字典序稳定排序（游标分页的基础）。
/// </para>
/// </summary>
public interface IOnlinePlayerStorageStore
{
    /// <summary>按键查找条目（含软删条目；可见性判定归服务层）。</summary>
    /// <param name="key">条目键载荷（租户 + App + 玩家 + 集合名 + 条目键）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>条目；不存在返回 null。</returns>
    Task<OnlinePlayerStorageEntry> FindAsync(OnlineStorageEntryKey key, CancellationToken cancellationToken = default);

    /// <summary>原子比较交换写入（乐观锁；expectedVersion=0 为仅创建，&gt;0 为版本匹配更新）。</summary>
    /// <param name="entry">待写入条目（Version 由调用方预置为目标版本）。</param>
    /// <param name="expectedVersion">期望版本。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>交换成功返回 true；版本不匹配或创建冲突返回 false。</returns>
    Task<bool> UpsertAsync(OnlinePlayerStorageEntry entry, long expectedVersion, CancellationToken cancellationToken = default);

    /// <summary>按键字典序列举集合内非软删条目（游标 = 排序起始键，不含）。</summary>
    /// <param name="query">列举查询载荷（玩家定位 + 集合名 + 游标 + 上限）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>非软删条目列表（按键字典序）。</returns>
    Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAsync(OnlineStorageListQuery query, CancellationToken cancellationToken = default);

    /// <summary>统计集合内活跃（非软删）键数（键数上限 enforcement 输入）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃键数。</returns>
    Task<int> CountActiveKeysAsync(long tenantId, long appId, long playerId, string collection, CancellationToken cancellationToken = default);

    /// <summary>列出全部条目（含软删；过期清理任务的扫描输入）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>全部条目列表。</returns>
    Task<IReadOnlyList<OnlinePlayerStorageEntry>> ListAllAsync(CancellationToken cancellationToken = default);
}
