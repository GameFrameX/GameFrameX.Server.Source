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
//   please refer to the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations.
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

using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using GameFrameX.Utility.Runtime;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Launcher.StartUp;
using GameFrameX.Tests.Online;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.Composition;

/// <summary>
/// 验证启动模板直排序列：业务库 → 组件注册 → 热更装载 → 能力组合 → 监听开放 → ServiceOnline → 启动就绪屏障；
/// 停机严格逆序（网络停机 → ServiceOffline → 能力逆序停 → 热更回存关库），任一步启动失败时中途逆序清理且各清理步骤恰好执行一次。
/// </summary>
[Collection(GameAppRuntimeCollection.Name)]
public class GameServerStartupSequenceTests
{
    private const string RealFamilyCompositionStep = "capability-compose";

    /// <summary>
    /// 以可覆写 hook 的测试子类记录模板调用序（全部 hook 记录后不触发真实副作用；
    /// 能力组合 hook 可选装载机制测试族的真实运行时，用于断言能力启停在序列中的位置）。
    /// </summary>
    private sealed class SequenceRecordingStartup : AppStartUpGameServerBase
    {
        public SequenceRecordingStartup(bool composeRealCapabilityFamily)
        {
            Setting = new AppSetting();
            ComposeRealCapabilityFamily = composeRealCapabilityFamily;
        }

        /// <summary>调用序列记录器。</summary>
        public List<string> Calls { get; } = new List<string>();

        /// <summary>注入失败的步骤名（记录该步后抛出；null = 不失败）。</summary>
        public string FailureStep { get; set; }

        /// <summary>能力组合 hook 是否装载机制测试族真实运行时（false = 记录后返回 null）。</summary>
        public bool ComposeRealCapabilityFamily { get; }
        /// <summary>能力组合 hook 是否改走模板缺省真实组合根（admin-only 形态；验证 admin 口开放时点）。</summary>
        public bool ComposeRealAdminOnlyRuntime { get; set; }

        /// <summary>是否在业务库步骤后阻塞等待 <see cref="StartupGate"/>（启动中途并发停机竞态测试）。</summary>
        public bool PauseAfterBusinessDatabase { get; set; }

        /// <summary>启动序阻塞闸门（<see cref="PauseAfterBusinessDatabase"/> 置位时在业务库步骤后等待释放）。</summary>
        public TaskCompletionSource StartupGate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>直调受保护 Init（生产路径经宿主入口触发；回归测试用，验证屏障在初始化阶段登记）。</summary>
        public void InvokeInitOnly()
        {
            Init();
        }

        protected override AppSetting CreateDefaultSetting()
        {
            return new AppSetting();
        }

        protected override async Task InitializeBusinessDatabaseAsync()
        {
            await RecordAsync("business-database");
            if (PauseAfterBusinessDatabase)
            {
                await StartupGate.Task;
            }
        }

        protected override Task RegisterComponentsAsync()
        {
            return RecordAsync("components-register");
        }

        protected override Task LoadHotfixModuleAsync()
        {
            return RecordAsync("hotfix-load");
        }

        protected override async Task<CapabilityRuntime> ComposeCapabilityRuntimeAsync()
        {
            Calls.Add(RealFamilyCompositionStep);
            if (ShouldFail(RealFamilyCompositionStep))
            {
                throw new InvalidOperationException(FailureMessage(RealFamilyCompositionStep));
            }

            if (ComposeRealAdminOnlyRuntime)
            {
                // 模板缺省真实组合根（admin-only 形态）：本调用返回即代表 admin 口（真实 Kestrel 端口）已开放。
                return await base.ComposeCapabilityRuntimeAsync();
            }
            if (!ComposeRealCapabilityFamily)
            {
                return null;
            }

            var options = new CapabilityCompositionOptions(JsonSerializer.SerializeToElement(new Dictionary<string, object>()), new CollectingCapabilityAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions { ServerId = 1, },
            };
            var runtime = CapabilityComposition.Compose(new MechanismTestFamily(), options);
            var modules = Assert.IsAssignableFrom<IList<ICapabilityModule>>(runtime.Host.Modules);
            for (var index = 0; index < modules.Count; index++)
            {
                if (modules[index].Manifest.Id.StartsWith("mech-", StringComparison.Ordinal))
                {
                    modules[index] = new RecordingCapabilityModule(modules[index], Calls);
                }
            }

            await runtime.StartAsync();
            return runtime;
        }

        protected override Task StartNetworkListenersAsync()
        {
            return RecordAsync("listen");
        }

        protected override Task OnStartupCompletedAsync()
        {
            return RecordAsync("service-online");
        }

        protected override Task StopNetworkAndMarkStoppedAsync(string message)
        {
            return RecordAsync("network-stop:" + message);
        }

        protected override Task OnStoppingAsync(string message)
        {
            return RecordAsync("service-offline:" + message);
        }

        protected override Task StopRuntimeCoreAsync()
        {
            return RecordAsync("runtime-stop");
        }

        private bool ShouldFail(string step)
        {
            return string.Equals(FailureStep, step, StringComparison.Ordinal);
        }

        private Task RecordAsync(string step)
        {
            Calls.Add(step);
            if (ShouldFail(step))
            {
                throw new InvalidOperationException(FailureMessage(step));
            }

            return Task.CompletedTask;
        }

        private static string FailureMessage(string step)
        {
            return "Injected startup failure: " + step;
        }
    }

    /// <summary>
    /// 记录激活 / 停用的能力模块包装（机制测试族槽位；激活后记录、停用后记录——能力在模板序列中位置的观测点）。
    /// </summary>
    private sealed class RecordingCapabilityModule : ICapabilityModule
    {
        private readonly ICapabilityModule _inner;
        private readonly List<string> _calls;

        public RecordingCapabilityModule(ICapabilityModule inner, List<string> calls)
        {
            _inner = inner;
            _calls = calls;
        }

        public CapabilityManifest Manifest
        {
            get { return _inner.Manifest; }
        }

        public void Register(CapabilityRegistrationContext context)
        {
            _inner.Register(context);
        }

        public async Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
        {
            await _inner.ActivateAsync(context, cancellationToken);
            _calls.Add(Manifest.Id + "-activate");
        }

        public async Task DeactivateAsync(CancellationToken cancellationToken)
        {
            await _inner.DeactivateAsync(cancellationToken);
            _calls.Add(Manifest.Id + "-stop");
        }
    }

    /// <summary>
    /// 完整启动序（含能力运行时）：业务库 → 组件 → 热更 → 能力组合与激活 → 监听 → ServiceOnline → 就绪屏障；
    /// 停机严格逆序：网络停机 → ServiceOffline → 能力逆序停 → 热更回存关库；重复停机幂等零新增调用。
    /// </summary>
    [Fact]
    public async Task StartupSequence_ComposesCapabilitiesBetweenHotfixAndListeners_StopsInReverse()
    {
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
        var startup = new SequenceRecordingStartup(composeRealCapabilityFamily: true);
        var expectedStart = new[]
        {
            "business-database",
            "components-register",
            "hotfix-load",
            "capability-compose",
            "mech-first-activate",
            "mech-fourth-activate",
            "mech-second-activate",
            "mech-third-activate",
            "listen",
            "service-online",
        };

        var startTask = startup.StartAsync();
        Assert.True(await WaitUntilReadyAsync(startup), "启动就绪屏障在超时内未置位");

        // 就绪屏障晚于 ServiceOnline（此刻序列停在 ServiceOnline，屏障才刚置位）。
        Assert.Equal(expectedStart, startup.Calls);

        GameAppRuntime.MarkStopped("unit-test-stop");
        await startTask;

        var expectedStop = new[]
        {
            "network-stop:unit-test-stop",
            "service-offline:unit-test-stop",
            "mech-third-stop",
            "mech-second-stop",
            "mech-fourth-stop",
            "mech-first-stop",
            "runtime-stop",
        };
        Assert.Equal(expectedStart.Concat(expectedStop), startup.Calls);

        // 停机幂等：缓存停止任务直接返回，不再产生任何调用。
        await startup.StopAsync("repeat-stop");
        Assert.Equal(expectedStart.Concat(expectedStop), startup.Calls);
    }

    /// <summary>
    /// 双闸全关形态（无能力运行时）：真实组合根返回 null，模板序列中热更装载直接衔接监听开放；停机仍为固定逆序。
    /// </summary>
    [Fact]
    public async Task StartupSequence_WithoutCapabilityRuntime_RunsFixedStepsDirectly()
    {
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
        var startup = new SequenceRecordingStartup(composeRealCapabilityFamily: false);
        var startTask = startup.StartAsync();
        Assert.True(await WaitUntilReadyAsync(startup), "启动就绪屏障在超时内未置位");

        Assert.Equal(new[]
        {
            "business-database",
            "components-register",
            "hotfix-load",
            "capability-compose",
            "listen",
            "service-online",
        }, startup.Calls);

        GameAppRuntime.MarkStopped("unit-test-stop");
        await startTask;

        Assert.Equal(new[]
        {
            "business-database",
            "components-register",
            "hotfix-load",
            "capability-compose",
            "listen",
            "service-online",
            "network-stop:unit-test-stop",
            "service-offline:unit-test-stop",
            "runtime-stop",
        }, startup.Calls);
    }

    /// <summary>
    /// 启动中途失败：就绪屏障不置位，已启动部分逆序清理（网络停机 → ServiceOffline → 能力逆序停 → 热更清理），
    /// 各清理步骤恰好执行一次；失败步骤之后的主序步骤不再执行。
    /// </summary>
    [Theory]
    [InlineData("hotfix-load")]
    [InlineData("capability-compose")]
    [InlineData("listen")]
    [InlineData("service-online")]
    public async Task StartupFailure_CleansStartedPartsInReverseExactlyOnce(string failureStep)
    {
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
        var startup = new SequenceRecordingStartup(composeRealCapabilityFamily: true) { FailureStep = failureStep, };

        await Assert.ThrowsAsync<InvalidOperationException>(() => startup.StartAsync());

        Assert.False(startup.StartUpReadyTask.IsCompleted, "启动失败不得置位就绪屏障");
        Assert.Equal(1, startup.Calls.Count(call => call == "network-stop:"));
        Assert.Equal(1, startup.Calls.Count(call => call == "service-offline:"));
        Assert.Equal(1, startup.Calls.Count(call => call == "runtime-stop"));
        Assert.True(startup.Calls.IndexOf("network-stop:") < startup.Calls.IndexOf("service-offline:"), "网络停机必须先于 ServiceOffline");
        Assert.True(startup.Calls.IndexOf("service-offline:") < startup.Calls.IndexOf("runtime-stop"), "ServiceOffline 必须先于热更清理");

        var failureIndex = startup.Calls.IndexOf(failureStep);
        var composeIndex = startup.Calls.IndexOf(RealFamilyCompositionStep);
        var capabilityActivated = composeIndex >= 0 && failureIndex > composeIndex;
        if (capabilityActivated)
        {
            // 能力已随组合激活：清理必须逆序停能力，且先于热更清理。
            var firstCapabilityStop = startup.Calls.FindIndex(call => call.EndsWith("-stop", StringComparison.Ordinal) && call.StartsWith("mech-", StringComparison.Ordinal));
            Assert.True(firstCapabilityStop > 0, "能力已激活时清理必须停止能力");
            Assert.True(startup.Calls.IndexOf("service-offline:") < firstCapabilityStop, "ServiceOffline 必须先于能力停止");
            Assert.True(firstCapabilityStop < startup.Calls.IndexOf("runtime-stop"), "能力停止必须先于热更清理");
        }
        else
        {
            Assert.DoesNotContain(startup.Calls, call => call.EndsWith("-activate", StringComparison.Ordinal) && call.StartsWith("mech-", StringComparison.Ordinal));
        }

        // 失败步骤之后的主序步骤不再执行。
        var listenIndex = startup.Calls.IndexOf("listen");
        if (failureStep == "hotfix-load" || failureStep == RealFamilyCompositionStep)
        {
            Assert.Equal(-1, listenIndex);
        }

        // 并发重复停机幂等：仍只清理一次。
        await Task.WhenAll(startup.StopAsync("concurrent-stop"), startup.StopAsync("concurrent-stop"));
        Assert.Equal(1, startup.Calls.Count(call => call == "runtime-stop"));
    }

    /// <summary>
    /// 启动中途并发停机：停机序必须以整条启动序列为屏障——序列收尾前停机任务不得提前完成、
    /// 不得出现「对空状态完成停机后启动序列继续跑完」的残留窗口；序列收尾后一次性逆序清理全部已启动部分。
    /// </summary>
    [Fact]
    public async Task ConcurrentStop_DuringStartup_WaitsForSequenceThen_TearsDownEverythingOnce()
    {
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
        var startup = new SequenceRecordingStartup(composeRealCapabilityFamily: true) { PauseAfterBusinessDatabase = true, };
        var startTask = startup.StartAsync();
        Assert.True(await WaitUntilAsync(() => startup.Calls.Contains("business-database"), TimeSpan.FromSeconds(10)), "启动序列未在超时内进入业务库步骤");

        // 模拟 SIGTERM/Ctrl+C 在启动早期窗口（业务库步骤，能力未组合、监听未开）并发进入 StopAsync。
        var stopTask = startup.StopAsync("concurrent-stop");
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.False(stopTask.IsCompleted, "启动序列未收尾前并发停机不得提前完成");
        Assert.DoesNotContain(startup.Calls, call => call.EndsWith("-stop", StringComparison.Ordinal));

        startup.StartupGate.SetResult();
        GameAppRuntime.MarkStopped("unit-test-stop");
        await startTask;
        await stopTask;

        // 停机等待启动序列完整跑完后统一逆序清理（首个停机消息胜出）：完整启动序 + 完整停机序。
        var expectedStart = new[]
        {
            "business-database",
            "components-register",
            "hotfix-load",
            "capability-compose",
            "mech-first-activate",
            "mech-fourth-activate",
            "mech-second-activate",
            "mech-third-activate",
            "listen",
            "service-online",
        };
        var expectedStop = new[]
        {
            "network-stop:concurrent-stop",
            "service-offline:concurrent-stop",
            "mech-third-stop",
            "mech-second-stop",
            "mech-fourth-stop",
            "mech-first-stop",
            "runtime-stop",
        };
        Assert.Equal(expectedStart.Concat(expectedStop), startup.Calls);

        // 停机幂等：缓存停止任务直接返回，不再产生任何调用。
        await startup.StopAsync("repeat-stop");
        Assert.Equal(expectedStart.Concat(expectedStop), startup.Calls);
    }

    /// <summary>
    /// admin-only 形态的 admin 口开放时点（有意语义位移，change §1 步骤 5 登记）：真实组合根在能力组合步
    /// 启动 admin-only 族运行时（真实 Kestrel 端口），早于游戏监听开放与 ServiceOnline；停机序摘取并停止该运行时。
    /// </summary>
    [Fact]
    public async Task AdminOnlyRuntime_OpensAdminPort_BeforeNetworkListeners()
    {
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
        var startup = new SequenceRecordingStartup(composeRealCapabilityFamily: false) { ComposeRealAdminOnlyRuntime = true, };
        startup.Setting.ServerId = 1001;
        startup.Setting.Online = new OnlineCapabilityOptions { EnableRuntime = false, EnableAdminApi = true, };
        startup.Setting.OnlineAdminPort = (ushort)FindFreeLoopbackPort();
        var startTask = startup.StartAsync();
        try
        {
            Assert.True(await WaitUntilReadyAsync(startup), "启动就绪屏障在超时内未置位");

            // 组合步返回即 admin 口已开放：必须早于游戏监听开放与 ServiceOnline 分发。
            var composeIndex = startup.Calls.IndexOf(RealFamilyCompositionStep);
            Assert.True(composeIndex >= 0, "能力组合步必须执行");
            Assert.True(composeIndex < startup.Calls.IndexOf("listen"), "admin 口开放必须早于游戏监听开放");
            Assert.True(composeIndex < startup.Calls.IndexOf("service-online"), "admin 口开放必须早于 ServiceOnline 分发");

            // 步骤序断言之外双锚真实端口连通：admin 口此刻必须可建立 TCP 连接（不只是调用序推断）。
            using (var adminClient = new TcpClient())
            {
                await adminClient.ConnectAsync(IPAddress.Loopback, startup.Setting.OnlineAdminPort);
                Assert.True(adminClient.Connected, "admin 口在运行期必须可建立 TCP 连接");
            }
        }
        finally
        {
            // 断言失败也必须回收：标记退出并等待宿主收尾，停机序经 DetachAdminOnlyFamilyRuntime 摘停 admin 运行时。
            GameAppRuntime.MarkStopped("unit-test-stop");
            await startTask;
        }

        Assert.Equal(new[]
        {
            "business-database",
            "components-register",
            "hotfix-load",
            "capability-compose",
            "listen",
            "service-online",
            "network-stop:unit-test-stop",
            "service-offline:unit-test-stop",
            "runtime-stop",
        }, startup.Calls);
    }

    /// <summary>
    /// 停机先于 StartAsync 到达（初始化后、启动调用前的空窗）：屏障任务在初始化阶段已登记，
    /// 停机必须等待启动序列收尾后统一逆序清理，不得对空状态提前完成、不得让启动序在停机后继续遗留已启动部分。
    /// </summary>
    [Fact]
    public async Task StopBeforeStartAsync_AfterInit_WaitsForSequenceThen_TearsDownOnce()
    {
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
        var startup = new SequenceRecordingStartup(composeRealCapabilityFamily: true) { PauseAfterBusinessDatabase = true, };

        // 初始化阶段登记屏障（生产路径：退出处理器武装之前；启动序列随之开始执行并停在业务库闸门）。
        startup.InvokeInitOnly();
        Assert.True(await WaitUntilAsync(() => startup.Calls.Contains("business-database"), TimeSpan.FromSeconds(10)), "启动序列未在超时内进入业务库步骤");

        // 停机先于 StartAsync 到达：不得对空状态提前完成（此时启动序已在途，须以屏障任务等待其收尾）。
        var stopTask = startup.StopAsync("stop-before-start");
        await Task.Delay(TimeSpan.FromMilliseconds(200));
        Assert.False(stopTask.IsCompleted, "启动序列未收尾前先到停机不得提前完成");
        Assert.DoesNotContain(startup.Calls, call => call.EndsWith("-stop", StringComparison.Ordinal));

        // 释放闸门让启动序收尾，停机随后统一逆序清理（首个停机消息胜出）；StartAsync 后到不产生二次清理。
        startup.StartupGate.SetResult();
        await stopTask;
        GameAppRuntime.MarkStopped("unit-test-stop");
        await startup.StartAsync();

        var expectedStart = new[]
        {
            "business-database",
            "components-register",
            "hotfix-load",
            "capability-compose",
            "mech-first-activate",
            "mech-fourth-activate",
            "mech-second-activate",
            "mech-third-activate",
            "listen",
            "service-online",
        };
        var expectedStop = new[]
        {
            "network-stop:stop-before-start",
            "service-offline:stop-before-start",
            "mech-third-stop",
            "mech-second-stop",
            "mech-fourth-stop",
            "mech-first-stop",
            "runtime-stop",
        };
        Assert.Equal(expectedStart.Concat(expectedStop), startup.Calls);
    }

    /// <summary>探测一个当前可绑定的回环空闲端口（探测后立即释放；admin 口测试用）。</summary>
    private static int FindFreeLoopbackPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        try
        {
            return ((IPEndPoint)listener.LocalEndpoint).Port;
        }
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>轮询启动就绪屏障（超时 false；屏障置位即启动主序完成）。</summary>
    private static async Task<bool> WaitUntilReadyAsync(SequenceRecordingStartup startup)
    {
        return await WaitUntilAsync(() => startup.StartUpReadyTask.IsCompleted, TimeSpan.FromSeconds(10));
    }

    /// <summary>轮询条件直到成立或超时。</summary>
    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(10);
        }

        return condition();
    }
}
