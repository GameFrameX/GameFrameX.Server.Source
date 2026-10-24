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
    /// <summary>
    /// 获取机制测试族的族标识，固定返回 <c>mechanism-tests</c>，与业务族区分，避免机制用例和真实能力族互相干扰。
    /// </summary>
    /// <remarks>
    /// Returns the fixed family identifier <c>mechanism-tests</c>, keeping mechanism test cases separate from real capability families.
    /// </remarks>
    /// <value>固定值 <c>mechanism-tests</c> / Fixed value <c>mechanism-tests</c></value>
    public string FamilyId
    {
        get { return "mechanism-tests"; }
    }

    /// <summary>
    /// 获取机制测试族的配置段名，固定返回 <c>MechanismTests</c>，用例按这个段名提供能力参数配置。
    /// </summary>
    /// <remarks>
    /// Returns the fixed configuration section name <c>MechanismTests</c>, under which test cases supply capability options.
    /// </remarks>
    /// <value>固定值 <c>MechanismTests</c> / Fixed value <c>MechanismTests</c></value>
    public string ConfigurationSectionName
    {
        get { return "MechanismTests"; }
    }

    /// <summary>
    /// 获取部署程序集名前缀，固定返回 <c>GameFrameX.Tests</c>，让发现流程能扫描到按部署命名约定复制出的 <c>GameFrameX.Tests.CapabilityStubs.dll</c> 中的四个槽位模块。
    /// </summary>
    /// <remarks>
    /// Returns the fixed deployment assembly prefix <c>GameFrameX.Tests</c>, so discovery picks up the four slot modules from the convention-named copy <c>GameFrameX.Tests.CapabilityStubs.dll</c>.
    /// </remarks>
    /// <value>固定值 <c>GameFrameX.Tests</c> / Fixed value <c>GameFrameX.Tests</c></value>
    public string DeploymentAssemblyPrefix
    {
        get { return "GameFrameX.Tests"; }
    }

    /// <summary>最近一次组合的事件发布口捕获（事件总线行为用例经它发布）。</summary>
    public static ICapabilityEventPublisher LastEventPublisher;

    /// <summary>
    /// 预置管理面测试适配器：注册一组五个错误码位都相同（代码 0、名称 <c>mechanism.test</c>）的 <see cref="AdminApiErrorCodes"/>，注册恒放行的 <see cref="MechanismTestScopeAdapter"/> 作为 <see cref="IAdminApiScopeAdapter"/>，并把上下文的事件发布口保存到 <see cref="LastEventPublisher"/> 供事件总线用例使用；不绑定任何 Online 业务语义。
    /// </summary>
    /// <remarks>
    /// Seeds the admin API test adapters: registers <c>AdminApiErrorCodes</c> whose five slots all share one code (0, <c>mechanism.test</c>), registers the always-allow <c>MechanismTestScopeAdapter</c> as <c>IAdminApiScopeAdapter</c>, and stores the context event publisher in <c>LastEventPublisher</c> for event bus test cases. No Online business semantics are bound.
    /// </remarks>
    /// <param name="context">族基础设施注册上下文，用于登记服务并提供事件发布口 / Family infrastructure context used to add services and supply the event publisher</param>
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
