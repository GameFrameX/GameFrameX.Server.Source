// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Operations.Audit;
using GameFrameX.Capability.Online.Operations.HotfixRollback;

namespace GameFrameX.Capability.Online.Operations;

/// <summary>
/// 运营聚合能力包的 hosting 扩展（C232 P5）：<see cref="AddOnlineOperationsRuntime"/> 装配入口（回调式，
/// 注册自装配模块）与三个存储替换缝（<see cref="UseOnlineAuditStore"/> / <see cref="UseOnlineHotfixVersionStore"/> /
/// <see cref="UseOnlineHotfixRollbackExecutor"/>）。
/// <para>
/// 维护约束：options 经共享服务面预置（模块无参构造，显式链式与 P4 真扫描共用同一模块类）；
/// 替换缝在模块 <c>Register</c> 期探测命中，未命中构造 InMemory 默认。
/// </para>
/// </summary>
/// <remarks>
/// Hosting extensions of the operations capability package (C232 P5): the <see cref="AddOnlineOperationsRuntime"/>
/// assembly entry (callback style, registering the self-assembling module) plus three replacement seams.
/// Options are pre-seeded through the shared service face; the replacement seams are probed at module
/// <c>Register</c> time with in-memory fallbacks.
/// </remarks>
public static class OnlineOperationsHostingExtensions
{
    /// <summary>
    /// 装配运营聚合能力运行时（注册自装配模块；<c>options => ...</c> 回调承载包级编排参数）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="configure">包级选项回调（可空 = 全默认）。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder AddOnlineOperationsRuntime(this CapabilityHostBuilder builder, Action<OnlineOperationsOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new OnlineOperationsOptions();
        configure?.Invoke(options);
        builder.UseService(options);
        return builder.AddModule(new OnlineOperationsCapabilityModule());
    }

    /// <summary>
    /// 替换统一审计存储（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineAuditStore(this CapabilityHostBuilder builder, IOnlineAuditStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }

    /// <summary>
    /// 替换 Hotfix 版本存储（默认 InMemory，生产持久化实现经本缝注入）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="store">存储实现。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineHotfixVersionStore(this CapabilityHostBuilder builder, IOnlineHotfixVersionStore store)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(store);
        return builder;
    }

    /// <summary>
    /// 注入 Hotfix 回滚执行器（可选注入位；admin 面暂无触发 action，默认空实现）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="executor">回滚执行器。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder UseOnlineHotfixRollbackExecutor(this CapabilityHostBuilder builder, IOnlineHotfixRollbackExecutor executor)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.UseService(executor);
        return builder;
    }
}
