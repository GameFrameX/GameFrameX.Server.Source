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

namespace GameFrameX.Online.Presence;

/// <summary>
/// 在线状态存储接口（vault:C3 S2.5：Presence 事实源的持久化契约）。
/// <para>
/// 维护约束：键 = (TenantId, AppId, PlayerId)——玩家维度唯一（多端并存时以会话域为准，Presence 表达玩家事实）；
/// 无记录即 Offline（RemoveAsync 即下线）；查询按 (TenantId, AppId) 列举，在线计数在服务层过滤状态。
/// </para>
/// </summary>
public interface IOnlinePresenceStore
{
    /// <summary>写入或覆盖在线状态记录（按 (TenantId, AppId, PlayerId) 全量写）。</summary>
    /// <param name="record">在线状态记录。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task SetAsync(OnlinePresenceRecord record, CancellationToken cancellationToken = default);

    /// <summary>按 (TenantId, AppId, PlayerId) 查找在线状态记录。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录；无记录返回 null（即 Offline）。</returns>
    Task<OnlinePresenceRecord> FindAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>移除在线状态记录（即 Offline）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task RemoveAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>列出 (TenantId, AppId) 下全部在线状态记录（在线计数与 Admin 查询输入）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>在线状态记录列表。</returns>
    Task<IReadOnlyList<OnlinePresenceRecord>> ListByAppAsync(long tenantId, long appId, CancellationToken cancellationToken = default);
}
