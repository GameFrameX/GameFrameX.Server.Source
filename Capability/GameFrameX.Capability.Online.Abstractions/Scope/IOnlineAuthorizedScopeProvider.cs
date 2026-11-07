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
namespace GameFrameX.Capability.Online.Abstractions.Scope;

/// <summary>
/// 宿主授权作用域发布契约（C294）：把族基建预置的 <see cref="OnlineScope"/> 三元组
/// （Tenant / App / Server）以桥契约形态暴露给进程内消费方（主要是 Hotfix 协议薄适配器——
/// 其程序集不引用宿主配置，无法另行读取作用域）。
/// <para>
/// 维护约束：由 online-foundation 模块在 <c>ActivateAsync</c> 期经
/// <c>CapabilityBridge&lt;IOnlineAuthorizedScopeProvider&gt;</c> 发布一次（全宿主唯一发布者）；
/// 消费方 <c>TryAcquire</c> 失败即显式 8002，不得回落到自造默认作用域——作用域是数据隔离根边界。
/// </para>
/// </summary>
public interface IOnlineAuthorizedScopeProvider
{
    /// <summary>
    /// 获取宿主授权作用域（族配置段三元组构造，全宿主单例语义）。
    /// </summary>
    OnlineScope AuthorizedScope { get; }
}
