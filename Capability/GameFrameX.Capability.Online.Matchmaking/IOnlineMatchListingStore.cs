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

namespace GameFrameX.Capability.Online.Matchmaking;

/// <summary>
/// 对局列表项存储契约（发布/发现/加入的持久化契约）。
/// <para>
/// 维护约束（红线）：存储实现负责防御性深拷贝（按引用保存会让落库前的实例突变污染已存状态）。
/// 查询按作用域隔离，跨租户/跨 App 不可见。
/// </para>
/// </summary>
public interface IOnlineMatchListingStore
{
    /// <summary>
    /// 保存列表项（发布新建专用：按键覆盖写，不校验版本）。
    /// <para>
    /// 更新一律走 <see cref="UpdateIfRowVersionAsync"/> 条件写；服务层更新路径
    /// 禁止调用本方法（覆盖写会丢失并发更新，见 GFX-1106）。
    /// </para>
    /// </summary>
    /// <param name="listing">列表项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveAsync(OnlineMatchListing listing, CancellationToken cancellationToken = default);

    /// <summary>
    /// 条件写入列表项（乐观锁 CAS）：仅当存量行存在且其 <see cref="OnlineMatchListing.RowVersion"/>
    /// 等于 <paramref name="expectedRowVersion"/> 时写入；写入行的版本强制置为
    /// <c>expectedRowVersion + 1</c>（不信任调用方携带的版本字段）。
    /// <para>
    /// 版本不符（已被并发修改）或记录不存在返回 <c>false</c>，调用方应重读后重试。
    /// 未来接入 GameDb 持久化时，行模型继承 BaseCacheState 并直接映射
    /// DB 层 <c>UpdateIfRowVersionAsync</c>（与 GameDbOnlinePlayerStorageStore 约定一致）。
    /// </para>
    /// </summary>
    /// <param name="listing">待写入的列表项（版本字段以本方法写入的为准）。</param>
    /// <param name="expectedRowVersion">期望的存量行版本（来自本次读取）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>写入成功返回 <c>true</c>；版本不符或记录不存在返回 <c>false</c>。</returns>
    Task<bool> UpdateIfRowVersionAsync(OnlineMatchListing listing, long expectedRowVersion, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按标识查找列表项。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="listingId">列表项标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列表项副本；不存在返回 null。</returns>
    Task<OnlineMatchListing> FindAsync(long tenantId, long appId, string listingId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内全部列表项（过滤与封顶由服务层执行）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>列表项副本列表。</returns>
    Task<IReadOnlyList<OnlineMatchListing>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default);
}