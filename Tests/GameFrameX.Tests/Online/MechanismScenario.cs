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
/// 机制层测试场景静态状态（四个槽位；全部机制用例须落 <see cref="CapabilityRuntimeCollection"/>
/// 串行集合，避免静态槽位被并行用例交叉覆写）。
/// </summary>
public static class MechanismScenario
{
    /// <summary>槽位一（类型名定序最先被发现）。</summary>
    public static readonly MechanismScenarioSlot First = new("mech-first");

    /// <summary>槽位二。</summary>
    public static readonly MechanismScenarioSlot Second = new("mech-second");

    /// <summary>槽位三。</summary>
    public static readonly MechanismScenarioSlot Third = new("mech-third");

    /// <summary>槽位四。</summary>
    public static readonly MechanismScenarioSlot Fourth = new("mech-fourth");

    /// <summary>
    /// 复位全部槽位（用例 finally 调用，保证槽位缺省态不泄漏到后续用例）。
    /// </summary>
    public static void Reset()
    {
        Reset(First);
        Reset(Second);
        Reset(Third);
        Reset(Fourth);
        MechanismTestFamily.LastEventPublisher = null;
    }

    /// <summary>
    /// 复位单个槽位。
    /// </summary>
    /// <param name="slot">槽位。</param>
    private static void Reset(MechanismScenarioSlot slot)
    {
        slot.Id = null;
        slot.Requires = Array.Empty<string>();
        slot.Optional = Array.Empty<CapabilityOptionalDependency>();
        slot.OptionsType = null;
        slot.Actions = Array.Empty<string>();
        slot.Register = null;
        slot.Activate = null;
        slot.Deactivate = null;
        slot.RegisterAdminActions = null;
    }
}

/// <summary>
/// 机制层测试族（发现面 = 测试程序集的部署命名约定副本 <c>GameFrameX.Tests.CapabilityStubs.dll</c>；
/// 族基建只预置管理面测试适配器——<see cref="AdminApiComposition.CreateAdminApiPlane"/> 的解析面）。
/// </summary>
