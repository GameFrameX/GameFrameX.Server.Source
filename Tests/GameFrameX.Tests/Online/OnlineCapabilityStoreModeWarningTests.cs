// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相关法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code repository root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止之行为！
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/cnblanblank
//   CNB 仓库：https://cnb.cool
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Launcher.Online;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Online 存储模式未声明告警判定测试（C302 无声降级防护）：组合根纯谓词
/// <c>OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared</c> 的三态口径——
/// 族启用（双闸任一开）且 <c>UseGameDbStores</c> 未声明（null）才告警；
/// 显式 true / 显式 false / 双闸全关 / 配置段缺失均不告警。
/// </summary>
public class OnlineCapabilityStoreModeWarningTests
{
    /// <summary>
    /// 双闸开 + 未声明（null）→ 告警（原缺陷形态：Game Role 启用 Online 但配置无 UseGameDbStores 键）。
    /// </summary>
    [Fact]
    public void Warns_WhenFamilyEnabledAndUseGameDbStoresUndeclared()
    {
        var online = new OnlineCapabilityOptions
        {
            EnableRuntime = true,
            EnableAdminApi = true,
        };

        Assert.True(OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared(online));
    }

    /// <summary>
    /// 双闸只开其一（admin-only 形态）+ 未声明 → 同样告警。
    /// </summary>
    [Fact]
    public void Warns_WhenOnlyAdminGateEnabledAndUndeclared()
    {
        var online = new OnlineCapabilityOptions
        {
            EnableRuntime = false,
            EnableAdminApi = true,
        };

        Assert.True(OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared(online));
    }

    /// <summary>
    /// 显式声明（true 落库 / false 有意易失）→ 不告警：声明即消歧。
    /// </summary>
    [Fact]
    public void DoesNotWarn_WhenUseGameDbStoresExplicitlyDeclared()
    {
        var persisted = new OnlineCapabilityOptions
        {
            EnableRuntime = true,
            EnableAdminApi = true,
            UseGameDbStores = true,
        };

        var volatileExplicit = new OnlineCapabilityOptions
        {
            EnableRuntime = true,
            EnableAdminApi = true,
            UseGameDbStores = false,
        };

        Assert.False(OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared(persisted));
        Assert.False(OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared(volatileExplicit));
    }

    /// <summary>
    /// 双闸全关（族未启用，零装配零日志）与配置段缺失 → 不告警。
    /// </summary>
    [Fact]
    public void DoesNotWarn_WhenFamilyDisabledOrSectionMissing()
    {
        var disabled = new OnlineCapabilityOptions
        {
            EnableRuntime = false,
            EnableAdminApi = false,
        };

        Assert.False(OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared(disabled));
        Assert.False(OnlineCapabilityCompositionRoot.ShouldWarnStoreModeUndeclared(null));
    }
}
