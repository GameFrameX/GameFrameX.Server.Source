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
using System.Linq;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;
using Xunit;
using LocalizationKeys = GameFrameX.Localization.Keys;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 能力机制层装配测试（C294：经唯一组合面 <see cref="CapabilityComposition.Compose"/> + 机制测试族槽位，
/// 锚定拓扑定序、必选闭包 fail-fast、同 Id fail-fast、过滤器语义、扫描发现、宿主选项校验与 job 全名防重复）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class CapabilityHostingTests
{
    /// <summary>
    /// 构造机制组合选项（合法作用域三元组 + 白名单 / 排除表圈定参与面）。
    /// </summary>
    /// <param name="log">装配日志收集器。</param>
    /// <param name="enabledIds">白名单（参与本次组合的能力 Id）。</param>
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
    /// 拓扑定序：依赖方排在被依赖方之后；同层按能力 Id 序数定序（不再按发现序 / 加入序）——
    /// 无依赖的 topo-gamma 虽与 topo-alpha 同处首层，仍排在序数更小的 topo-beta 之后。
    /// </summary>
    [Fact]
    public async Task Compose_OrdersDependentAfterDependencyAndSameLevelByIdOrdinal()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Id = "topo-beta";
            MechanismScenario.First.Requires = new[] { "topo-alpha", };
            MechanismScenario.Second.Id = "topo-alpha";
            MechanismScenario.Third.Id = "topo-gamma";

            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "topo-beta", "topo-alpha", "topo-gamma", }));

            Assert.Equal(new[] { "topo-alpha", "topo-beta", "topo-gamma", }, runtime.Report.EnabledIdsInOrder);
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
    /// 必选闭包 fail-fast：Requires 指向未发现的能力即抛装配汇总异常（C294 起为 <see cref="CapabilityAssemblyException"/>）。
    /// </summary>
    [Fact]
    public void Compose_MissingRequirement_FailsFastWithAssemblyException()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Requires = new[] { "missing-capability", };

            var exception = Assert.Throws<CapabilityAssemblyException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", })));

            Assert.Single(exception.Problems);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// manifest Id 重复即装配失败：两个槽位同 Id 时部署目录扫描直接 fail-fast（不再静默取首）。
    /// </summary>
    [Fact]
    public void Compose_DuplicateCapabilityId_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Id = "dup-capability";
            MechanismScenario.Second.Id = "dup-capability";

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "dup-capability", })));
            Assert.Contains("dup-capability", exception.Message);

            Assert.Throws<InvalidOperationException>(() => CapabilityAssembly.DiscoverFromDeploymentDirectory(new MechanismTestFamily().DeploymentAssemblyPrefix));
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 过滤器引用未发现的能力 Id 即装配失败（排除表与白名单两种形态）。
    /// </summary>
    [Fact]
    public void Compose_FilterTargetMissing_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            var expectedMessage = string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.FilterTargetMissing), "no-such-capability");

            var disabled = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", }, new[] { "no-such-capability", })));
            Assert.Equal(expectedMessage, disabled.Message);

            var enabled = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", "no-such-capability", })));
            Assert.Equal(expectedMessage, enabled.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 排除表语义：排除表命中的能力不装配，报告判定为 ExcludedByDisableList（排除表优先于白名单）。
    /// </summary>
    [Fact]
    public async Task Compose_DisabledCapability_ExcludedFromEnabledOrder()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", "mech-second", }, new[] { "mech-second", }));

            Assert.Equal(new[] { "mech-first", }, runtime.Report.EnabledIdsInOrder);
            Assert.Equal(CapabilityFilterState.ExcludedByDisableList, runtime.Report.Entries.Single(entry => entry.Manifest.Id == "mech-second").State);
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
    /// 白名单形态：清单段未收录的能力标记 NotInEnableList，收录的照常装配。
    /// </summary>
    [Fact]
    public async Task Compose_EnableList_FiltersUnlistedCapabilities()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            runtime = Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", }));

            Assert.Equal(new[] { "mech-first", }, runtime.Report.EnabledIdsInOrder);
            Assert.Equal(CapabilityFilterState.Enabled, runtime.Report.Entries.Single(entry => entry.Manifest.Id == "mech-first").State);
            Assert.Equal(CapabilityFilterState.NotInEnableList, runtime.Report.Entries.Single(entry => entry.Manifest.Id == "mech-second").State);
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
    /// 扫描发现：程序集扫描能发现测试程序集内的槽位模块（机制开放性——无硬编码清单）。
    /// </summary>
    [Fact]
    public void DiscoverModules_FindsSlotModulesFromAssembly()
    {
        MechanismScenario.Reset();
        try
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(MechanismFirstSlotModule).Assembly, });
            var ids = modules.Select(module => module.Manifest.Id).ToArray();

            Assert.Contains("mech-first", ids);
            Assert.Contains("mech-second", ids);
            Assert.Contains("mech-third", ids);
            Assert.Contains("mech-fourth", ids);
            Assert.Contains(modules, module => module is MechanismFirstSlotModule);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 宿主选项非法（作用域三元组非正）即组合失败。
    /// </summary>
    [Fact]
    public void Compose_InvalidHostOptions_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            var options = CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", });
            options.HostOptions = new CapabilityHostOptions { TenantId = 0, AppId = 1, ServerId = 1, };

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Equal(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.OptionsInvalid), exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// job 全名防重复：同能力 Register 内重复 jobId 即组合失败（全名 = 能力Id/jobId）。
    /// </summary>
    [Fact]
    public void Compose_DuplicateJobIdWithinCapability_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Register = context =>
            {
                context.AddTickJob("sweep", cancellationToken => Task.CompletedTask);
                context.AddTickJob("sweep", cancellationToken => Task.CompletedTask);
            };

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions(new CollectingCapabilityAssemblyLog(), new[] { "mech-first", })));

            Assert.Contains("mech-first/sweep", exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }
}
