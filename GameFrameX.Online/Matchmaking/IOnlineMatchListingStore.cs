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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 对局列表项存储契约（vault:C5 S4.4）。
/// <para>
/// 维护约束（红线）：存储实现负责防御性深拷贝（C95 先例：按引用保存会让落库前的实例突变污染已存状态）。
/// 查询按作用域隔离，跨租户/跨 App 不可见。
/// </para>
/// </summary>
public interface IOnlineMatchListingStore
{
    /// <summary>
    /// 保存列表项（新增或覆盖）。
    /// </summary>
    /// <param name="listing">列表项。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知。</returns>
    Task SaveAsync(OnlineMatchListing listing, CancellationToken cancellationToken = default);

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
