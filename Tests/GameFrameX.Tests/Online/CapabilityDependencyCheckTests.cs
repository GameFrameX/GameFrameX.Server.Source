// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;
using Xunit;
using LocalizationKeys = GameFrameX.Localization.Keys;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 能力依赖检查测试（设计 §6.4：必选缺失一次报全 + 汇总日志、可选缺失的报告与日志披露、环路完整路径、
/// 缺席原因三分、发现序无关的确定性定序、Register 期解析来源检查与 GetServices 阶段限制）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class CapabilityDependencyCheckTests
{
    /// <summary>
    /// 解析来源检查用的探针服务契约（由某个槽位能力注册，另一个槽位尝试解析）。
    /// </summary>
    public interface IScopeProbeService
    {
        /// <summary>探针名（区分实例）。</summary>
        string Name { get; }
    }

    /// <summary>
    /// 探针服务实现。
    /// </summary>
    private sealed class ScopeProbeService : IScopeProbeService
    {
        /// <summary>
        /// 初始化探针。
        /// </summary>
        /// <param name="name">探针名。</param>
        public ScopeProbeService(string name)
        {
            Name = name;
        }

        /// <inheritdoc />
        public string Name { get; }
    }

    /// <summary>
    /// 构造机制组合选项（合法作用域三元组 + 白名单 / 排除表圈定参与面）。
    /// </summary>
    /// <param name="log">装配日志收集器。</param>
    /// <param name="enabledIds">白名单。</param>
    /// <param name="disabledIds">排除表（可空）。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateOptions(CollectingCapabilityAssemblyLog log, string[] enabledIds, string[] disabledIds = null)
    {
        return new CapabilityCompositionOptions(default, log)
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, },
            Filter = new CapabilityAssemblyFilter(enabledIds, disabledIds),
        };
    }

    /// <summary>
    /// 以机制测试族执行一次真实组合。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <returns>组合产物。</returns>
    private static CapabilityRuntime Compose(CapabilityCompositionOptions options)
    {
        return CapabilityComposition.Compose(new MechanismTestFamily(), options);
    }

    /// <summary>
    /// 组合一次、取装配序后立即停止（确定性定序比较用）。
    /// </summary>
    /// <param name="enabledIds">白名单。</param>
    /// <returns>装配序。</returns>
    private static async Task<string[]> ComposeOrderAsync(string[] enabledIds)
    {
        var runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), enabledIds));
        try
        {
            return runtime.Report.EnabledIdsInOrder.ToArray();
        }
        finally
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// 必选依赖缺失问题文案（与机制拼接口径一致：依赖方 + 被依赖方 + 缺席原因）。
    /// </summary>
    /// <param name="dependentId">依赖方能力 Id。</param>
    /// <param name="requiredId">被依赖能力 Id。</param>
    /// <param name="reasonKey">缺席原因本地化键。</param>
    /// <returns>问题文案。</returns>
    private static string RequiredMissingProblem(string dependentId, string requiredId, string reasonKey)
    {
        return string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyRequiredDependencyMissing), dependentId, requiredId, LocalizationService.GetString(reasonKey));
    }

    /// <summary>
    /// 一次报全：同一组合内多个能力的多条必选缺失全部进入 Problems（含三种缺席原因），
    /// 装配日志 Error 流 = 汇总行 + 每条问题一行（两空格缩进）；依赖检查失败时任何模块 Register 都不执行。
    /// </summary>
    [Fact]
    public void Compose_SeveralMissingRequirements_ReportsAllProblemsAtOnce()
    {
        MechanismScenario.Reset();
        try
        {
            var registerInvoked = false;
            MechanismScenario.First.Requires = new[] { "mech-second", "mech-third", "ghost-one", };
            MechanismScenario.First.Register = context => registerInvoked = true;
            MechanismScenario.Fourth.Requires = new[] { "ghost-two", };
            MechanismScenario.Fourth.Register = context => registerInvoked = true;
            var log = new CollectingCapabilityAssemblyLog();

            var exception = Assert.Throws<CapabilityAssemblyException>(() => Compose(CreateOptions(log, new[] { "mech-first", "mech-fourth", }, new[] { "mech-third", })));

            // 问题序 = 启用模块的发现序（槽位类型全名 Ordinal：First < Fourth）× manifest 声明序。
            var expectedProblems = new[]
            {
                RequiredMissingProblem("mech-first", "mech-second", LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonNotInEnableList),
                RequiredMissingProblem("mech-first", "mech-third", LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonExcludedByDisableList),
                RequiredMissingProblem("mech-first", "ghost-one", LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonNotDiscovered),
                RequiredMissingProblem("mech-fourth", "ghost-two", LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonNotDiscovered),
            };
            Assert.Equal(expectedProblems, exception.Problems);
            Assert.False(registerInvoked);

            var expectedErrors = new List<string>
            {
                string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyFailed), expectedProblems.Length),
            };
            expectedErrors.AddRange(expectedProblems.Select(problem => "  " + problem));
            Assert.Equal(expectedErrors, log.Errors);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 可选依赖缺失是正常装配形态：模块照常 Register 并进入装配序；报告条目记录 Kind=Optional / State=NotAssembled
    /// 与缺席原因，AbsentFeatures 为缺失功能的本地化文案；装配日志打一行固定格式的 Info，不打 Error。
    /// </summary>
    [Fact]
    public async Task Compose_MissingOptionalDependency_AssemblesAndDisclosesAbsentFeature()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var featureKey = LocalizationKeys.Capability.OnlineModule.Match.OptionalAssetsAbsent;
            var registerInvoked = false;
            MechanismScenario.First.Optional = new[] { new CapabilityOptionalDependency("ghost-optional", featureKey), };
            MechanismScenario.First.Register = context => registerInvoked = true;
            var log = new CollectingCapabilityAssemblyLog();

            runtime = Compose(CreateOptions(log, new[] { "mech-first", }));

            Assert.True(registerInvoked);
            Assert.Contains("mech-first", runtime.Report.EnabledIdsInOrder);
            var entry = runtime.Report.Entries.Single(candidate => candidate.Manifest.Id == "mech-first");
            Assert.Equal(CapabilityFilterState.Enabled, entry.State);
            var dependency = Assert.Single(entry.Dependencies);
            Assert.Equal("ghost-optional", dependency.CapabilityId);
            Assert.Equal(CapabilityDependencyKind.Optional, dependency.Kind);
            Assert.Equal(CapabilityDependencyAssemblyState.NotAssembled, dependency.State);
            Assert.Equal(CapabilityDependencyAbsenceReason.NotDiscovered, dependency.AbsenceReason);

            var featureText = LocalizationService.GetString(featureKey);
            Assert.NotEmpty(entry.AbsentFeatures);
            Assert.Equal(new[] { featureText, }, entry.AbsentFeatures);

            var expectedInfo = string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyOptionalDependencyMissing), "mech-first", "ghost-optional", LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonNotDiscovered), featureText);
            Assert.Single(log.Infos, line => line == expectedInfo);
            Assert.Empty(log.Errors);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 缺席原因三分：白名单未收录（NotInEnableList）/ 排除表命中（ExcludedByDisableList）/ 部署目录未发现
    /// （NotDiscovered，依赖了没有任何槽位持有的 Id），报告数据面与 Info 日志逐项区分。
    /// </summary>
    [Fact]
    public async Task Compose_OptionalAbsenceReasons_DistinguishEnableListDisableListAndNotDiscovered()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var featureKey = LocalizationKeys.Capability.OnlineModule.Match.OptionalAssetsAbsent;
            MechanismScenario.First.Optional = new[]
            {
                new CapabilityOptionalDependency("mech-second", featureKey),
                new CapabilityOptionalDependency("mech-third", featureKey),
                new CapabilityOptionalDependency("ghost-optional", featureKey),
            };
            var log = new CollectingCapabilityAssemblyLog();

            runtime = Compose(CreateOptions(log, new[] { "mech-first", }, new[] { "mech-third", }));

            Assert.Equal(new[] { "mech-first", }, runtime.Report.EnabledIdsInOrder);
            Assert.Equal(CapabilityFilterState.NotInEnableList, runtime.Report.Entries.Single(candidate => candidate.Manifest.Id == "mech-second").State);
            Assert.Equal(CapabilityFilterState.ExcludedByDisableList, runtime.Report.Entries.Single(candidate => candidate.Manifest.Id == "mech-third").State);

            var entry = runtime.Report.Entries.Single(candidate => candidate.Manifest.Id == "mech-first");
            Assert.Equal(3, entry.Dependencies.Count);
            Assert.All(entry.Dependencies, dependency => Assert.Equal(CapabilityDependencyAssemblyState.NotAssembled, dependency.State));
            Assert.Equal(CapabilityDependencyAbsenceReason.NotInEnableList, entry.Dependencies.Single(dependency => dependency.CapabilityId == "mech-second").AbsenceReason);
            Assert.Equal(CapabilityDependencyAbsenceReason.ExcludedByDisableList, entry.Dependencies.Single(dependency => dependency.CapabilityId == "mech-third").AbsenceReason);
            Assert.Equal(CapabilityDependencyAbsenceReason.NotDiscovered, entry.Dependencies.Single(dependency => dependency.CapabilityId == "ghost-optional").AbsenceReason);
            Assert.Equal(3, entry.AbsentFeatures.Count);

            var featureText = LocalizationService.GetString(featureKey);
            var template = LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyOptionalDependencyMissing);
            Assert.Contains(string.Format(template, "mech-first", "mech-second", LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonNotInEnableList), featureText), log.Infos);
            Assert.Contains(string.Format(template, "mech-first", "mech-third", LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonExcludedByDisableList), featureText), log.Infos);
            Assert.Contains(string.Format(template, "mech-first", "ghost-optional", LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyReasonNotDiscovered), featureText), log.Infos);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 环路：问题文案列出完整路径（cycle-a → cycle-b → cycle-c → cycle-a，箭头为机制拼接的 " → "），
    /// 同一个环只报一次，并进入 Error 日志。
    /// </summary>
    [Fact]
    public void Compose_DependencyCycle_ReportsFullCyclePath()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Id = "cycle-a";
            MechanismScenario.First.Requires = new[] { "cycle-b", };
            MechanismScenario.Second.Id = "cycle-b";
            MechanismScenario.Second.Requires = new[] { "cycle-c", };
            MechanismScenario.Third.Id = "cycle-c";
            MechanismScenario.Third.Requires = new[] { "cycle-a", };
            var log = new CollectingCapabilityAssemblyLog();

            var exception = Assert.Throws<CapabilityAssemblyException>(() => Compose(CreateOptions(log, new[] { "cycle-a", "cycle-b", "cycle-c", })));

            var problem = Assert.Single(exception.Problems);
            Assert.Contains("cycle-a → cycle-b → cycle-c → cycle-a", problem);
            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyDependencyCycle), "cycle-a → cycle-b → cycle-c → cycle-a"), problem);
            Assert.Contains("  " + problem, log.Errors);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 发现序无关：把能力 Id 在槽位间互换（发现序随之改变）后再组合，装配序完全一致
    /// （依赖边 + 同层 Id 序数决定，与扫描序 / 类型名无关）。
    /// </summary>
    [Fact]
    public async Task Compose_ShuffledDiscoveryOrder_ProducesIdenticalOrder()
    {
        MechanismScenario.Reset();
        try
        {
            var enabledIds = new[] { "shuffle-x", "shuffle-y", "shuffle-z", };
            MechanismScenario.First.Id = "shuffle-x";
            MechanismScenario.First.Requires = new[] { "shuffle-y", };
            MechanismScenario.Second.Id = "shuffle-y";
            MechanismScenario.Third.Id = "shuffle-z";
            var firstOrder = await ComposeOrderAsync(enabledIds);

            MechanismScenario.Reset();
            MechanismScenario.First.Id = "shuffle-z";
            MechanismScenario.Second.Id = "shuffle-x";
            MechanismScenario.Second.Requires = new[] { "shuffle-y", };
            MechanismScenario.Third.Id = "shuffle-y";
            var secondOrder = await ComposeOrderAsync(enabledIds);

            Assert.Equal(new[] { "shuffle-y", "shuffle-x", "shuffle-z", }, firstOrder);
            Assert.Equal(firstOrder, secondOrder);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// Register 期越界解析 fail-fast：mech-second 未声明依赖 mech-first，却解析 mech-first 注册的服务
    /// （即使装配序上对方已注册）——拒绝靠装配顺序碰巧可用的未声明依赖。
    /// </summary>
    [Fact]
    public void Compose_RegisterResolvesServiceOutsideDeclaredDependencies_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Register = context => context.AddService<IScopeProbeService>(new ScopeProbeService("owned-by-first"));
            MechanismScenario.Second.Register = context => context.GetService<IScopeProbeService>();

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", "mech-second", })));

            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.ServiceResolutionForbidden), "mech-second", typeof(IScopeProbeService).FullName, "mech-first"), exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 对照：声明了必选依赖后，Register 期解析对方注册的服务合法且拿到同一实例。
    /// </summary>
    [Fact]
    public async Task Compose_RegisterResolvesServiceOfDeclaredRequirement_Succeeds()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var probe = new ScopeProbeService("owned-by-first");
            IScopeProbeService resolved = null;
            MechanismScenario.First.Register = context => context.AddService<IScopeProbeService>(probe);
            MechanismScenario.Second.Requires = new[] { "mech-first", };
            MechanismScenario.Second.Register = context => resolved = context.GetService<IScopeProbeService>();

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", "mech-second", }));

            Assert.Same(probe, resolved);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// GetServices 在 Register 期恒抛（枚举结果不得随装配顺序变化）。
    /// </summary>
    [Fact]
    public void Compose_GetServicesDuringRegister_Throws()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Register = context => context.GetServices<IScopeProbeService>();

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", })));

            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.ServicesEnumerationForbidden), "mech-first", typeof(IScopeProbeService).FullName), exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// GetServices 阶段边界：宿主侧在 Activate 开始前同样拒绝枚举；Activate 期与之后枚举可用且列出已注册实现。
    /// </summary>
    [Fact]
    public async Task GetServices_AvailableFromActivationOnward()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var probe = new ScopeProbeService("enumerated");
            IReadOnlyList<IScopeProbeService> enumeratedDuringActivate = null;
            MechanismScenario.First.Register = context => context.AddService<IScopeProbeService>(probe);
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                enumeratedDuringActivate = context.Services.GetServices<IScopeProbeService>();
                return Task.CompletedTask;
            };

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", }));

            var beforeStart = Assert.Throws<InvalidOperationException>(() => runtime.Host.Services.GetServices<IScopeProbeService>());
            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.ServicesEnumerationForbidden), "host", typeof(IScopeProbeService).FullName), beforeStart.Message);

            await runtime.StartAsync();

            Assert.NotNull(enumeratedDuringActivate);
            Assert.Same(probe, Assert.Single(enumeratedDuringActivate));
            Assert.Same(probe, Assert.Single(runtime.Host.Services.GetServices<IScopeProbeService>()));
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }
}
