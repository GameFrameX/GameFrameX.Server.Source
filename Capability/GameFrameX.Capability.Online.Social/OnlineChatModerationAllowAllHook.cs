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

namespace GameFrameX.Capability.Online.Social;

using System.Threading;
using GameFrameX.Capability.Online.Abstractions;
using System.Threading.Tasks;

/// <summary>
/// 聊天内容审核钩子的默认放行策略（C294 机制钩子化：<see cref="OnlineSocialCapabilityModule"/> 经
/// <c>DeclareHook&lt;IOnlineChatModerationHook&gt;</c> 声明本默认，热更侧未挂载审核插件时生效）。
/// <para>
/// 维护约束：默认策略**只放行**——审核是扩展能力不是正确性前提，未挂载即拒绝等于把「有没有接审核服务」
/// 变成聊天能不能用的开关；真实审核实现由热更侧经 <c>CapabilityHook&lt;IOnlineChatModerationHook&gt;.Attach</c>
/// 原子替换，本实现不感知。放行不产生 <see cref="OnlineChatEvents.ModerationFailed"/> 事件——
/// 该事件语义是「审核插件执行失败被放行」的留痕，默认放行不是失败。
/// </para>
/// </summary>
public sealed class OnlineChatModerationAllowAllHook : IOnlineChatModerationHook
{
    /// <summary>
    /// 审核一条待发送消息（默认放行策略：恒放行，不做任何检查）。
    /// </summary>
    /// <param name="message">待发送消息。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恒返回放行裁决。</returns>
    public Task<OnlineChatModerationVerdict> CheckAsync(OnlineChatMessage message, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(OnlineChatModerationVerdict.Allow());
    }
}
