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
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 能力服务共享协议机制测试（C294：描述符表形态——模块默认注册 / 宿主覆盖恒胜出 / 命名寻址 /
/// 懒工厂单例 / Activate 期枚举；生产者与消费者全部经 <see cref="MechanismScenario"/> 槽位 +
/// <see cref="CapabilityComposition.Compose"/> 真实装配面驱动）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public class CapabilityServiceSharingTests
{
    /// <summary>
    /// 信号服务（测试载荷：生产槽位注册、消费槽位解析）。
    /// </summary>
    private sealed class SignalService
    {
        /// <summary>获取或设置标记值。</summary>
        public string Marker { get; set; }
    }

    /// <summary>
    /// 从未注册的服务类型（缺失语义断言用）。
    /// </summary>
    private sealed class UnregisteredService
    {
    }

    /// <summary>
    /// 循环解析用例：懒工厂解析 <see cref="CycleBetaService"/> 的服务。
    /// </summary>
    private sealed class CycleAlphaService
    {
    }

    /// <summary>
    /// 循环解析用例：懒工厂反向解析 <see cref="CycleAlphaService"/> 的服务。
    /// </summary>
    private sealed class CycleBetaService
    {
    }

    /// <summary>
    /// 构造机制组合选项（无族配置段；作用域三元组合法；白名单圈定参与槽位）。
    /// </summary>
    /// <param name="enabledIds">参与组合的槽位能力 Id。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateOptions(params string[] enabledIds)
    {
        return new CapabilityCompositionOptions(default, new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 },
            Filter = new CapabilityAssemblyFilter(enabledIds),
        };
    }

    /// <summary>
    /// 组合机制测试族。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <returns>组合产物。</returns>
    private static CapabilityRuntime Compose(CapabilityCompositionOptions options)
    {
        return CapabilityComposition.Compose(new MechanismTestFamily(), options);
    }

    /// <summary>
    /// 模块间共享：消费者声明必选依赖后在 Register 期解析到生产者注册的同一实例；
    /// 组合完成后宿主侧 host.Services 取到的也是同一实例。
    /// </summary>
    [Fact]
    public async Task Compose_ProducerService_ResolvedByRequiringConsumerAndHost()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var produced = new SignalService { Marker = "produced", };
            SignalService resolvedByConsumer = null;
            MechanismScenario.First.Register = context => context.AddService(produced);
            MechanismScenario.Second.Requires = new[] { "mech-first" };
            MechanismScenario.Second.Register = context => resolvedByConsumer = context.GetService<SignalService>();

            runtime = Compose(CreateOptions("mech-first", "mech-second"));

            Assert.Same(produced, resolvedByConsumer);
            Assert.Same(produced, runtime.Host.Services.GetService<SignalService>());
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
    /// 缺失语义：未注册类型宿主侧 GetService 抛出、GetOptionalService 返回 null（替代旧 TryGet 缺失分支）；
    /// 模块注册 null 实例被拒绝。
    /// </summary>
    [Fact]
    public async Task Compose_MissingService_GetThrowsAndOptionalReturnsNull()
    {
        CapabilityRuntime runtime = null;
        try
        {
            Exception nullInstanceError = null;
            MechanismScenario.First.Register = context => nullInstanceError = Record.Exception(() => context.AddService((SignalService)null));

            runtime = Compose(CreateOptions("mech-first"));

            Assert.IsType<ArgumentNullException>(nullInstanceError);
            Assert.Throws<InvalidOperationException>(() => runtime.Host.Services.GetService<UnregisteredService>());
            Assert.Null(runtime.Host.Services.GetOptionalService<UnregisteredService>());
            Assert.Null(runtime.Host.Services.GetOptionalService<SignalService>());
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
    /// 同键第二个默认注册即装配失败（两个能力各注册同类型默认实现，错误信息带服务类型键）。
    /// </summary>
    [Fact]
    public void Compose_SecondDefaultRegistrationOfSameKey_FailsFast()
    {
        try
        {
            MechanismScenario.First.Register = context => context.AddService(new SignalService { Marker = "first", });
            MechanismScenario.Second.Register = context => context.AddService(new SignalService { Marker = "second", });

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("mech-first", "mech-second")));

            Assert.Contains(typeof(SignalService).FullName, exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 宿主覆盖恒胜出模块默认（替代旧组合根预置服务形态）：宿主侧与依赖它的消费者都解析到覆盖实例；
    /// 被覆盖的模块默认仍留在审计描述符表中。
    /// </summary>
    [Fact]
    public async Task Compose_Override_WinsOverModuleDefault()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var replacement = new SignalService { Marker = "replaced", };
            SignalService resolvedByConsumer = null;
            MechanismScenario.First.Register = context => context.AddService(new SignalService { Marker = "module-default", });
            MechanismScenario.Second.Requires = new[] { "mech-first" };
            MechanismScenario.Second.Register = context => resolvedByConsumer = context.GetService<SignalService>();
            var options = CreateOptions("mech-first", "mech-second");
            options.Override(replacement);

            runtime = Compose(options);

            Assert.Same(replacement, runtime.Host.Services.GetService<SignalService>());
            Assert.Same(replacement, resolvedByConsumer);
            var registrations = runtime.Host.Services.Registrations.Where(registration => registration.ServiceType == typeof(SignalService)).ToList();
            Assert.Equal(2, registrations.Count);
            Assert.Contains(registrations, registration => registration.IsOverride && registration.OwnerCapabilityId == "mechanism-tests");
            Assert.Contains(registrations, registration => !registration.IsOverride && registration.OwnerCapabilityId == "mech-first");
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
    /// 同键第二个宿主覆盖即装配失败（覆盖面与模块注册面共用同一描述符表与口径）。
    /// </summary>
    [Fact]
    public void Compose_SecondOverrideOfSameKey_FailsFast()
    {
        try
        {
            var options = CreateOptions("mech-first");
            options.Override(new SignalService { Marker = "one", });
            options.Override(new SignalService { Marker = "two", });

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains(typeof(SignalService).FullName, exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 命名注册：宿主侧 GetService&lt;T&gt;(name) 按命名键寻址；消费者 Activate 期经解析器按名取用；
    /// 只有命名注册时默认键仍为缺失。
    /// </summary>
    [Fact]
    public async Task Compose_NamedRegistrations_ResolveByName()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var alpha = new SignalService { Marker = "alpha", };
            var beta = new SignalService { Marker = "beta", };
            SignalService betaResolvedInActivate = null;
            MechanismScenario.First.Register = context =>
            {
                context.AddService("alpha", alpha);
                context.AddService("beta", beta);
            };
            MechanismScenario.Second.Requires = new[] { "mech-first" };
            MechanismScenario.Second.Activate = (context, cancellationToken) =>
            {
                betaResolvedInActivate = context.Services.GetService<SignalService>("beta");
                return Task.CompletedTask;
            };

            runtime = Compose(CreateOptions("mech-first", "mech-second"));

            Assert.Same(alpha, runtime.Host.Services.GetService<SignalService>("alpha"));
            Assert.Same(beta, runtime.Host.Services.GetService<SignalService>("beta"));
            Assert.Throws<InvalidOperationException>(() => runtime.Host.Services.GetService<SignalService>());
            Assert.Null(runtime.Host.Services.GetOptionalService<SignalService>());

            await runtime.StartAsync();

            Assert.Same(beta, betaResolvedInActivate);
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
    /// 多实现枚举：Register 期与宿主启动前 GetServices 恒抛出；Activate 期与启动后按注册序列出默认 + 命名实现。
    /// </summary>
    [Fact]
    public async Task Compose_GetServices_EnumeratesDefaultAndNamedOnlyFromActivation()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var defaultInstance = new SignalService { Marker = "default", };
            var alphaInstance = new SignalService { Marker = "alpha", };
            Exception registerEnumerationError = null;
            IReadOnlyList<SignalService> enumeratedInActivate = null;
            MechanismScenario.First.Register = context =>
            {
                context.AddService(defaultInstance);
                context.AddService("alpha", alphaInstance);
                registerEnumerationError = Record.Exception(() => context.GetServices<SignalService>());
            };
            MechanismScenario.Second.Requires = new[] { "mech-first" };
            MechanismScenario.Second.Activate = (context, cancellationToken) =>
            {
                enumeratedInActivate = context.Services.GetServices<SignalService>();
                return Task.CompletedTask;
            };

            runtime = Compose(CreateOptions("mech-first", "mech-second"));

            Assert.IsType<InvalidOperationException>(registerEnumerationError);
            Assert.Throws<InvalidOperationException>(() => runtime.Host.Services.GetServices<SignalService>());

            await runtime.StartAsync();

            Assert.Equal(new[] { defaultInstance, alphaInstance }, enumeratedInActivate);
            Assert.Equal(new[] { defaultInstance, alphaInstance }, runtime.Host.Services.GetServices<SignalService>());
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
    /// 懒工厂：组合完成时尚未构造；首次解析才执行一次，之后重复解析命中同一实例（宿主内单例）。
    /// </summary>
    [Fact]
    public async Task Compose_LazyFactory_RunsOnceOnFirstResolve()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var factoryRuns = 0;
            MechanismScenario.First.Register = context => context.AddService<SignalService>(resolver =>
            {
                factoryRuns++;
                return new SignalService { Marker = "lazy", };
            });

            runtime = Compose(CreateOptions("mech-first"));

            Assert.Equal(0, factoryRuns);

            var first = runtime.Host.Services.GetService<SignalService>();
            var second = runtime.Host.Services.GetService<SignalService>();

            Assert.Equal(1, factoryRuns);
            Assert.Same(first, second);
            Assert.Equal("lazy", first.Marker);
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
    /// 宿主 Modules 面：只暴露启用模块的拓扑序实例（与 Register 共用同一槽位模块实例）；被过滤槽位缺席且报告标注未收录。
    /// </summary>
    [Fact]
    public async Task Compose_HostModules_ExposesEnabledInstancesOnly()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.Second.Requires = new[] { "mech-first" };

            runtime = Compose(CreateOptions("mech-first", "mech-second"));

            Assert.Equal(new[] { "mech-first", "mech-second" }, runtime.Host.Modules.Select(module => module.Manifest.Id).ToArray());
            Assert.IsType<MechanismFirstSlotModule>(runtime.Host.Modules[0]);
            Assert.IsType<MechanismSecondSlotModule>(runtime.Host.Modules[1]);
            Assert.DoesNotContain(runtime.Host.Modules, module => module is MechanismThirdSlotModule || module is MechanismFourthSlotModule);
            Assert.Equal(new[] { "mech-first", "mech-second" }, runtime.Host.Report.EnabledIdsInOrder);
            Assert.Equal(CapabilityFilterState.NotInEnableList, runtime.Report.Entries.Single(entry => entry.Manifest.Id == "mech-third").State);
            Assert.Equal(CapabilityFilterState.NotInEnableList, runtime.Report.Entries.Single(entry => entry.Manifest.Id == "mech-fourth").State);
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
    /// 懒工厂循环依赖（A 的工厂解析 B、B 的工厂解析 A）：首次解析即抛 InvalidOperationException 并列出完整解析链
    /// （A -> B -> A），不再无限递归导致不可捕获的栈溢出。
    /// </summary>
    [Fact]
    public void Compose_LazyFactoryCycle_FailsFastWithResolutionChain()
    {
        try
        {
            MechanismScenario.First.Register = context =>
            {
                context.AddService<CycleAlphaService>(resolver =>
                {
                    resolver.GetService<CycleBetaService>();
                    return new CycleAlphaService();
                });
                context.AddService<CycleBetaService>(resolver =>
                {
                    resolver.GetService<CycleAlphaService>();
                    return new CycleBetaService();
                });
                context.GetService<CycleAlphaService>();
            };

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("mech-first")));

            var chain = typeof(CycleAlphaService).FullName + " -> " + typeof(CycleBetaService).FullName + " -> " + typeof(CycleAlphaService).FullName;
            Assert.Equal(string.Format(GameFrameX.Foundation.Localization.Core.LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.ServiceResolutionCycle), chain), exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 模块作用域可选解析的合法缺席：消费者声明的可选依赖未装配时，Register 期与 Activate 期 GetOptionalService 未命中均返回 null。
    /// </summary>
    [Fact]
    public async Task GetOptionalService_DeclaredOptionalDependencyAbsent_ReturnsNull()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var registerResolved = new SignalService { Marker = "sentinel", };
            var activateResolved = new SignalService { Marker = "sentinel", };
            MechanismScenario.First.Optional = new[] { new CapabilityOptionalDependency("mech-second", GameFrameX.Localization.Keys.Capability.OnlineModule.Match.OptionalAssetsAbsent), };
            MechanismScenario.First.Register = context => registerResolved = context.GetOptionalService<SignalService>();
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                activateResolved = context.Services.GetOptionalService<SignalService>();
                return Task.CompletedTask;
            };

            runtime = Compose(CreateOptions("mech-first"));
            await runtime.StartAsync();

            Assert.Null(registerResolved);
            Assert.Null(activateResolved);
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
    /// 模块作用域可选解析的 fail-fast（设计 §4.9.1）：声明的可选依赖已装配却没注册该服务 → Register 期抛 InvalidOperationException；
    /// 消费者未声明任何可选依赖时 Activate 期未命中同样抛出（能力在场但服务未注册，不得静默返回 null）。
    /// </summary>
    [Fact]
    public async Task GetOptionalService_NoAbsentOptionalDependency_FailsFast()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Optional = new[] { new CapabilityOptionalDependency("mech-second", GameFrameX.Localization.Keys.Capability.OnlineModule.Match.OptionalAssetsAbsent), };
            MechanismScenario.First.Register = context => context.GetOptionalService<SignalService>();

            var registerError = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("mech-first", "mech-second")));

            Assert.Equal(string.Format(GameFrameX.Foundation.Localization.Core.LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.OptionalServiceNotRegistered), "mech-first", typeof(SignalService).FullName), registerError.Message);

            MechanismScenario.Reset();
            MechanismScenario.First.Activate = (context, cancellationToken) =>
            {
                context.Services.GetOptionalService<SignalService>();
                return Task.CompletedTask;
            };

            runtime = Compose(CreateOptions("mech-first"));
            var activateError = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());

            Assert.Equal(string.Format(GameFrameX.Foundation.Localization.Core.LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.OptionalServiceNotRegistered), "mech-first", typeof(SignalService).FullName), activateError.Message);
            Assert.Null(runtime.Host.Services.GetOptionalService<SignalService>());
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
