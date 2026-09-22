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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.StartUp;
using Xunit;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// AppBootstrapper 静态状态测试集合定义：进程级静态守卫状态要求测试间串行。
/// </summary>
[CollectionDefinition(nameof(StartUpStaticStateCollection), DisableParallelization = true)]
public class StartUpStaticStateCollection
{
}

/// <summary>
/// AppBootstrapper 进程级共享内核幂等初始化测试（C143b D5）。
/// </summary>
/// <remarks>
/// <see cref="CollectionDefinitionAttribute.DisableParallelization"/> keeps these tests serialized
/// with the other StartUp static-state tests, because <see cref="AppBootstrapper"/> holds process-wide state.
/// </remarks>
[Collection(nameof(StartUpStaticStateCollection))]
public class AppBootstrapperTests
{
    /// <summary>
    /// 每个测试前重置进程级守卫状态，避免测试间污染。
    /// </summary>
    public AppBootstrapperTests()
    {
        AppBootstrapper.Reset();
    }

    /// <summary>
    /// 显式二次初始化抛 BootstrapperAlreadyInitializedException，内核委托只执行一次。
    /// </summary>
    [Fact]
    public void Initialize_SecondCall_ThrowsBootstrapperAlreadyInitialized()
    {
        var invocationCount = 0;
        AppBootstrapper.Initialize(() => invocationCount++);

        Assert.True(AppBootstrapper.IsInitialized);
        Assert.Throws<BootstrapperAlreadyInitializedException>(() => AppBootstrapper.Initialize(() => invocationCount++));
        Assert.Equal(1, invocationCount);
    }

    /// <summary>
    /// EnsureInitialized 多次调用只执行首个内核委托，后续调用无异常。
    /// </summary>
    [Fact]
    public void EnsureInitialized_MultipleCalls_InitializeExactlyOnce()
    {
        var invocationCount = 0;
        AppBootstrapper.EnsureInitialized(() => invocationCount++);
        AppBootstrapper.EnsureInitialized(() => invocationCount += 10);
        AppBootstrapper.EnsureInitialized(() => invocationCount += 100);

        Assert.Equal(1, invocationCount);
        Assert.True(AppBootstrapper.IsInitialized);
    }

    /// <summary>
    /// EnsureInitialized 在显式 Initialize 之后调用是无害 no-op，不执行新委托。
    /// </summary>
    [Fact]
    public void EnsureInitialized_AfterExplicitInitialize_IsNoOp()
    {
        var invocationCount = 0;
        AppBootstrapper.Initialize(() => invocationCount++);

        AppBootstrapper.EnsureInitialized(() => invocationCount += 10);

        Assert.Equal(1, invocationCount);
    }

    /// <summary>
    /// 内核委托抛异常时守卫状态回退（IsInitialized=false），重试可成功。
    /// </summary>
    [Fact]
    public void Initialize_FailingKernel_RollsBackStateAndRetrySucceeds()
    {
        var invocationCount = 0;
        Assert.Throws<InvalidOperationException>(() => AppBootstrapper.Initialize(() =>
        {
            invocationCount++;
            throw new InvalidOperationException("kernel failure");
        }));

        Assert.False(AppBootstrapper.IsInitialized);

        AppBootstrapper.Initialize(() => invocationCount++);

        Assert.True(AppBootstrapper.IsInitialized);
        Assert.Equal(2, invocationCount);
    }

    /// <summary>
    /// 并发 EnsureInitialized 只有一个调用者执行内核委托，其余视为成功。
    /// </summary>
    [Fact]
    public void EnsureInitialized_ConcurrentCalls_InitializeExactlyOnce()
    {
        var invocationCount = 0;

        Parallel.For(0, 8, _ =>
        {
            AppBootstrapper.EnsureInitialized(() =>
            {
                invocationCount++;
                Thread.Sleep(20);
            });
        });

        Assert.Equal(1, invocationCount);
        Assert.True(AppBootstrapper.IsInitialized);
    }

    /// <summary>
    /// 并发等待方在首个内核委托完成前不返回：阻塞首个委托，确认其它调用被挂起、
    /// IsInitialized 仅在成功后发布、委托只执行一次。
    /// </summary>
    [Fact]
    public async Task EnsureInitialized_ConcurrentCalls_WaitersBlockUntilFirstInitializationSucceeds()
    {
        var invocationCount = 0;
        var firstDelegateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDelegate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var waiterReturnCount = 0;

        var initializer = Task.Run(() =>
        {
            AppBootstrapper.EnsureInitialized(() =>
            {
                Interlocked.Increment(ref invocationCount);
                firstDelegateEntered.SetResult();
                releaseFirstDelegate.Task.Wait();
            });
        });

        // 首个委托已进入且被阻塞：Initialized 尚未发布
        await firstDelegateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(AppBootstrapper.IsInitialized);

        var waiters = Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            AppBootstrapper.EnsureInitialized(() => Interlocked.Increment(ref invocationCount));
            Interlocked.Increment(ref waiterReturnCount);
        })).ToArray();

        // 等待方全部阻塞：在首个委托完成前不得有任何一个返回
        await Task.Delay(150);
        Assert.Equal(0, Volatile.Read(ref waiterReturnCount));
        Assert.False(AppBootstrapper.IsInitialized);

        releaseFirstDelegate.SetResult();
        await Task.WhenAll(waiters).WaitAsync(TimeSpan.FromSeconds(5));
        await initializer.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(1, invocationCount);
        Assert.Equal(waiters.Length, Volatile.Read(ref waiterReturnCount));
        Assert.True(AppBootstrapper.IsInitialized);
    }

    /// <summary>
    /// 首个初始化失败后等待方重试：阻塞的首个委托抛异常回滚状态，等待方用自己的委托重试成功。
    /// </summary>
    [Fact]
    public async Task EnsureInitialized_FirstInitializationFails_WaiterRetriesWithOwnDelegate()
    {
        var invocationCount = 0;
        var firstDelegateEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseFirstDelegate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var initializer = Task.Run(() =>
        {
            AppBootstrapper.EnsureInitialized(() =>
            {
                Interlocked.Increment(ref invocationCount);
                firstDelegateEntered.SetResult();
                releaseFirstDelegate.Task.Wait();
                throw new InvalidOperationException("kernel failure");
            });
        });

        await firstDelegateEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var waiter = Task.Run(() => AppBootstrapper.EnsureInitialized(() => Interlocked.Increment(ref invocationCount)));

        // 等待方阻塞在首个（即将失败的）初始化上，不得提前返回
        await Task.Delay(150);
        Assert.False(waiter.IsCompleted);
        Assert.False(AppBootstrapper.IsInitialized);

        releaseFirstDelegate.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => initializer.WaitAsync(TimeSpan.FromSeconds(5)));

        // 首个失败回滚后，等待方用自己的委托重试并成功（重试必在回滚之后，否则 Initialize 会抛 BootstrapperAlreadyInitializedException）
        await waiter.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, invocationCount);
        Assert.True(AppBootstrapper.IsInitialized);
    }

    /// <summary>
    /// Initialize 对 null 委抛 ArgumentNullException。
    /// </summary>
    [Fact]
    public void Initialize_NullAction_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => AppBootstrapper.Initialize(null));
    }
}
