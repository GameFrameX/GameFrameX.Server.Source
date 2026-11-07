// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights or interests of others, as prohibited by laws and regulations.
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using System.Runtime.Loader;

namespace GameFrameX.Utility.Hotload;

/// <summary>
/// 可收集程序集加载上下文。
/// Collectible assembly load context.
/// </summary>
/// <remarks>
/// 继承 <see cref="AssemblyLoadContext"/> 并以 <c>isCollectible: true</c> 构造，是 BCL 可收集 ALC 的官方样例形态
/// （替代历史的 <c>DllLoader</c> + 嵌套 <c>HostAssemblyLoadContext</c> 两层包装）。
/// Inherits <see cref="AssemblyLoadContext"/> constructed with <c>isCollectible: true</c>, the official BCL
/// collectible-ALC sample shape (replacing the historical <c>DllLoader</c> wrapper plus nested context).
/// <para>
/// <see cref="Load"/> 固定返回 <c>null</c>：被加载程序集的引用统一回落 Default ALC 解析，使其与宿主共享同一份
/// 公共程序集（Utility / Core / Network 等），不产生跨 ALC 的类型身份割裂；本上下文自身可被卸载回收。
/// <see cref="Load"/> always returns <c>null</c>: references of the loaded assembly fall back to the Default ALC,
/// so it shares the very same common assemblies (Utility / Core / Network) with the host — no cross-ALC type
/// identity split — while this context itself stays collectible.
/// </para>
/// </remarks>
public sealed class CollectibleAssemblyLoadContext : AssemblyLoadContext
{
    /// <summary>
    /// 初始化可收集程序集加载上下文。
    /// Initializes the collectible assembly load context.
    /// </summary>
    public CollectibleAssemblyLoadContext()
        : base(isCollectible: true)
    {
    }

    /// <summary>
    /// 解析引用程序集：固定返回 <c>null</c> 以回落 Default ALC（见类备注的身份不变量）。
    /// Resolves referenced assemblies: always returns <c>null</c> to fall back to the Default ALC (see the identity invariant in class remarks).
    /// </summary>
    /// <param name="assemblyName">程序集名称 / The assembly name.</param>
    /// <returns>始终为 <c>null</c> / Always <c>null</c>.</returns>
    protected override Assembly Load(AssemblyName assemblyName)
    {
        return null;
    }
}
