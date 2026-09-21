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
/// <see cref="Initialize"/> guards the state with <see cref="Interlocked.CompareExchange"/> and
/// throws <see cref="BootstrapperAlreadyInitializedException"/> on an explicit second initialization
/// instead of silently overwriting the kernel; <see cref="EnsureInitialized"/> is the idempotent entry
/// used by the per-role startup chain, so subsequent roles become no-ops.
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
    /// 初始化状态（0 = 未初始化，1 = 已初始化）。
    /// </summary>
    /// <remarks>
    /// The initialization state (0 = not initialized, 1 = initialized).
    /// Guarded by <see cref="Interlocked.CompareExchange"/> so concurrent initializers race on a single CAS.
    /// </remarks>
    private static int _initializationState;

    /// <summary>
    /// 获取共享内核是否已初始化。
    /// </summary>
    /// <remarks>
    /// Gets whether the shared kernel has been initialized.
    /// </remarks>
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
    /// If the kernel initialization delegate throws, the guard state is rolled back so a retry is possible.
    /// </remarks>
    /// <param name="sharedKernelInitialization">共享内核初始化委托 / The shared kernel initialization delegate</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="sharedKernelInitialization"/> 为 null 时抛出 / Thrown when <paramref name="sharedKernelInitialization"/> is null</exception>
    /// <exception cref="BootstrapperAlreadyInitializedException">当共享内核已初始化时抛出 / Thrown when the shared kernel has already been initialized</exception>
    public static void Initialize(Action sharedKernelInitialization)
    {
        ArgumentNullException.ThrowIfNull(sharedKernelInitialization, nameof(sharedKernelInitialization));

        if (Interlocked.CompareExchange(ref _initializationState, Initialized, NotInitialized) != NotInitialized)
        {
            throw new BootstrapperAlreadyInitializedException();
        }

        try
        {
            sharedKernelInitialization();
        }
        catch
        {
            // 内核初始化失败：回退守卫状态，允许下一次重试，避免“已初始化但内核残缺”的假成功
            Volatile.Write(ref _initializationState, NotInitialized);
            throw;
        }
    }

    /// <summary>
    /// 幂等初始化共享内核（首个调用生效）。
    /// </summary>
    /// <remarks>
    /// Idempotently initializes the shared kernel: the first caller performs the initialization,
    /// later callers return immediately. Used by the per-role startup chain so each role can call it
    /// while the kernel is created exactly once per process. Concurrent losers of the CAS race
    /// treat the winner's result as success.
    /// </remarks>
    /// <param name="sharedKernelInitialization">共享内核初始化委托 / The shared kernel initialization delegate</param>
    public static void EnsureInitialized(Action sharedKernelInitialization)
    {
        if (IsInitialized)
        {
            return;
        }

        try
        {
            Initialize(sharedKernelInitialization);
        }
        catch (BootstrapperAlreadyInitializedException)
        {
            // 并发落败方：其它调用者已完成初始化，视为成功
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
