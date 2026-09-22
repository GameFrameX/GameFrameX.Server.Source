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
//   Any legal disputes or liabilities arising from secondary development based on this project
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


namespace GameFrameX.StartUp;

/// <summary>
/// 进程级共享内核幂等初始化器（C143b D5）。
/// </summary>
/// <remarks>
/// Process-level shared kernel idempotent initializer (C143b D5).
/// The shared kernel (log handler, proto registration, hotfix infrastructure, global settings)
/// must be initialized exactly once before the first role of the process starts.
/// <see cref="Initialize"/> guards the state behind an initialization gate and
/// throws <see cref="BootstrapperAlreadyInitializedException"/> on an explicit second initialization
/// instead of silently overwriting the kernel; <see cref="EnsureInitialized"/> is the idempotent entry
/// used by the per-role startup chain: concurrent callers block until the running initialization
/// publishes its result, and <see cref="IsInitialized"/> turns <c>true</c> only after success.
///
/// </remarks>
public static class AppBootstrapper
{
    /// <summary>
    /// 未初始化状态标记。
    /// </summary>
    /// <remarks>
    /// The not-initialized state marker.
    /// </remarks>
    private const int NotInitialized = 0;

    /// <summary>
    /// 已初始化状态标记。
    /// </summary>
    /// <remarks>
    /// The initialized state marker.
    /// </remarks>
    private const int Initialized = 1;

    /// <summary>
    /// 初始化中状态标记（首个调用者正在执行内核初始化委托）。
    /// </summary>
    /// <remarks>
    /// The initializing state marker (the first caller is currently running the kernel initialization delegate).
    /// </summary>
    private const int Initializing = 2;

    /// <summary>
    /// 初始化互斥锁：串行化状态迁移并让并发等待方阻塞等待初始化结果。
    /// </summary>
    /// <remarks>
    /// The initialization gate: serializes state transitions and lets concurrent waiters block until the initialization result is published.
    /// </summary>
    private static readonly object InitializationGate = new object();

    /// <summary>
    /// 初始化状态（0 = 未初始化，1 = 已初始化，2 = 初始化中）。
    /// </summary>
    /// <remarks>
    /// The initialization state (0 = not initialized, 1 = initialized, 2 = initializing).
    /// <see cref="Initializing"/> is published only while the kernel delegate runs;
    /// <see cref="Initialized"/> is published only after the delegate completes successfully (C143b D5),
    /// so waiters never consume an incomplete shared kernel.
    /// </remarks>
    private static int _initializationState;

    /// <summary>
    /// 获取共享内核是否已初始化。
    /// </summary>
    /// <remarks>
    /// Gets whether the shared kernel has been initialized.
    /// </summary>
    /// <value>已初始化则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if initialized; otherwise, <c>false</c></value>
    public static bool IsInitialized
    {
        get { return Volatile.Read(ref _initializationState) == Initialized; }
    }

    /// <summary>
    /// 显式初始化共享内核（二次调用抛异常）。
    /// </summary>
    /// <remarks>
    /// Explicitly initializes the shared kernel; a second call throws
    /// <see cref="BootstrapperAlreadyInitializedException"/> instead of silently overwriting the kernel (D5).
    /// The state first moves to <see cref="Initializing"/>, and <see cref="Initialized"/> is published only after
    /// the delegate completes successfully; a concurrent explicit call blocks on the gate and then throws.
    /// If the kernel initialization delegate throws, the guard state is rolled back so a retry is possible.
    /// </remarks>
    /// <param name="sharedKernelInitialization">共享内核初始化委托 / The shared kernel initialization delegate</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="sharedKernelInitialization"/> 为 null 时抛出 / Thrown when <paramref name="sharedKernelInitialization"/> is null</exception>
    /// <exception cref="BootstrapperAlreadyInitializedException">当共享内核已初始化或正在初始化时抛出 / Thrown when the shared kernel has already been initialized or is currently initializing</exception>
    public static void Initialize(Action sharedKernelInitialization)
    {
        ArgumentNullException.ThrowIfNull(sharedKernelInitialization, nameof(sharedKernelInitialization));

        lock (InitializationGate)
        {
            if (Volatile.Read(ref _initializationState) != NotInitialized)
            {
                throw new BootstrapperAlreadyInitializedException();
            }

            // 先发布 Initializing：Initialized 仅在委托成功完成后发布，等待方不得消费未完成的共享内核
            Volatile.Write(ref _initializationState, Initializing);
            try
            {
                sharedKernelInitialization();
                Volatile.Write(ref _initializationState, Initialized);
            }
            catch
            {
                // 内核初始化失败：回退守卫状态，允许下一次重试，避免“已初始化但内核残缺”的假成功
                Volatile.Write(ref _initializationState, NotInitialized);
                throw;
            }
        }
    }

    /// <summary>
    /// 幂等初始化共享内核（首个调用生效，并发调用阻塞等待结果）。
    /// </summary>
    /// <remarks>
    /// Idempotently initializes the shared kernel: the first caller performs the initialization,
    /// and concurrent callers block on the initialization gate until the result is published —
    /// they return only after the winner's delegate has completed successfully, never while it is still running.
    /// If the first initialization fails (state rolled back to not-initialized), the unblocked waiter
    /// retries by running its own delegate and propagates the failure if that also fails.
    /// </remarks>
    /// <param name="sharedKernelInitialization">共享内核初始化委托 / The shared kernel initialization delegate</param>
    public static void EnsureInitialized(Action sharedKernelInitialization)
    {
        if (IsInitialized)
        {
            return;
        }

        lock (InitializationGate)
        {
            // 拿到锁时不可能是其它线程的初始化中途（Initializing 只在持锁期间可见）：
            // 已初始化 → 首个初始化已成功，等待方直接返回；
            // 未初始化 → 成为初始化执行方（含首个初始化失败回滚后的重试），失败时异常向本调用传播
            if (Volatile.Read(ref _initializationState) == Initialized)
            {
                return;
            }

            Initialize(sharedKernelInitialization);
        }
    }

    /// <summary>
    /// 重置初始化状态（测试钩子，禁止业务代码调用）。
    /// </summary>
    /// <remarks>
    /// Resets the initialization state (test hook only; production code must never call this).
    /// </remarks>
    internal static void Reset()
    {
        Volatile.Write(ref _initializationState, NotInitialized);
    }
}
