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
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Core;

namespace GameFrameX.Capability.Online.Foundation;

/// <summary>
/// 平台地基能力包的 hosting 扩展：<see cref="AddOnlineFoundationRuntime"/> 装配入口
/// （回调式，注册自装配模块）。地基三 store 的替换缝经共享服务面预置（模块 Register 期探测命中，
/// 未命中构造 InMemory 默认），不另设包级语法糖。
/// <para>
/// 维护约束：模块无参构造，显式链式与真扫描（组合根 <see cref="CapabilityAssembly"/> 发现）共用同一模块类；
/// 生产组合根（Launcher 的 AddOnlineCapabilities）经真扫描装配，不经本入口。
/// </para>
/// </summary>
/// <remarks>
/// Hosting extensions of the foundation capability package: the
/// <see cref="AddOnlineFoundationRuntime"/> assembly entry (callback style, registering the
/// self-assembling module). The three foundation stores probe the shared service face at module
/// <c>Register</c> time with in-memory fallbacks; no package-level sugar is added for those seams.
/// The production composition root assembles through real scanning rather than this entry.
/// </remarks>
public static class OnlineFoundationHostingExtensions
{
    /// <summary>
    /// 装配平台地基能力运行时（注册自装配模块；<c>options => ...</c> 回调承载包级编排参数）。
    /// </summary>
    /// <param name="builder">能力装配器。</param>
    /// <param name="configure">包级选项回调（可空 = 全默认）。</param>
    /// <returns>本装配器（链式）。</returns>
    public static CapabilityHostBuilder AddOnlineFoundationRuntime(this CapabilityHostBuilder builder, Action<OnlineFoundationOptions> configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new OnlineFoundationOptions();
        configure?.Invoke(options);
        builder.UseService(options);
        return builder.AddModule(new OnlineFoundationCapabilityModule());
    }
}
