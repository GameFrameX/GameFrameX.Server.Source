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
//   or infringe upon the lawful rights and interests of others, as prohibited by law and regulations!
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
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// Sqlite NextSequenceAsync 遇 SQLITE_BUSY 幂等重试回归测试（C327 / GFX-1037）。
/// </summary>
/// <remarks>
/// Regression tests for C327 (GFX-1037): <c>SqliteDbService.NextSequenceAsync</c> runs through the
/// shared resilience engine with <c>isIdempotent: true</c>, so a transient <c>SQLITE_BUSY</c> write
/// conflict retries instead of failing immediately. A second connection to the same file database
/// holds a <c>BEGIN EXCLUSIVE</c> write lock while the service allocates the next sequence value;
/// the service connection carries <c>Default Timeout=1</c> so the first attempt fails fast with
/// <c>SqliteException(5)</c> after Microsoft.Data.Sqlite's ~1 s internal busy wait, and the engine
/// then retries through its {150, 400, 900} ms backoff. Against the pre-fix code (zero retries for
/// non-idempotent writes) both facts fail: the first throws immediately, and the second reports a
/// ~1 s wall time instead of the retry-exhaustion wall time.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class SqliteNextSequenceBusyRetryTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    /// <summary>
    /// 测试 busy 冲突经幂等重试后成功返回下一序列值（A/B：旧代码首次尝试即抛 DatabaseUnavailableException）。
    /// </summary>
    [Fact]
    public async Task NextSequence_BusyConflict_ShouldRetryAndReturnNextValue()
    {
        var (service, dataFile) = await CreateServiceAsync();

        // 预热：建序列表 + 基线取号（锁外，必成功）。
        Assert.Equal(1L, await service.NextSequenceAsync<NextSequenceBusyRetryTestState>("busy"));

        using (var locker = new SqliteConnection($"Data Source={dataFile};Pooling=False"))
        {
            await locker.OpenAsync();
            await ExecuteNonQueryAsync(locker, "BEGIN EXCLUSIVE;");
            var released = false;
            try
            {
                // 首次尝试约 1 s 后以 SQLITE_BUSY 失败；约 1.5 s 后释锁，重试成功。
                var release = Task.Run(async () =>
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(1500));
                    await ExecuteNonQueryAsync(locker, "COMMIT;");
                    released = true;
                });

                var value = await service.NextSequenceAsync<NextSequenceBusyRetryTestState>("busy");
                await release;

                Assert.True(released);
                Assert.Equal(2L, value);
            }
            finally
            {
                await RollbackIfOpenAsync(locker);
            }
        }

        await service.CloseAsync();
    }

    /// <summary>
    /// 测试 busy 冲突重试耗尽后仍抛 DatabaseUnavailableException（内层保留 SQLITE_BUSY 异常，非静默吞掉）。
    /// </summary>
    [Fact]
    public async Task NextSequence_BusyNeverReleased_ShouldThrowDatabaseUnavailableWithBusyInner()
    {
        var (service, dataFile) = await CreateServiceAsync();

        // 预热：建序列表 + 基线取号（锁外，必成功）。
        Assert.Equal(1L, await service.NextSequenceAsync<NextSequenceBusyRetryTestState>("busy"));

        using (var locker = new SqliteConnection($"Data Source={dataFile};Pooling=False"))
        {
            await locker.OpenAsync();
            await ExecuteNonQueryAsync(locker, "BEGIN EXCLUSIVE;");
            try
            {

                var stopwatch = Stopwatch.StartNew();
                var thrown = await Assert.ThrowsAsync<DatabaseUnavailableException>(() => service.NextSequenceAsync<NextSequenceBusyRetryTestState>("busy"));
                stopwatch.Stop();

                var busy = Assert.IsType<SqliteException>(thrown.InnerException);
                Assert.Equal(5, busy.SqliteErrorCode);

                // 重试确实发生：3 次 busy 等待（各约 1 s）+ 重试退避 150+400+900 ms + jitter 远大于旧代码的一次性失败。
                Assert.True(stopwatch.Elapsed >= TimeSpan.FromMilliseconds(2500), "重试耗尽耗时低于下限，疑似未走重试路径");
                Assert.True(stopwatch.Elapsed <= TimeSpan.FromMilliseconds(20000), "重试耗尽耗时超上限");
            }
            finally
            {
                await RollbackIfOpenAsync(locker);
            }
        }

        await service.CloseAsync();
    }

    /// <summary>
    /// 创建文件库 service（1 s 命令超时使 busy 快速暴露；禁用连接池确保锁连接物理独立）。
    /// </summary>
    private async Task<(SqliteDbService Service, string DataFile)> CreateServiceAsync()
    {
        var root = CreateTempDirectory();
        var dataFile = Path.Combine(root, "sequence-busy.db");
        var service = new SqliteDbService();
        var opened = await service.OpenAsync(new DbOptions
        {
            ConnectionString = $"Data Source={dataFile};Default Timeout=1;Pooling=False",
            Name = "c327-next-sequence-busy-retry",
        });
        Assert.True(opened);
        return (service, dataFile);
    }

    private static async Task ExecuteNonQueryAsync(SqliteConnection connection, string commandText)
    {
        using (var command = connection.CreateCommand())
        {
            command.CommandText = commandText;
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task RollbackIfOpenAsync(SqliteConnection connection)
    {
        try
        {
            await ExecuteNonQueryAsync(connection, "ROLLBACK;");
        }
        catch (SqliteException)
        {
            // 事务已提交或无活动事务时忽略，避免清理路径遮蔽真实断言。
        }
    }

    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx-sqlite-busy-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    /// 清理临时目录（SQLite 文件与 WAL 副产物一并删除）。
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响测试结果
            }
        }
    }

    /// <summary>
    /// busy 重试回归测试状态实体。
    /// </summary>
    private sealed class NextSequenceBusyRetryTestState : BaseCacheState
    {
    }
}
