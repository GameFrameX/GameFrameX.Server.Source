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
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;
using Xunit;
using LocalizationKeys = GameFrameX.Localization.Keys;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 宿主扩展点与能力钩子测试（设计 §6.4：RequireHostService 缺失 fail-fast、宿主 Override 恒胜出模块默认、
/// 钩子声明 / 挂载 / 原子替换 / 摘下回默认，以及钩子声明随宿主停止、激活失败、组合失败释放的回归锚点）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public sealed class CapabilityHostServiceAndHookTests
{
    /// <summary>宿主扩展点契约（模块声明 RequireHostService，宿主 Override 提供）。</summary>
    public interface IHostDirectoryService
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>可被宿主覆盖的模块默认服务契约。</summary>
    public interface IOverridableClockService
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>挂载 / 替换 / 摘下用例的钩子契约。</summary>
    public interface IAttachSwapHook
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>停止后再组合用例的钩子契约。</summary>
    public interface IRecomposeAfterStopHook
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>组合失败释放用例的钩子契约。</summary>
    public interface IRecomposeAfterFailedComposeHook
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>激活失败释放用例的钩子契约。</summary>
    public interface IRecomposeAfterFailedStartHook
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>同组合内重复声明用例的钩子契约。</summary>
    public interface IDuplicateDeclarationHook
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>声明计数用例的钩子契约（交错释放不得抹掉仍存活的声明）。</summary>
    public interface IInterleavedDeclarationHook
    {
        /// <summary>实现标记。</summary>
        string Name { get; }
    }

    /// <summary>
    /// 全部测试契约的统一实现（按实现标记区分实例）。
    /// </summary>
    private sealed class NamedImplementation : IHostDirectoryService, IOverridableClockService, IAttachSwapHook, IRecomposeAfterStopHook, IRecomposeAfterFailedComposeHook, IRecomposeAfterFailedStartHook, IDuplicateDeclarationHook, IInterleavedDeclarationHook
    {
        /// <summary>
        /// 初始化实现。
        /// </summary>
        /// <param name="name">实现标记。</param>
        public NamedImplementation(string name)
        {
            Name = name;
        }

        /// <summary>
        /// 获取构造时传入的实现标记，供测试按标记断言宿主服务或钩子解析到的是哪个实例。
        /// </summary>
        /// <remarks>
        /// Gets the implementation marker passed at construction so tests can assert which instance a host service or hook resolved to.
        /// </remarks>
        /// <value>实现标记 / Implementation marker</value>
        public string Name { get; }
    }

    /// <summary>
    /// 构造机制组合选项（合法作用域三元组 + 白名单圈定参与面）。
    /// </summary>
    /// <param name="enabledIds">白名单。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateOptions(params string[] enabledIds)
    {
        return new CapabilityCompositionOptions(default, new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, },
            Filter = new CapabilityAssemblyFilter(enabledIds),
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
    /// 宿主扩展点缺失 fail-fast：模块 Register 声明 RequireHostService 而宿主未 Override，组合即失败（HostServiceMissing）。
    /// </summary>
    [Fact]
    public void Compose_RequireHostServiceWithoutOverride_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            MechanismScenario.First.Register = context => context.RequireHostService<IHostDirectoryService>();

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("mech-first")));

            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.HostServiceMissing), "mech-first", typeof(IHostDirectoryService).FullName), exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 宿主扩展点满足：宿主 Override 后组合成功，模块 Register 期可解析到宿主提供的实例（来源 = 族，不受来源检查拦截），
    /// 宿主侧服务面解析到同一实例。
    /// </summary>
    [Fact]
    public async Task Compose_RequireHostServiceWithOverride_ComposesAndResolvesHostInstance()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var hostDirectory = new NamedImplementation("host-directory");
            IHostDirectoryService resolvedInRegister = null;
            MechanismScenario.First.Register = context =>
            {
                context.RequireHostService<IHostDirectoryService>();
                resolvedInRegister = context.GetService<IHostDirectoryService>();
            };
            var options = CreateOptions("mech-first");
            options.Override<IHostDirectoryService>(hostDirectory);

            runtime = Compose(options);

            Assert.Same(hostDirectory, resolvedInRegister);
            Assert.Same(hostDirectory, runtime.Host.Services.GetService<IHostDirectoryService>());
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
    /// 宿主 Override 恒胜出模块默认（与注册先后无关）：模块 Register 注册默认实现、宿主 Override 同键，
    /// 模块侧、宿主侧单实例解析与 Activate 期枚举都只见覆盖实现（被覆盖的默认不列出）。
    /// </summary>
    [Fact]
    public async Task Compose_OverrideBeatsModuleDefault()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var moduleDefault = new NamedImplementation("module-default");
            var hostOverride = new NamedImplementation("host-override");
            IOverridableClockService resolvedInRegister = null;
            MechanismScenario.First.Register = context =>
            {
                context.AddService<IOverridableClockService>(moduleDefault);
                resolvedInRegister = context.GetService<IOverridableClockService>();
            };
            var options = CreateOptions("mech-first");
            options.Override<IOverridableClockService>(hostOverride);

            runtime = Compose(options);

            Assert.Same(hostOverride, resolvedInRegister);
            Assert.Same(hostOverride, runtime.Host.Services.GetService<IOverridableClockService>());

            await runtime.StartAsync();

            Assert.Same(hostOverride, Assert.Single(runtime.Host.Services.GetServices<IOverridableClockService>()));
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
    /// 同键第二个宿主 Override = 组合失败（ServiceOverrideDuplicate）。
    /// </summary>
    [Fact]
    public void Compose_DuplicateOverrideOfSameKey_FailsFast()
    {
        MechanismScenario.Reset();
        try
        {
            var options = CreateOptions("mech-first");
            options.Override<IOverridableClockService>(new NamedImplementation("first-override"));
            options.Override<IOverridableClockService>(new NamedImplementation("second-override"));

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.ServiceOverrideDuplicate), typeof(IOverridableClockService).FullName), exception.Message);
        }
        finally
        {
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 钩子声明 / 挂载 / 原子替换 / 摘下：组合后句柄读到默认策略；Attach(a) 后为 a；Attach(b) 原子替换为 b，
    /// 被替换的旧租约 Dispose 为空操作（b 仍在位）；摘下当前租约后回默认策略。
    /// </summary>
    [Fact]
    public async Task Hook_AttachReplaceAndDetach_FollowsLeaseSemantics()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        CapabilityHookLease<IAttachSwapHook> leaseA = null;
        CapabilityHookLease<IAttachSwapHook> leaseB = null;
        try
        {
            var defaultHook = new NamedImplementation("default");
            var hookA = new NamedImplementation("a");
            var hookB = new NamedImplementation("b");
            CapabilityHookHandle<IAttachSwapHook> handle = null;
            MechanismScenario.First.Register = context => handle = context.DeclareHook<IAttachSwapHook>(defaultHook);

            runtime = Compose(CreateOptions("mech-first"));

            Assert.NotNull(handle);
            Assert.Same(defaultHook, handle.Current);

            leaseA = CapabilityHook<IAttachSwapHook>.Attach(hookA);
            Assert.Same(hookA, handle.Current);

            leaseB = CapabilityHook<IAttachSwapHook>.Attach(hookB);
            Assert.Same(hookB, handle.Current);

            leaseA.Dispose();
            Assert.Same(hookB, handle.Current);

            leaseB.Dispose();
            Assert.Same(defaultHook, handle.Current);
        }
        finally
        {
            leaseA?.Dispose();
            leaseB?.Dispose();
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 同一组合内两个能力声明同一钩子 = 组合失败（HookDuplicateDeclaration，消息含先后两个声明者）；
    /// 失败组合已声明的项被释放，随后只留一个声明者再组合成功。
    /// </summary>
    [Fact]
    public async Task Compose_SameHookDeclaredTwice_FailsFastAndReleasesDeclaration()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var defaultHook = new NamedImplementation("default");
            MechanismScenario.First.Register = context => context.DeclareHook<IDuplicateDeclarationHook>(defaultHook);
            MechanismScenario.Second.Register = context => context.DeclareHook<IDuplicateDeclarationHook>(defaultHook);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("mech-first", "mech-second")));

            Assert.Equal(string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.HookDuplicateDeclaration), typeof(IDuplicateDeclarationHook).FullName, "mech-first", "mech-second"), exception.Message);

            CapabilityHookHandle<IDuplicateDeclarationHook> handle = null;
            MechanismScenario.Second.Register = null;
            MechanismScenario.First.Register = context => handle = context.DeclareHook<IDuplicateDeclarationHook>(defaultHook);

            runtime = Compose(CreateOptions("mech-first", "mech-second"));

            Assert.Same(defaultHook, handle.Current);
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
    /// 钩子声明释放回归（停止路径）：组合 → 启动 → 停止后同进程再次以同一 DeclareHook 组合成功；
    /// 已组合未启动直接停止同样释放声明。修复前第二次组合抛 HookDuplicateDeclaration。
    /// </summary>
    [Fact]
    public async Task Compose_AfterStopAsync_SameHookCanBeDeclaredAgain()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var defaultHook = new NamedImplementation("default");
            CapabilityHookHandle<IRecomposeAfterStopHook> handle = null;
            MechanismScenario.First.Register = context => handle = context.DeclareHook<IRecomposeAfterStopHook>(defaultHook);

            runtime = Compose(CreateOptions("mech-first"));
            await runtime.StartAsync();
            await runtime.StopAsync();
            runtime = null;

            handle = null;
            runtime = Compose(CreateOptions("mech-first"));
            Assert.Same(defaultHook, handle.Current);

            // 已组合未启动：停止仍须释放 Register 期声明。
            await runtime.StopAsync();
            runtime = null;

            handle = null;
            runtime = Compose(CreateOptions("mech-first"));
            Assert.Same(defaultHook, handle.Current);
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
    /// 钩子声明释放回归（组合失败路径）：前序模块 Register 已声明钩子、后序模块 Register 抛出，
    /// 组合失败后声明被释放，再次组合成功并读到默认策略。
    /// </summary>
    [Fact]
    public async Task Compose_AfterFailedComposition_SameHookCanBeDeclaredAgain()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var defaultHook = new NamedImplementation("default");
            CapabilityHookHandle<IRecomposeAfterFailedComposeHook> handle = null;
            var secondRegisterInvoked = false;
            MechanismScenario.First.Register = context => handle = context.DeclareHook<IRecomposeAfterFailedComposeHook>(defaultHook);
            MechanismScenario.Second.Register = context =>
            {
                secondRegisterInvoked = true;
                throw new InvalidOperationException("mech-second register failed");
            };

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("mech-first", "mech-second")));

            Assert.Equal("mech-second register failed", exception.Message);
            Assert.True(secondRegisterInvoked);
            Assert.NotNull(handle);

            handle = null;
            MechanismScenario.Second.Register = null;
            runtime = Compose(CreateOptions("mech-first", "mech-second"));

            Assert.NotNull(handle);
            Assert.Same(defaultHook, handle.Current);
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
    /// 钩子声明释放回归（激活失败路径）：模块 Register 声明钩子、Activate 抛出，StartAsync 回滚后声明被释放，
    /// 再次组合成功并读到默认策略。
    /// </summary>
    [Fact]
    public async Task Compose_AfterFailedStart_SameHookCanBeDeclaredAgain()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        try
        {
            var defaultHook = new NamedImplementation("default");
            CapabilityHookHandle<IRecomposeAfterFailedStartHook> handle = null;
            MechanismScenario.First.Register = context => handle = context.DeclareHook<IRecomposeAfterFailedStartHook>(defaultHook);
            MechanismScenario.First.Activate = (context, cancellationToken) => throw new InvalidOperationException("mech-first activation failed");

            runtime = Compose(CreateOptions("mech-first"));
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => runtime.StartAsync());
            Assert.Equal("mech-first activation failed", exception.Message);
            runtime = null;

            handle = null;
            MechanismScenario.First.Activate = null;
            runtime = Compose(CreateOptions("mech-first"));

            Assert.NotNull(handle);
            Assert.Same(defaultHook, handle.Current);
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
    /// 钩子声明按契约计数（交错释放回归）：旧宿主释放声明前，另一宿主对同契约的新声明已登记（以机制内部登记入口模拟
    /// 该交错到达点）；旧宿主停止后，新声明仍在册——随后组合时不得对已挂载契约误报「已挂载但无能力声明」Info；
    /// 新声明也释放后，同一审计如实报出。
    /// </summary>
    [Fact]
    public async Task HookDeclaration_InterleavedRelease_DoesNotEraseLiveDeclaration()
    {
        MechanismScenario.Reset();
        CapabilityRuntime runtime = null;
        CapabilityHookLease<IInterleavedDeclarationHook> lease = null;
        // 登记表是机制内部类型（测试程序集不在 InternalsVisibleTo 名单），经程序集按名取用以模拟交错到达的另一宿主声明。
        var slotsType = typeof(CapabilityHook<>).Assembly.GetType("GameFrameX.Capability.Hosting.Abstractions.CapabilityHookSlots", true);
        var markDeclared = slotsType.GetMethod("MarkDeclared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var markUndeclared = slotsType.GetMethod("MarkUndeclared", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var liveDeclarationRegistered = false;
        try
        {
            var hookNotDeclared = string.Format(LocalizationService.GetString(LocalizationKeys.Capability.Hosting.Assembly.AssemblyHookNotDeclared), typeof(IInterleavedDeclarationHook).FullName);
            lease = CapabilityHook<IInterleavedDeclarationHook>.Attach(new NamedImplementation("attached"));
            MechanismScenario.First.Register = context => context.DeclareHook<IInterleavedDeclarationHook>(new NamedImplementation("default"));

            runtime = Compose(CreateOptions("mech-first"));
            markDeclared.Invoke(null, new object[] { typeof(IInterleavedDeclarationHook), });
            liveDeclarationRegistered = true;
            await runtime.StopAsync();
            runtime = null;

            MechanismScenario.First.Register = null;
            var whileLiveLog = new CollectingCapabilityAssemblyLog();
            runtime = CapabilityComposition.Compose(new MechanismTestFamily(), CreateOptionsWithLog(whileLiveLog, "mech-first"));
            await runtime.StopAsync();
            runtime = null;

            Assert.DoesNotContain(hookNotDeclared, whileLiveLog.Infos);

            markUndeclared.Invoke(null, new object[] { typeof(IInterleavedDeclarationHook), });
            liveDeclarationRegistered = false;
            var afterReleaseLog = new CollectingCapabilityAssemblyLog();
            runtime = CapabilityComposition.Compose(new MechanismTestFamily(), CreateOptionsWithLog(afterReleaseLog, "mech-first"));

            Assert.Contains(hookNotDeclared, afterReleaseLog.Infos);
        }
        finally
        {
            if (liveDeclarationRegistered)
            {
                markUndeclared.Invoke(null, new object[] { typeof(IInterleavedDeclarationHook), });
            }

            lease?.Dispose();
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 构造带指定装配日志收集器的机制组合选项。
    /// </summary>
    /// <param name="log">装配日志收集器。</param>
    /// <param name="enabledIds">白名单。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateOptionsWithLog(CollectingCapabilityAssemblyLog log, params string[] enabledIds)
    {
        return new CapabilityCompositionOptions(default, log)
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1, },
            Filter = new CapabilityAssemblyFilter(enabledIds),
        };
    }
}
