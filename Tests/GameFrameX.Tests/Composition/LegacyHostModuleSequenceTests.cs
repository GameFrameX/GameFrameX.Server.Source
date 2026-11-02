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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Launcher.Composition;
using GameFrameX.Tests.Online;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Composition;

/// <summary>
/// 旧栈宿主模块启停顺序断言（双装配裁决）：早段 <see cref="LegacyRuntimeHostModule"/>（控制库 → 发现层 →
/// 业务库 → 组件注册 → 热更装载）先于能力模块激活；晚段 <see cref="LegacyNetworkListenerHostModule"/>
///（监听 → ServiceOnline → 就绪屏障 → Active）晚于能力模块激活——等价旧模板序列「热更 → 能力 → 监听」；
/// 停用严格逆序（晚段 ServiceOffline → 网络停机 → 能力停用）。假宿主记录调用序列，无数据库 / 网络依赖。
/// </summary>
/// <remarks>
/// The legacy host-module start/stop order assertions (the dual-assembly verdict): the early
/// <see cref="LegacyRuntimeHostModule"/> (control database → discovery → business database → component
/// registration → hotfix load) activates before the capability modules; the late
/// <see cref="LegacyNetworkListenerHostModule"/> (listening → ServiceOnline → the ready barrier →
/// Active) activates after them — equivalent to the legacy template sequence
/// "hotfix → capability → listen"; deactivation follows the strict reverse order (the late module's
/// ServiceOffline → network shutdown → capability teardown). A fake owner records the call sequence
/// with no database or network dependency.
/// </remarks>
public class LegacyHostModuleSequenceTests
{
    /// <summary>
    /// 记录调用序列的假宿主（实现 <see cref="ILegacyRuntimeHostOwner"/> 全部成员，无副作用）。
    /// </summary>
    private sealed class RecordingLegacyRuntimeHostOwner : ILegacyRuntimeHostOwner
    {
        /// <summary>调用序列记录器。</summary>
        public List<string> Calls { get; } = new List<string>();

        /// <summary>无 Online 段的最小配置。</summary>
        public AppSetting Setting { get; } = new AppSetting();

        public string CurrentStopMessage { get; set; } = "unit-test-stop";

        public Task LoadHotfixModuleAsync()
        {
            Calls.Add("hotfix-load");
            return Task.CompletedTask;
        }

        public Task InitializeControlDatabaseAsync()
        {
            Calls.Add("control-database");
            return Task.CompletedTask;
        }

        public Task ActivateDiscoveryLayerAsync()
        {
            Calls.Add("discovery");
            return Task.CompletedTask;
        }

        public Task InitializeBusinessDatabaseAsync()
        {
            Calls.Add("business-database");
            return Task.CompletedTask;
        }

        public Task StartNetworkListenersAsync()
        {
            Calls.Add("listen");
            return Task.CompletedTask;
        }

        public Task OnStartupCompletedAsync()
        {
            Calls.Add("service-online");
            return Task.CompletedTask;
        }

        public void MarkStartUpReadyBarrier()
        {
            Calls.Add("ready-barrier");
        }

        public Task MarkDiscoveryActiveAsync()
        {
            Calls.Add("mark-active");
            return Task.CompletedTask;
        }

        public bool ShouldPublishInnerKcpEndpoint
        {
            get
            {
                Calls.Add("kcp-check");
                return false;
            }
        }

        public void PublishInnerKcpAdvertiseEndpoint()
        {
            Calls.Add("kcp-publish");
        }

        public Task OnStoppingAsync(string message)
        {
            Calls.Add("service-offline:" + message);
            return Task.CompletedTask;
        }

        public Task StopNetworkAndMarkStoppedAsync(string message)
        {
            Calls.Add("network-stop:" + message);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 记录激活 / 停用的假模块（Id 取 <c>zz-</c> 前缀保证在 After 相位内按 Id 序数最后激活、最先停用——
    /// 停用逆序断言的观测点）。
    /// </summary>
    private sealed class RecordingTrailingHostModule : ICapabilityModule
    {
        private readonly List<string> _calls;

        public RecordingTrailingHostModule(List<string> calls)
        {
            _calls = calls;
            Manifest = new CapabilityManifest("zz-recording-trailing", "1.0.0");
        }

        public CapabilityManifest Manifest { get; }

        public void Register(CapabilityRegistrationContext context)
        {
        }

        public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
        {
            _calls.Add("trailing-activate");
            return Task.CompletedTask;
        }

        public Task DeactivateAsync(CancellationToken cancellationToken)
        {
            _calls.Add("trailing-stop");
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 构造含早晚宿主模块的组合选项（作用域三元组取合法值满足机制校验）。
    /// </summary>
    private static CapabilityCompositionOptions BuildOptions(RecordingLegacyRuntimeHostOwner owner)
    {
        var options = new CapabilityCompositionOptions(JsonSerializer.SerializeToElement(new Dictionary<string, object>()), new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, },
        };
        options.AddHostModule(new LegacyRuntimeHostModule(owner), CapabilityHostModulePhase.BeforeCapabilities);
        options.AddHostModule(new LegacyNetworkListenerHostModule(owner), CapabilityHostModulePhase.AfterCapabilities);
        return options;
    }

    /// <summary>
    /// 无族组合（宿主模块 only）：启动序 = 早段序列 → 晚段序列；停用逆序 = 晚段停用（ServiceOffline → 网络停机）→ 早段停用（空）。
    /// </summary>
    [Fact]
    public async Task HostOnlyComposition_StartsEarlyThenLate_StopsLateFirst()
    {
        var owner = new RecordingLegacyRuntimeHostOwner();
        var runtime = CapabilityComposition.Compose(BuildOptions(owner));
        Assert.Null(runtime.AdminApi);
        await runtime.StartAsync();

        Assert.Equal(new[]
        {
            "control-database",
            "discovery",
            "business-database",
            "hotfix-load",
            "listen",
            "kcp-check",
            "service-online",
            "ready-barrier",
            "mark-active",
        }, owner.Calls);

        await runtime.StopAsync();

        // 停用严格逆序：晚段（ServiceOffline → 网络停机与退出令牌置位）先停；早段停用为有意空实现（热更停止归首个 Role 退出尾部）。
        Assert.Equal(new[]
        {
            "control-database",
            "discovery",
            "business-database",
            "hotfix-load",
            "listen",
            "kcp-check",
            "service-online",
            "ready-barrier",
            "mark-active",
            "service-offline:unit-test-stop",
            "network-stop:unit-test-stop",
        }, owner.Calls);
    }

    /// <summary>
    /// 族合并组合：激活序（<c>Host.Modules</c> 即激活序）= 早段宿主模块 → 全部能力模块（机制测试族的四个槽位）
    /// → 晚段宿主模块——等价旧模板序列「热更 → 能力 → 监听」；停用严格逆序：晚段宿主模块的
    /// ServiceOffline / 网络停机先于任何晚于它的模块停用执行。
    /// </summary>
    [Fact]
    public async Task FamilyMergedComposition_InterleavesHostBeforeCapabilitiesBeforeListener()
    {
        var owner = new RecordingLegacyRuntimeHostOwner();
        var moduleCalls = new List<string>();
        var options = BuildOptions(owner);
        options.AddHostModule(new RecordingTrailingHostModule(moduleCalls), CapabilityHostModulePhase.AfterCapabilities);

        var runtime = CapabilityComposition.Compose(new MechanismTestFamily(), options);
        var activationOrder = runtime.Host.Modules.Select(module => module.Manifest.Id).ToList();

        // 早段先于全部能力模块；晚段晚于全部能力模块；尾部观测模块最后。
        Assert.True(activationOrder.IndexOf("host-legacy-runtime") < activationOrder.IndexOf("mech-first"), "早段宿主模块必须先于能力模块");
        foreach (var capabilityId in new[] { "mech-first", "mech-second", "mech-third", "mech-fourth" })
        {
            Assert.True(activationOrder.IndexOf(capabilityId) > activationOrder.IndexOf("host-legacy-runtime"), "能力模块必须晚于早段宿主模块: " + capabilityId);
            Assert.True(activationOrder.IndexOf(capabilityId) < activationOrder.IndexOf("host-legacy-listener"), "能力模块必须早于晚段宿主模块: " + capabilityId);
        }

        Assert.True(activationOrder.IndexOf("host-legacy-listener") < activationOrder.IndexOf("zz-recording-trailing"), "晚段宿主模块必须早于尾部观测模块");

        await runtime.StartAsync();

        // 序列级断言：热更装载先于监听（等价旧模板「热更 → 能力 → 监听」的外侧边界）。
        Assert.True(owner.Calls.IndexOf("hotfix-load") >= 0, "早段热更装载必须执行");
        Assert.True(owner.Calls.IndexOf("listen") > owner.Calls.IndexOf("hotfix-load"), "监听必须晚于热更装载");
        Assert.Contains("trailing-activate", moduleCalls);

        await runtime.StopAsync();

        // 停用严格逆序：尾部观测模块（激活序最后）先停，随后晚段宿主模块（ServiceOffline → 网络停机）。
        Assert.True(moduleCalls.IndexOf("trailing-stop") >= 0, "尾部观测模块必须停用");
        Assert.True(owner.Calls.IndexOf("service-offline:unit-test-stop") > moduleCalls.IndexOf("trailing-stop"), "晚段 ServiceOffline 必须晚于尾部观测模块停用");
        Assert.True(owner.Calls.IndexOf("network-stop:unit-test-stop") > owner.Calls.IndexOf("service-offline:unit-test-stop"), "网络停机必须晚于 ServiceOffline");
    }

    /// <summary>
    /// 无族组合入口的误用防护：无宿主模块 / 关闭运行面均 fail-fast；正常路径产出业务宿主且无管理面。
    /// </summary>
    [Fact]
    public void HostOnlyComposition_RejectsMisuse()
    {
        var emptyOptions = new CapabilityCompositionOptions(default(JsonElement), new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, },
        };
        Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(emptyOptions));

        var owner = new RecordingLegacyRuntimeHostOwner();
        var disabledOptions = BuildOptions(owner);
        disabledOptions.EnableRuntime = false;
        Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(disabledOptions));
    }
}
