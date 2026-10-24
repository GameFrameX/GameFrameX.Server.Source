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
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;

namespace GameFrameX.Tests.Online;


/// <summary>
/// 机制层测试族（发现面 = 测试程序集的部署命名约定副本 <c>GameFrameX.Tests.CapabilityStubs.dll</c>；
/// 族基建只预置管理面测试适配器——<see cref="AdminApiComposition.CreateAdminApiPlane"/> 的解析面）。
/// </summary>
public sealed class MechanismTestFamily : ICapabilityFamily
{
    /// <inheritdoc />
    public string FamilyId
    {
        get { return "mechanism-tests"; }
    }

    /// <inheritdoc />
    public string ConfigurationSectionName
    {
        get { return "MechanismTests"; }
    }

    /// <inheritdoc />
    public string DeploymentAssemblyPrefix
    {
        get { return "GameFrameX.Tests"; }
    }

    /// <summary>最近一次组合的事件发布口捕获（事件总线行为用例经它发布）。</summary>
    public static ICapabilityEventPublisher LastEventPublisher;

    /// <inheritdoc />
    public void RegisterInfrastructure(CapabilityFamilyContext context)
    {
        // 管理面测试适配器：五个机制用错误码位 + 恒放行作用域（机制口径，不绑 Online 语义）。
        var code = new AdminApiErrorCode(0, "mechanism.test");
        context.AddService(new AdminApiErrorCodes(code, code, code, code, code));
        context.AddService<IAdminApiScopeAdapter>(new MechanismTestScopeAdapter());
        LastEventPublisher = context.EventPublisher;
    }
}

/// <summary>
/// 机制测试作用域适配器（从请求体读三元组 + 恒放行——作用域守卫语义另有 Online 侧用例锚定）。
/// </summary>
