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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
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

using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 版本清单存储契约（vault:C9 S8.5：版本协议清单登记簿 + 活跃版本指针；
/// 作用域按 (TenantId, AppId) 两键隔离——清单是 App 级资产，ServerId 固定 0 不参与定位）。
/// <para>
/// 维护约束（红线）：
/// ① <b>登记幂等判重</b>——同 (作用域, 版本号) 重复登记返回「非新登记」且<b>不覆盖</b>既有清单
/// （清单一经登记不可变更，历史清单稳定是回滚兼容判定的前提）；
/// ② <b>活跃版本切换与登记判重在实现内须处同一临界区</b>（并发登记 + 切换不得撕裂状态）；
/// ③ 出入参防御性拷贝（持久化行语义，调用方引用不得别名存储内部状态）。
/// </para>
/// </summary>
public interface IOnlineHotfixVersionStore
{
    /// <summary>
    /// 登记一份版本协议清单（发布流水线簿记；不改变活跃版本，不落审计）。
    /// </summary>
    /// <param name="manifest">待登记清单（版本号在同作用域内唯一）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>新登记返回 <c>true</c>；同 (作用域, 版本号) 已存在返回 <c>false</c>（幂等回执，不覆盖既有清单）。</returns>
    Task<bool> RegisterAsync(OnlineHotfixProtocolManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按版本号查找清单（只读）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="version">版本号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>清单快照；未登记返回 <see langword="null"/>。</returns>
    Task<OnlineHotfixProtocolManifest> FindAsync(long tenantId, long appId, string version, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查找当前活跃版本清单（只读）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>活跃清单快照；从未激活返回 <see langword="null"/>。</returns>
    Task<OnlineHotfixProtocolManifest> FindActiveAsync(long tenantId, long appId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 把活跃版本切换到已登记的目标版本（回滚链路在执行器成功后调用）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="version">目标版本号（须已登记）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>切换前的活跃版本号（从未激活返回空字符串）。</returns>
    /// <exception cref="System.InvalidOperationException">目标版本未登记。</exception>
    Task<string> SetActiveAsync(long tenantId, long appId, string version, CancellationToken cancellationToken = default);
}
