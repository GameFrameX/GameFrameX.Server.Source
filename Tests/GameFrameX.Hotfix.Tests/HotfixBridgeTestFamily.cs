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

using GameFrameX.Capability.Hosting.Abstractions;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 域桥适配器测试族（C294）：发现面 = 测试程序集的部署命名约定副本
/// <c>GameFrameX.Hotfix.Tests.BridgeStubs.dll</c>（机制按 <c>{前缀}.*.dll</c> 扫描后解析回本测试程序集），
/// 让桩能力模块走真实「Compose → ActivateAsync 发布桥 → StopAsync 撤桥」路径；族基建为空。
/// </summary>
public sealed class HotfixBridgeTestFamily : ICapabilityFamily
{
    /// <summary>
    /// 获取测试能力族标识，固定返回 <c>hotfix-bridge-tests</c>。
    /// </summary>
    /// <remarks>
    /// Gets the test capability family identifier; always returns <c>hotfix-bridge-tests</c>.
    /// </remarks>
    /// <value>固定的族标识 / Fixed family identifier</value>
    public string FamilyId
    {
        get { return "hotfix-bridge-tests"; }
    }

    /// <summary>
    /// 获取测试能力族的配置节名，固定返回 <c>HotfixBridgeTests</c>。
    /// </summary>
    /// <remarks>
    /// Gets the configuration section name of the test family; always returns <c>HotfixBridgeTests</c>.
    /// </remarks>
    /// <value>固定的配置节名 / Fixed configuration section name</value>
    public string ConfigurationSectionName
    {
        get { return "HotfixBridgeTests"; }
    }

    /// <summary>
    /// 获取部署程序集前缀，固定返回 <c>GameFrameX.Hotfix.Tests</c>，使本测试程序集内的桩模块参与发现。
    /// </summary>
    /// <remarks>
    /// Gets the deployment assembly prefix; always returns <c>GameFrameX.Hotfix.Tests</c> so stub modules in this test assembly are discovered.
    /// </remarks>
    /// <value>固定的部署程序集前缀 / Fixed deployment assembly prefix</value>
    public string DeploymentAssemblyPrefix
    {
        get { return "GameFrameX.Hotfix.Tests"; }
    }

    /// <summary>
    /// 不注册任何族基建：域桥测试的桥契约全部由桩模块在激活期发布。
    /// </summary>
    /// <remarks>
    /// Registers no family infrastructure, because all bridge contracts in the domain bridge tests are published by the stub module at activation time.
    /// </remarks>
    /// <param name="context">能力族上下文（本实现不使用） / Capability family context (unused by this implementation)</param>
    public void RegisterInfrastructure(CapabilityFamilyContext context)
    {
        // 域桥测试不需要族基建：桥契约全部由桩模块在激活期发布。
    }
}
