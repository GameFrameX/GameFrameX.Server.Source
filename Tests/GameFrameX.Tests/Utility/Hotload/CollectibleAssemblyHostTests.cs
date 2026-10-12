// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规之开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights or interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Utility.Hotload;
using Xunit;
using static GameFrameX.Tests.Utility.GameAppRuntimeCollection;

namespace GameFrameX.Tests.Utility.Hotload;

/// <summary>
/// <see cref="CollectibleAssemblyHost{TModule}"/> 与 <see cref="CollectibleAssemblyLoadContext"/> 的行为测试。
/// Behavioral tests for <see cref="CollectibleAssemblyHost{TModule}"/> and <see cref="CollectibleAssemblyLoadContext"/>.
/// </summary>
/// <remarks>
/// <see cref="CollectibleAssemblyHost{TModule}.Retire"/> 内部读取 <see cref="GameFrameX.Utility.Setting.GlobalSettings"/>，
/// 按约定挂 <see cref="GameAppRuntimeCollection"/>（进程级静态串行集合）。
/// <see cref="CollectibleAssemblyHost{TModule}.Retire"/> reads <see cref="GameFrameX.Utility.Setting.GlobalSettings"/>
/// internally, so these tests join the <see cref="GameAppRuntimeCollection"/> (the process-level serialized collection) by convention.
/// </remarks>
[Collection(Name)]
public class CollectibleAssemblyHostTests
{
    /// <summary>
    /// 等待条件成立的有界轮询（lesson「test-port-assertion-identity-mismatch」纪律：不无限等待，超时后由断言给结论）。
    /// Bounded polling for a condition (per the "test-port-assertion-identity-mismatch" lesson: never wait
    /// unbounded — the assertion after timeout carries the verdict).
    /// </summary>
    private static async Task<bool> WaitAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(20);
        }

        return condition();
    }

    /// <summary>
    /// 测试替身模块：记录卸载次数并返回预设弱引用。
    /// Test-double module: records unload calls and returns a preset weak reference.
    /// </summary>
    private sealed class FakeModule : ICollectibleAssemblyModule
    {
        public Assembly LoadedAssembly
        {
            get;
        }

        public int UnloadCallCount
        {
            get;
            private set;
        }

        public WeakReference UnloadResult
        {
            get;
            set;
        }

        public FakeModule(Assembly loadedAssembly)
        {
            LoadedAssembly = loadedAssembly;
        }

        public WeakReference Unload()
        {
            UnloadCallCount++;
            return UnloadResult;
        }
    }

    /// <summary>
    /// Register 应立即切换当前模块且不产生过渡态。
    /// Register should swap the current module immediately without entering transition.
    /// </summary>
    [Fact]
    public void Register_SetsCurrentWithoutTransition()
    {
        var host = new CollectibleAssemblyHost<FakeModule>();
        Assert.False(host.InTransition);
        Assert.Null(host.CurrentModule);

        var module = new FakeModule(typeof(CollectibleAssemblyHostTests).Assembly);
        host.Register(module);

        Assert.Same(module, host.CurrentModule);
        Assert.False(host.InTransition);
    }

    /// <summary>
    /// Retire 应同步进入过渡态；宽限到期后卸载模块并仅在链排空后清过渡态。
    /// Retire should enter transition synchronously; after grace the module unloads and the flag
    /// clears only once the chain has drained.
    /// </summary>
    [Fact]
    public async Task Retire_InTransitionUntilChainDrained()
    {
        var host = new CollectibleAssemblyHost<FakeModule>();
        var module = new FakeModule(typeof(CollectibleAssemblyHostTests).Assembly)
        {
            UnloadResult = new WeakReference(new object()),
        };
        host.Register(module);

        host.Retire(module, TimeSpan.FromMilliseconds(50));

        // 同步置位：Retire 返回即处于过渡期
        Assert.True(host.InTransition);

        // 宽限到期后卸载且链排空 → 过渡态清零（有界等待，超时由断言裁决）
        var drained = await WaitAsync(() => !host.InTransition && module.UnloadCallCount == 1, TimeSpan.FromSeconds(5));
        Assert.True(drained, "退役链应在宽限到期后排空并卸载一次 / the retirement chain should drain and unload exactly once after grace");
        Assert.False(host.InTransition);
        Assert.Equal(1, module.UnloadCallCount);
    }

    /// <summary>
    /// TryGetOwner 应按程序集身份命中退役模块；未退役程序集与 null 均不命中。
    /// TryGetOwner should match a retired module by assembly identity; non-retired assemblies and null never match.
    /// </summary>
    [Fact]
    public void TryGetOwner_MatchesRetiredModuleByAssemblyIdentity()
    {
        var host = new CollectibleAssemblyHost<FakeModule>();
        var module = new FakeModule(typeof(CollectibleAssemblyHostTests).Assembly);
        host.Register(module);
        host.Retire(module, TimeSpan.FromMinutes(5)); // 长宽限：保持退役态供路由断言

        Assert.True(host.TryGetOwner(module.LoadedAssembly, out var owner));
        Assert.Same(module, owner);

        Assert.False(host.TryGetOwner(typeof(CollectibleAssemblyLoadContext).Assembly, out _));
        Assert.False(host.TryGetOwner(null, out _));
    }

    /// <summary>
    /// Retire 对 null 模块应抛 ArgumentNullException。
    /// Retire should throw ArgumentNullException for a null module.
    /// </summary>
    [Fact]
    public void Retire_NullModule_Throws()
    {
        var host = new CollectibleAssemblyHost<FakeModule>();
        Assert.Throws<ArgumentNullException>(() => host.Retire(null, TimeSpan.Zero));
    }

    /// <summary>
    /// 可收集上下文卸载后，弱引用应经强制 GC 后失活（真正释放）。
    /// After unloading the collectible context, its weak reference should die under forced collections
    /// (real release).
    /// </summary>
    [Fact]
    public async Task CollectibleAssemblyLoadContext_Unload_ReleasesContext()
    {
        var weakReference = LoadUnloadAndReleaseReferences(out var isDistinctCopy);

        // 同一文件加载进独立可收集 ALC：类型同构但实例不同
        Assert.True(isDistinctCopy);

        var released = await WaitAsync(() =>
        {
#pragma warning disable S1215 // GC.Collect 为可回收 ALC 释放判定的官方推荐模式（测试内有意保留）
            GC.Collect();
            GC.WaitForPendingFinalizers();
#pragma warning restore S1215
            return !weakReference.IsAlive;
        }, TimeSpan.FromSeconds(5));

        Assert.True(released, "可收集 ALC 卸载并强制 GC 后应真正释放 / the collectible ALC should be truly released after unload plus forced collections");
    }

    /// <summary>
    /// 独立方法承载装载与卸载：方法返回即弃置 context / loaded 局部引用，避免 Debug 局部存活保活 ALC。
    /// Loading and unloading live in a separate helper: returning drops the context / loaded locals so
    /// Debug extended local lifetimes cannot keep the ALC alive.
    /// </summary>
    /// <param name="isDistinctCopy">装载副本是否区别于测试宿主程序集实例 / Whether the loaded copy differs from the test-host assembly instance.</param>
    /// <returns>指向已卸载上下文的弱引用 / A weak reference to the unloaded context.</returns>
    private static WeakReference LoadUnloadAndReleaseReferences(out bool isDistinctCopy)
    {
        var sourceAssemblyPath = typeof(CollectibleAssemblyHostTests).Assembly.Location;
        var context = new CollectibleAssemblyLoadContext();
        var loaded = context.LoadFromAssemblyPath(sourceAssemblyPath);
        isDistinctCopy = !ReferenceEquals(typeof(CollectibleAssemblyHostTests).Assembly, loaded);

        var weakReference = new WeakReference(context);
        context.Unload();
        loaded = null;
        context = null;
        return weakReference;
    }
}
