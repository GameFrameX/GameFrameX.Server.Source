// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规之开源许可证之规定。
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

namespace GameFrameX.Utility.Hotload;

/// <summary>
/// 可卸载程序集模块契约。
/// Contract for an unloadable assembly-backed module.
/// </summary>
/// <remarks>
/// 由装载方（如 Core 的热更模块）实现；<see cref="CollectibleAssemblyHost{TModule}"/> 仅经本契约驱动
/// 退役宽限与卸载验证，不感知模块的业务构成。
/// Implemented by the loading side (e.g. the Core hotfix module); <see cref="CollectibleAssemblyHost{TModule}"/>
/// drives retirement grace and unload verification purely through this contract, unaware of module internals.
/// </remarks>
public interface ICollectibleAssemblyModule
{
    /// <summary>
    /// 模块装载的程序集（退役期内按程序集身份路由的判定键）。
    /// The assembly loaded by this module (the identity key for routing during retirement).
    /// </summary>
    Assembly LoadedAssembly
    {
        get;
    }

    /// <summary>
    /// 卸载模块底层的可收集上下文。
    /// Unloads the module's underlying collectible context.
    /// </summary>
    /// <returns>
    /// 指向已卸载可收集 ALC 的弱引用（供宿主做释放排空判定）；模块未持有 ALC（如测试构造）时返回 <c>null</c>。
    /// A weak reference to the unloaded collectible ALC (for the host's drain detection); <c>null</c> when the
    /// module owns no ALC (e.g. test-constructed modules).
    /// </returns>
    WeakReference Unload();
}
