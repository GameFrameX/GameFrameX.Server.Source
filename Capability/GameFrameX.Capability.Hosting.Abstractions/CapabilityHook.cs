// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力钩子挂载入口（Hotfix 侧发布、能力侧消费；与 <see cref="CapabilityBridge{TContract}"/> 方向相反、
/// 共用同一套槽位实现）。
/// <para>
/// 维护约束：钩子不在装配期注入实例——能力只持有槽位句柄（<see cref="CapabilityRegistrationContext.DeclareHook{TContract}"/>），
/// 每次调用读 <see cref="CapabilityHookHandle{TContract}.Current"/>；<see cref="Attach"/> 原子替换旧挂载并使旧租约失效，
/// 新版本热更装载完成即重挂、旧版本退役时其租约已失效，能力侧不会钉住旧程序集。
/// <see cref="Attach"/> 只在 Hotfix 程序集合法（GFX0018 符号检查）。
/// </para>
/// </summary>
/// <typeparam name="TContract">钩子契约类型。</typeparam>
public static class CapabilityHook<TContract> where TContract : class
{
    /// <summary>
    /// 钩子槽位（与 <see cref="CapabilityBridge{TContract}"/> 各自独立、共用同一套槽位实现）。
    /// </summary>
    private static readonly CapabilityContractSlot<TContract> Slot = new CapabilityContractSlot<TContract>();

    /// <summary>
    /// 声明钩子与默认策略（能力 Register 期经 <see cref="CapabilityRegistrationContext.DeclareHook{TContract}"/> 调用）。
    /// <para>
    /// 维护约束：释放凭据写入 <paramref name="declarationReleases"/>，由声明方宿主在停止 / 激活失败 / 装配失败时统一释放；
    /// 释放后槽位回到未声明状态，同进程再次组合可重新声明。
    /// </para>
    /// </summary>
    /// <param name="defaultImplementation">默认策略实现。</param>
    /// <param name="declarerId">声明者能力 Id（重复声明诊断用）。</param>
    /// <param name="declarationReleases">声明释放凭据收集器（宿主持有）。</param>
    /// <returns>能力侧句柄。</returns>
    internal static CapabilityHookHandle<TContract> Declare(TContract defaultImplementation, string declarerId, List<IDisposable> declarationReleases)
    {
        if (defaultImplementation == null)
        {
            throw new ArgumentNullException(nameof(defaultImplementation));
        }

        if (declarationReleases == null)
        {
            throw new ArgumentNullException(nameof(declarationReleases));
        }

        var slotRelease = Slot.DeclareDefault(defaultImplementation, declarerId);
        CapabilityHookSlots.MarkDeclared(typeof(TContract));
        declarationReleases.Add(new CapabilityBridgePublication(() =>
        {
            slotRelease.Dispose();
            CapabilityHookSlots.MarkUndeclared(typeof(TContract));
        }));
        return new CapabilityHookHandle<TContract>(Slot);
    }

    /// <summary>
    /// 挂载一个钩子实现（原子替换旧挂载并使旧租约失效；摘下后回能力声明的默认策略）。
    /// </summary>
    /// <param name="implementation">挂载实现（不可为 null）。</param>
    /// <returns>挂载租约（Dispose 摘下当前挂载；已被替换的旧租约 Dispose 为空操作）。</returns>
    public static CapabilityHookLease<TContract> Attach(TContract implementation)
    {
        if (implementation == null)
        {
            throw new ArgumentNullException(nameof(implementation));
        }

        CapabilityHookSlots.MarkAttached(typeof(TContract));
        var detach = Slot.Attach(implementation);
        return new CapabilityHookLease<TContract>(detach);
    }
}
