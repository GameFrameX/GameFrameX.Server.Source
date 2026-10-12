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


using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 连接管理（Open 路径诊断与可写校验 / 边界警告 / 信息行，Amendment 3/4）。
/// </summary>
/// <remarks>
/// Connection management for the SQLite adapter. The Open path performs the Amendment 4
/// pre-checks (Data Source presence, full-path resolution, directory creation with a real
/// create+delete probe, file ReadWrite probe) and fails fast with localized errors carrying
/// the full path and the failing stage; the per-Open info line prints the resolved data file
/// (or the in-memory no-persistence notice), and the boundary warning fires once per process.
/// </remarks>
public sealed partial class SqliteDbService
{
    /// <summary>
    /// 内存数据库连接串标记。
    /// </summary>
    /// <remarks>
    /// The in-memory database marker inside a SQLite connection string.
    /// </remarks>
    private const string MemoryDataSource = ":memory:";

    /// <summary>
    /// 连接数据库：Data Source 预检 → 路径诊断与可写校验 → 打开（文件模式启用 WAL）→ 信息行 + 边界警告。
    /// </summary>
    /// <remarks>
    /// Opens the database. Failure semantics: argument problems throw (like the Mongo/PG adapters);
    /// path / writability problems throw localized fail-fast errors (Amendment 4 — never wait for
    /// the first write to fail); the in-memory variant only skips the file checks. This method
    /// effectively never returns <c>false</c> for an embedded engine — a reachable local file opens
    /// or the diagnosis throws. The per-Open info line and the once-per-process boundary warning
    /// are printed on the success path.
    /// </remarks>
    /// <param name="dbOptions">数据库配置选项 / Database configuration options</param>
    /// <exception cref="ArgumentNullException">当 dbOptions / ConnectionString / Name 为 null 时抛出 / Thrown when dbOptions, ConnectionString or Name is null</exception>
    /// <exception cref="InvalidOperationException">当连接串缺 Data Source 或路径预检失败时抛出（含全路径与失败环节）/ Thrown when the connection string lacks Data Source or a path pre-check fails (carrying the full path and the failing stage)</exception>
    /// <returns>打开是否成功（嵌入式引擎为 true 或抛出）/ Whether the open succeeded (an embedded engine returns true or throws)</returns>
    public async Task<bool> Open(DbOptions dbOptions)
    {
        ArgumentNullException.ThrowIfNull(dbOptions);
        ArgumentNullException.ThrowIfNull(dbOptions.ConnectionString);
        ArgumentNullException.ThrowIfNull(dbOptions.Name);

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            var dataSource = ResolveDataSourceOrThrow(dbOptions.ConnectionString);
            var isMemory = string.Equals(dataSource, MemoryDataSource, StringComparison.OrdinalIgnoreCase);
            string fullPath = null;
            if (!isMemory)
            {
                fullPath = PrecheckWritableFileOrThrow(dataSource);
            }

            // 同目标复用：连接串与库名逐字相等且连接仍存活（SELECT 1 探活）则直接复用；否则重建。
            if (_connection != null && string.Equals(_options?.ConnectionString, dbOptions.ConnectionString, StringComparison.Ordinal) && string.Equals(_options?.Name, dbOptions.Name, StringComparison.Ordinal) && await ProbeConnectionAsync().ConfigureAwait(false))
            {
                _options = dbOptions;
                return true;
            }

            CloseCore();

            _connection = new SqliteConnection(dbOptions.ConnectionString);
            await _connection.OpenAsync().ConfigureAwait(false);
            _options = dbOptions;

            if (!isMemory)
            {
                using var command = _connection.CreateCommand();
                command.CommandText = "PRAGMA journal_mode=WAL;";
                command.ExecuteNonQuery();
            }

            if (isMemory)
            {
                // Localization: Database.Provider.SqliteDataFileInfoMemory - SQLite 内存模式：无数据文件，数据不持久化——进程退出即全部丢失。
                LogHelper.Info(LocalizationService.GetString(Localization.Keys.Database.SqliteDataFileInfoMemory));
            }
            else
            {
                // Localization: Database.Provider.SqliteDataFileInfo - SQLite 数据文件：{0}（目录可写 ✓ / 文件可读写 ✓）。
                LogHelper.Info(LocalizationService.GetString(Localization.Keys.Database.SqliteDataFileInfo, fullPath));
            }

            EmitBoundaryWarningOnce();
            return true;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 关闭数据库连接（释放单连接与建表缓存）。
    /// </summary>
    /// <remarks>
    /// Closes the connection and clears the table-readiness cache.
    /// </remarks>
    /// <returns>异步任务 / Async task</returns>
    public Task Close()
    {
        _gate.Wait();
        try
        {
            CloseCore();
            return Task.CompletedTask;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// 解析 Data Source（缺失即本地化 fail-fast，报错即文档，附两示例）。
    /// </summary>
    /// <remarks>
    /// Resolves the Data Source token; a missing token fails fast with the localized
    /// self-documenting error carrying both usage examples.
    /// </remarks>
    /// <param name="connectionString">连接串 / The connection string</param>
    /// <exception cref="InvalidOperationException">当连接串缺少 Data Source 时抛出 / Thrown when the connection string lacks Data Source</exception>
    /// <returns>数据源值（文件路径或 :memory:）/ The data source value (file path or :memory:)</returns>
    private static string ResolveDataSourceOrThrow(string connectionString)
    {
        string dataSource;
        try
        {
            dataSource = new SqliteConnectionStringBuilder(connectionString).DataSource;
        }
        catch (ArgumentException exception)
        {
            dataSource = null;
            // SqliteConnectionStringBuilder 对非法连接串参数抛 ArgumentException——统一走缺 Data Source 的诊断口径。
            // The builder rejects malformed connection strings with an ArgumentException — funnel them into the same diagnosis.
            _ = exception;
        }

        if (string.IsNullOrWhiteSpace(dataSource))
        {
            // Localization: Database.Provider.SqliteDataSourceMissing - SQLite 连接串缺少“Data Source=”（示例：Data Source=:memory: 或 Data Source=/abs/path/game.db）。当前连接串：{0}
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.SqliteDataSourceMissing, connectionString));
        }

        return dataSource;
    }

    /// <summary>
    /// 文件模式前置预检（Amendment 4）：全路径解析 → 目录创建 → 目录可写探针（真实 I/O）→ 文件 ReadWrite 探测。
    /// </summary>
    /// <remarks>
    /// File-mode pre-checks: resolve the full path against the current working directory
    /// (Microsoft.Data.Sqlite resolves relatives the same way), create the directory when
    /// missing, prove directory writability with a real create+delete probe (no permission-bit
    /// guessing, cross-platform), and probe an existing file with a ReadWrite open (catching ACL
    /// denials and exclusive locks held by other processes). Any failure throws the localized
    /// fail-fast error carrying the full path and the failing stage token.
    /// </remarks>
    /// <param name="dataSource">连接串中的数据源（可为相对路径）/ The data source from the connection string (may be relative)</param>
    /// <exception cref="InvalidOperationException">当目录不可创建/不可写或文件不可读写/被占用时抛出 / Thrown when the directory cannot be created/written or the file cannot be opened read-write</exception>
    /// <returns>解析后的数据文件全路径 / The resolved absolute data file path</returns>
    private static string PrecheckWritableFileOrThrow(string dataSource)
    {
        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(dataSource);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            // Localization: Database.Provider.SqlitePathNotAccessible - SQLite 数据文件路径不可用：{0}（失败环节：{1}）。
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.SqlitePathNotAccessible, dataSource, "path-resolve"), exception);
        }

        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            try
            {
                Directory.CreateDirectory(directory);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.SqlitePathNotAccessible, fullPath, "directory-create"), exception);
            }

            try
            {
                var probePath = Path.Combine(directory, $".gfx_sqlite_probe_{Guid.NewGuid():N}.tmp");
                File.WriteAllText(probePath, string.Empty);
                File.Delete(probePath);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.SqlitePathNotAccessible, fullPath, "directory-write-probe"), exception);
            }
        }

        if (File.Exists(fullPath))
        {
            try
            {
                using var _ = File.Open(fullPath, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
            }
            catch (Exception exception) when (exception is UnauthorizedAccessException or IOException)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.SqlitePathNotAccessible, fullPath, "file-read-write-probe"), exception);
            }
        }

        return fullPath;
    }

    /// <summary>
    /// 打印边界警告（Warning 级、本地化、四条硬边界 + 自担声明；进程内仅一次，Amendment 3）。
    /// </summary>
    /// <remarks>
    /// Emits the boundary warning (Warning level, localized, the four hard boundaries plus the
    /// own-risk statement) at most once per process so the control and business database Opens
    /// do not both print it.
    /// </remarks>
    private static void EmitBoundaryWarningOnce()
    {
        if (Interlocked.CompareExchange(ref _boundaryWarningEmitted, 1, 0) != 0)
        {
            return;
        }

        // Localization: Database.Provider.SqliteBoundaryWarning - SQLite 边界警告：① 复杂条件查询为客户端求值（可能全表扫描）；② 写路径单连接串行；③ 拓扑边界为单进程（跨进程发现/路由不可用，心跳 no-op）；④ 可用性监控恒报 Healthy。生产规模化使用风险由使用方自担。
        LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Database.SqliteBoundaryWarning));
        BoundaryWarningEmitCount++;
    }

    /// <summary>
    /// 探活当前连接（SELECT 1）。
    /// </summary>
    /// <remarks>
    /// Probes the current connection with SELECT 1.
    /// </remarks>
    /// <returns>连接是否存活 / Whether the connection is alive</returns>
    private async Task<bool> ProbeConnectionAsync()
    {
        try
        {
            using var command = _connection.CreateCommand();
            command.CommandText = "SELECT 1;";
            var result = await command.ExecuteScalarAsync().ConfigureAwait(false);
            return result is int or long && Convert.ToInt64(result) == 1L;
        }
        catch (SqliteException)
        {
            return false;
        }
    }

    /// <summary>
    /// 边界警告实际发出次数（internal 测试缝：断言「进程内仅一次」语义；仅 IVT 可见）。
    /// </summary>
    /// <remarks>
    /// The number of times the boundary warning was actually emitted (internal test seam for the
    /// once-per-process semantics; visible to the test assembly via IVT only).
    /// </remarks>
    internal static int BoundaryWarningEmitCount { get; private set; }

    /// <summary>
    /// 重置边界警告一次性闸与计数（仅测试隔离使用）。
    /// </summary>
    /// <remarks>
    /// Resets the one-shot gate and the counter. Test-only: production code never calls this.
    /// </remarks>
    internal static void ResetBoundaryWarningForTest()
    {
        _boundaryWarningEmitted = 0;
        BoundaryWarningEmitCount = 0;
    }

    /// <summary>
    /// 释放连接与建表缓存（调用方须持有 <see cref="_gate"/>）。
    /// </summary>
    /// <remarks>
    /// Disposes the connection and clears the table cache (the caller must hold <see cref="_gate"/>).
    /// </remarks>
    private void CloseCore()
    {
        _connection?.Dispose();
        _connection = null;
        _options = null;
        _tableReady.Clear();
    }
}
