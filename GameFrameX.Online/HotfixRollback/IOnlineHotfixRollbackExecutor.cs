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
/// Hotfix 回滚执行器契约（vault:C9 S8.5 服务端配合面的装配面：实际 Hotfix 程序集加载切换
/// 由运行时装配提供实现——Server 仓 X4 装配层调用 <c>HotfixManager.LoadHotfix(dllVersion)</c>）。
/// <para>
/// 维护约束（红线）：
/// ① <b>依赖方向</b>——<c>GameFrameX.Online</c> 不引用 <c>GameFrameX.Core</c>，
/// <c>HotfixManager</c> 经本契约解耦（对齐 C95 <c>IOnlineCrossServerGrantTransport</c> 先例）；
/// ② <b>失败以异常表达</b>——程序集缺失、校验失败、加载异常一律抛出
/// （<see cref="OnlineHotfixRollbackService"/> 捕获后映射 8002 并把幂等键落定失败，回滚未生效）；
/// ③ <b>单进程语义</b>——本服务只驱动本进程的加载切换；多实例逐实例回滚的编排归运维发布流程（OL-025）。
/// </para>
/// </summary>
public interface IOnlineHotfixRollbackExecutor
{
    /// <summary>
    /// 把本进程的 Hotfix 程序集切换加载到目标版本。
    /// </summary>
    /// <param name="targetVersion">目标版本号（与清单登记的版本号一致）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>完成通知（切换成功；任何失败以异常表达）。</returns>
    Task RollbackAsync(string targetVersion, CancellationToken cancellationToken = default);
}
