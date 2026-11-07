// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// SQLite 连接闸等待超时测试（事务壳 action 内不可重入死锁限时失败而非永久挂起）。
/// </summary>
/// <remarks>
/// Regression tests for GFX-1035: while a transaction shell holds the non-reentrant connection
/// gate, a same-instance gate acquisition that bypasses the ambient transaction must fail with
/// <see cref="SqliteGateWaitTimeoutException"/> within <c>GateWaitTimeout</c> instead of hanging,
/// and the gate timeout must not be retried by the shared resilience engine (a retried timeout
/// would surface as <see cref="DatabaseUnavailableException"/> after several timeout periods).
/// Every await on the scenario is bounded by a cancellation token so a regression fails the test
/// with <see cref="OperationCanceledException"/> rather than hanging the suite.
/// </remarks>
public sealed class SqliteGateWaitTimeoutTests
{
    /// <summary>
    /// 测试缝注入的闸等待超时（缩短死锁判定时间）。
    /// </summary>
    private static readonly TimeSpan InjectedGateWaitTimeout = TimeSpan.FromSeconds(1);

    /// <summary>
    /// 场景兜底时限（远大于注入超时；回归时以取消失败代替挂起）。
    /// </summary>
    private static readonly TimeSpan ScenarioDeadline = TimeSpan.FromSeconds(15);

    /// <summary>
    /// action 内 await 同实例 CloseAsync（不经环境事务放行）：限时抛闸超时异常，且事务回滚、闸被释放。
    /// </summary>
    [Fact]
    public async Task ExecuteInTransaction_ActionAwaitsSameInstanceClose_ShouldThrowGateTimeoutAndRollBack()
    {
        var service = await OpenServiceAsync();
        try
        {
            using (var deadline = new CancellationTokenSource(ScenarioDeadline))
            {
                // 壳外先写一行：建表落在事务外（避免 DDL 随回滚撤销），同时作为回滚后仍保留的对照行。
                await service.AddAsync(new GateWaitTimeoutTestState { Id = 100, Name = "kept", }).WaitAsync(deadline.Token);

                var exception = await Assert.ThrowsAsync<SqliteGateWaitTimeoutException>(() => service.ExecuteInTransactionAsync(async () =>
                {
                    await service.AddAsync(new GateWaitTimeoutTestState { Id = 1, Name = "rolled-back", });
                    await service.CloseAsync();
                }).WaitAsync(deadline.Token));

                Assert.IsAssignableFrom<TimeoutException>(exception);
                Assert.Contains(nameof(SqliteDbService.CloseAsync), exception.Message);

                // 壳因 action 失败回滚并释放闸：后续壳外操作可正常拿闸，action 内写入未持久化，壳外对照行保留。
                var rolledBack = await service.FindAsync<GateWaitTimeoutTestState>(1, isCreateIfNotExists: false).WaitAsync(deadline.Token);
                Assert.Null(rolledBack);
                var kept = await service.FindAsync<GateWaitTimeoutTestState>(100, isCreateIfNotExists: false).WaitAsync(deadline.Token);
                Assert.NotNull(kept);
                Assert.Equal("kept", kept.Name);
            }
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 闸被事务壳持有时，另一异步流的读操作限时抛闸超时原异常（不被弹性引擎重试包装为 DatabaseUnavailableException）。
    /// </summary>
    [Fact]
    public async Task Find_WhileShellHoldsGate_ShouldThrowGateTimeoutWithoutRetry()
    {
        var service = await OpenServiceAsync();
        var actionEntered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseAction = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            using (var deadline = new CancellationTokenSource(ScenarioDeadline))
            {
                var shell = service.ExecuteInTransactionAsync(async () =>
                {
                    actionEntered.SetResult();
                    await releaseAction.Task;
                });
                await actionEntered.Task.WaitAsync(deadline.Token);

                await Assert.ThrowsAsync<SqliteGateWaitTimeoutException>(() => service.FindAsync<GateWaitTimeoutTestState>(2, isCreateIfNotExists: false).WaitAsync(deadline.Token));

                releaseAction.SetResult();
                await shell.WaitAsync(deadline.Token);
            }
        }
        finally
        {
            releaseAction.TrySetResult();
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 打开独立内存库服务并注入短闸超时。
    /// </summary>
    /// <returns>已打开的服务 / The opened service</returns>
    private static async Task<SqliteDbService> OpenServiceAsync()
    {
        var service = new SqliteDbService
        {
            GateWaitTimeout = InjectedGateWaitTimeout,
        };
        var opened = await service.OpenAsync(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = "gfx-1035-gate-wait-timeout-tests",
        });
        Assert.True(opened);
        return service;
    }

    /// <summary>
    /// 闸超时测试状态类型。
    /// </summary>
    private sealed class GateWaitTimeoutTestState : BaseCacheState
    {
        /// <summary>
        /// 名称字段。
        /// </summary>
        public string Name { get; set; } = string.Empty;
    }
}
