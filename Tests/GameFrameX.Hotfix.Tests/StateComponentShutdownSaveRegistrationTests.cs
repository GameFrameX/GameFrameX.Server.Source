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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com
//   Official Documentation: https://gameframex.doc.alianblank.com
//  ==========================================================================================

using System.Collections;
using System.Diagnostics;
using System.Reflection;
using GameFrameX.Core.Components;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 关服回存注册契约回归测试：注册表以状态类型为键幂等替换（热更后重复注册不叠加执行），
/// 且 <c>SaveAllAsync</c> 整体等待受超时上限保护（悬挂回存委托不会无限阻塞关服流程）。
/// Regression tests for the shutdown-save registration contract: the registry replaces
/// registrations per state type (hot-reload re-registration must not stack executions), and
/// <c>SaveAllAsync</c> waits are capped so a hanging save delegate cannot block shutdown forever.
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class StateComponentShutdownSaveRegistrationTests
{
    /// <summary>幂等替换用例的注册键状态类型。/ Marker state type used as the registration key for the replace case.</summary>
    private sealed class ReplaceableRegistrationState
    {
    }

    /// <summary>独立性/超时用例的注册键状态类型。/ Marker state type used as the registration key for the independence/timeout cases.</summary>
    private sealed class IndependentRegistrationState
    {
    }

    /// <summary>
    /// 同一状态类型注册两次：仅最新委托被执行一次（替换语义，不叠加）。
    /// Registering the same state type twice: only the latest delegate runs exactly once
    /// (replace semantics, no stacking).
    /// </summary>
    [Fact]
    public async Task AddShutdownSaveFunc_SameStateTypeTwice_RunsOnlyLatestDelegateOnce()
    {
        var firstInvocationCount = 0;
        var secondInvocationCount = 0;
        Func<bool, bool, Task> firstSaveFunc = (_, _) => { firstInvocationCount++; return Task.CompletedTask; };
        Func<bool, bool, Task> secondSaveFunc = (_, _) => { secondInvocationCount++; return Task.CompletedTask; };

        try
        {
            StateComponent.AddShutdownSaveFunc(typeof(ReplaceableRegistrationState), firstSaveFunc);
            StateComponent.AddShutdownSaveFunc(typeof(ReplaceableRegistrationState), secondSaveFunc);

            await StateComponent.SaveAllAsync();

            Assert.Equal(0, firstInvocationCount);
            Assert.Equal(1, secondInvocationCount);
        }
        finally
        {
            RemoveRegistration(typeof(ReplaceableRegistrationState));
        }
    }

    /// <summary>
    /// 不同状态类型各自注册：两个委托均被执行一次（互不干扰）。
    /// Distinct state types: both delegates run exactly once (independent entries).
    /// </summary>
    [Fact]
    public async Task AddShutdownSaveFunc_DistinctStateTypes_RunsEachDelegateOnce()
    {
        var firstTypeInvocationCount = 0;
        var secondTypeInvocationCount = 0;
        Func<bool, bool, Task> firstSaveFunc = (_, _) => { firstTypeInvocationCount++; return Task.CompletedTask; };
        Func<bool, bool, Task> secondSaveFunc = (_, _) => { secondTypeInvocationCount++; return Task.CompletedTask; };

        try
        {
            StateComponent.AddShutdownSaveFunc(typeof(ReplaceableRegistrationState), firstSaveFunc);
            StateComponent.AddShutdownSaveFunc(typeof(IndependentRegistrationState), secondSaveFunc);

            await StateComponent.SaveAllAsync();

            Assert.Equal(1, firstTypeInvocationCount);
            Assert.Equal(1, secondTypeInvocationCount);
        }
        finally
        {
            RemoveRegistration(typeof(ReplaceableRegistrationState));
            RemoveRegistration(typeof(IndependentRegistrationState));
        }
    }

    /// <summary>
    /// 悬挂回存委托（永不完成的任务）+ 注入短超时：<c>SaveAllAsync</c> 在超时上限内返回，
    /// 不再无限阻塞关服流程。
    /// A hanging save delegate (never-completing task) with an injected short timeout:
    /// <c>SaveAllAsync</c> returns within the cap instead of blocking shutdown forever.
    /// </summary>
    [Fact]
    public async Task SaveAllAsync_HangingSaveDelegate_ReturnsWithinTimeoutCap()
    {
        var originalTimeout = StateComponent.ShutdownSaveTimeout;
        Func<bool, bool, Task> hangingSaveFunc = (_, _) => Task.Delay(Timeout.InfiniteTimeSpan);

        try
        {
            StateComponent.ShutdownSaveTimeout = TimeSpan.FromMilliseconds(200);
            StateComponent.AddShutdownSaveFunc(typeof(IndependentRegistrationState), hangingSaveFunc);

            var stopwatch = Stopwatch.StartNew();
            await StateComponent.SaveAllAsync();
            stopwatch.Stop();

            // 超时不得早于注入值触发；返回本身即证明等待有界（未修复形态将永久挂起）。
            // The timeout must not fire before the injected value; the mere return proves the wait is bounded.
            Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(200), $"elapsed={stopwatch.Elapsed}");
            Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(10), $"elapsed={stopwatch.Elapsed}");
        }
        finally
        {
            StateComponent.ShutdownSaveTimeout = originalTimeout;
            RemoveRegistration(typeof(IndependentRegistrationState));
        }
    }

    [Fact]
    public async Task Registration_FromReloadedAssembly_ReplacesPreviousGeneration()
    {
        var assemblyName = new AssemblyName("ReloadedStateAssembly");
        var firstAssembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(assemblyName, System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
        var secondAssembly = System.Reflection.Emit.AssemblyBuilder.DefineDynamicAssembly(assemblyName, System.Reflection.Emit.AssemblyBuilderAccess.RunAndCollect);
        var firstType = firstAssembly.DefineDynamicModule("State").DefineType("Reloaded.PlayerState").CreateType();
        var secondType = secondAssembly.DefineDynamicModule("State").DefineType("Reloaded.PlayerState").CreateType();
        var previousCalls = 0;
        var currentCalls = 0;
        try
        {
            StateComponent.AddShutdownSaveFunc(firstType, (shutdown, force) => { previousCalls++; return Task.CompletedTask; });
            StateComponent.AddShutdownSaveFunc(secondType, (shutdown, force) => { currentCalls++; return Task.CompletedTask; });
            await StateComponent.SaveAllAsync();
            Assert.Equal(0, previousCalls);
            Assert.Equal(1, currentCalls);
        }
        finally
        {
            RemoveRegistration(firstType);
            RemoveRegistration(secondType);
        }
    }

    /// <summary>
    /// 反射清理本测试注入的注册条目（注册表无公开注销 API，沿用静态字典反射清理先例）。
    /// Reflectively remove registrations injected by these tests (the registry exposes no public
    /// unregister API; follows the established static-dictionary reflection cleanup pattern).
    /// </summary>
    private static void RemoveRegistration(Type stateType)
    {
        var saveFuncMapField = typeof(StateComponent).GetField("SaveFuncMap", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(saveFuncMapField);
        var saveFuncMap = Assert.IsAssignableFrom<IDictionary>(saveFuncMapField.GetValue(null));
        saveFuncMap.Remove(stateType.Assembly.GetName().Name + ":" + stateType.FullName);
    }
}
