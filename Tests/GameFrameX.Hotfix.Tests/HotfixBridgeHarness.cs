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

using System;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 域桥发布辅助（C294：替代旧进程级 holder 的 Publish / Retract）——写桩槽位 → 真实
/// <see cref="CapabilityComposition.Compose"/> → <see cref="CapabilityRuntime.StartAsync"/>（桩模块激活期发布桥）→
/// 执行用例 → finally <see cref="CapabilityRuntime.StopAsync"/>（宿主停机自动撤桥）+ 槽位复位。
/// 「租约已撤销」= 用例体内先 StopAsync 再发请求。
/// </summary>
public static class HotfixBridgeHarness
{
    /// <summary>
    /// 发布好友域桥（+ 可选授权作用域提供者桥）后执行用例。
    /// </summary>
    /// <param name="friendDomain">好友域桥契约桩。</param>
    /// <param name="body">用例体（入参为已启动的组合产物）。</param>
    /// <param name="publishScopeProvider">是否同时发布授权作用域提供者桥。</param>
    /// <returns>异步任务。</returns>
    public static Task RunWithFriendDomainAsync(IOnlineFriendDomain friendDomain, Func<CapabilityRuntime, Task> body, bool publishScopeProvider = true)
    {
        HotfixBridgeStubSlots.FriendDomain = friendDomain;
        HotfixBridgeStubSlots.PublishScopeProvider = publishScopeProvider;
        return RunAsync(body);
    }

    /// <summary>
    /// 发布匹配域桥（+ 可选授权作用域提供者桥）后执行用例。
    /// </summary>
    /// <param name="matchDomain">匹配域桥契约桩。</param>
    /// <param name="body">用例体（入参为已启动的组合产物）。</param>
    /// <param name="publishScopeProvider">是否同时发布授权作用域提供者桥。</param>
    /// <returns>异步任务。</returns>
    public static Task RunWithMatchDomainAsync(IOnlineMatchDomain matchDomain, Func<CapabilityRuntime, Task> body, bool publishScopeProvider = true)
    {
        HotfixBridgeStubSlots.MatchDomain = matchDomain;
        HotfixBridgeStubSlots.PublishScopeProvider = publishScopeProvider;
        return RunAsync(body);
    }

    /// <summary>
    /// 组合并启动桩族、执行用例，finally 停机撤桥并复位槽位（组合失败时槽位同样复位）。
    /// </summary>
    /// <param name="body">用例体。</param>
    /// <returns>异步任务。</returns>
    private static async Task RunAsync(Func<CapabilityRuntime, Task> body)
    {
        CapabilityRuntime runtime = null;
        try
        {
            var options = new CapabilityCompositionOptions(default, new HotfixBridgeAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions
                {
                    TenantId = HotfixBridgeStubScopeProvider.TenantId,
                    AppId = HotfixBridgeStubScopeProvider.AppId,
                    ServerId = HotfixBridgeStubScopeProvider.ServerId,
                },
            };
            runtime = CapabilityComposition.Compose(new HotfixBridgeTestFamily(), options);
            await runtime.StartAsync();
            await body(runtime);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            HotfixBridgeStubSlots.Reset();
        }
    }
}
