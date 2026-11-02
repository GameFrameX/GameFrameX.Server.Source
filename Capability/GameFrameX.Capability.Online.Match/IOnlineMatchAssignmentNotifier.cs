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


using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Match;

/// <summary>
/// 撮合成立通知的出站端口：把「撮合已成立、对局已就位」的事实交给宿主侧投递实现
/// （会话通道与协议消息形态由宿主决定），能力包自身不依赖任何旧链会话设施与协议产物。
/// <para>
/// 维护约束：包内缺省为 <see cref="NullOnlineMatchAssignmentNotifier"/>（静默丢弃），
/// 宿主经运行时装配覆盖注入真实投递，覆盖恒胜出缺省；实现方对离线 / 无通道成员静默跳过
/// （best-effort，等待方经状态轮询兜底），投递异常按调用方既有批次隔离路径外抛。
/// </para>
/// </summary>
public interface IOnlineMatchAssignmentNotifier
{
    /// <summary>
    /// 向通知载荷内的全部成员投递撮合成立通知（best-effort；离线成员静默跳过）。
    /// </summary>
    /// <param name="notification">撮合成立通知载荷（协议无关）。</param>
    /// <param name="cancellationToken">停止取消令牌。</param>
    /// <returns>投递任务。</returns>
    Task NotifyAssignmentCreatedAsync(OnlineMatchAssignmentNotification notification, CancellationToken cancellationToken);
}
