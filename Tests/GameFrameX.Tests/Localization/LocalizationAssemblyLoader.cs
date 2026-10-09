// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
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


using System.Reflection;
using System.Runtime.CompilerServices;

namespace GameFrameX.Tests.Localization;

/// <summary>
/// 测试宿主的本地化资源装配：模块初始化时强制加载 GameFrameX.Localization 资源程序集。
/// </summary>
/// <remarks>
/// LocalizationService resolves keys by scanning loaded assemblies for
/// <c>*.Localization.Messages.Resources.resources</c>, while localization key constants are inlined
/// at compile time — no unit-test code path ever touches a runtime type of the resource assembly, so
/// GetString would fall back to returning the raw key and every message-content assertion would fail
/// (root cause of the four known localization test regressions). Loading the assembly once per test
/// host makes ResourceManager probing succeed for all tests.
/// </remarks>
internal static class LocalizationAssemblyLoader
{
    /// <summary>
    /// 模块初始化器：在测试宿主加载本模块时预载资源程序集。
    /// </summary>
    /// <remarks>
    /// Module initializer preloading the resource assembly when the test host loads this module.
    /// </remarks>
    [ModuleInitializer]
    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void Load()
    {
        Assembly.Load("GameFrameX.Localization");
    }
}
