// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 域桥桩能力模块（本测试程序集内唯一的能力模块）：激活期按 <see cref="HotfixBridgeStubSlots"/>
/// 发布好友 / 匹配域桥契约与授权作用域提供者桥，复刻生产「模块 Activate 发布 / 宿主停机自动撤桥」。
/// </summary>
public sealed class HotfixBridgeStubModule : ICapabilityModule
{
    /// <summary>桩能力标识。</summary>
    public const string CapabilityId = "hotfix-bridge-stub";

    /// <summary>
    /// 桩能力清单（无依赖、无 admin action、无 Options）。
    /// </summary>
    private static readonly CapabilityManifest ManifestInstance = new CapabilityManifest(CapabilityId, "1.0.0");

    /// <summary>
    /// 获取桩模块的能力清单，固定返回静态的 <c>ManifestInstance</c>。
    /// </summary>
    /// <remarks>
    /// Gets the stub module capability manifest; always returns the static <c>ManifestInstance</c>.
    /// </remarks>
    /// <value>桩模块固定的能力清单实例 / Fixed capability manifest instance of the stub module</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 注册期不做任何声明：桥契约只能在激活期发布，因此桩模块无需注册服务。
    /// </summary>
    /// <remarks>
    /// Declares nothing during registration, because bridge contracts may only be published at activation time; the stub module needs no service registration.
    /// </remarks>
    /// <param name="context">能力注册上下文（本实现不使用） / Capability registration context (unused by this implementation)</param>
    public void Register(CapabilityRegistrationContext context)
    {
        // 桩模块无注册期声明：桥契约只能在激活期发布。
    }

    /// <summary>
    /// 按测试槽位发布桥：好友域与匹配域槽位非空时分别发布 <c>IOnlineFriendDomain</c> 与 <c>IOnlineMatchDomain</c>，开启作用域开关时发布授权作用域提供者桩。
    /// </summary>
    /// <remarks>
    /// Publishes bridges according to the test slots: the friend and match domain bridges when their slots are non-null, and the authorized scope provider stub when the scope switch is enabled.
    /// </remarks>
    /// <param name="context">能力激活上下文，用于发布桥 / Capability activation context used to publish bridges</param>
    /// <param name="cancellationToken">取消令牌（本实现不使用） / Cancellation token (unused by this implementation)</param>
    /// <returns>发布完成后立即返回的已完成任务 / Completed task returned immediately after publishing</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        var friendDomain = HotfixBridgeStubSlots.FriendDomain;
        if (friendDomain != null)
        {
            context.Bridges.Publish<IOnlineFriendDomain>(friendDomain);
        }

        var matchDomain = HotfixBridgeStubSlots.MatchDomain;
        if (matchDomain != null)
        {
            context.Bridges.Publish<IOnlineMatchDomain>(matchDomain);
        }

        if (HotfixBridgeStubSlots.PublishScopeProvider)
        {
            context.Bridges.Publish<IOnlineAuthorizedScopeProvider>(new HotfixBridgeStubScopeProvider());
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用时不做任何处理：已发布的桥由机制在停止期自动撤销，模块不得自行撤销。
    /// </summary>
    /// <remarks>
    /// Does nothing on deactivation, because published bridges are retracted automatically by the mechanism during stop and the module must not retract them itself.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌（本实现不使用） / Cancellation token (unused by this implementation)</param>
    /// <returns>立即返回的已完成任务 / Completed task returned immediately</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        // 桥由机制在停止期自动撤销，模块不得自行撤销。
        return Task.CompletedTask;
    }
}
