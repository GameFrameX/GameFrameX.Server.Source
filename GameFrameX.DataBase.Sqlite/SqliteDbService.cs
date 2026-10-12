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


using System.Collections.Concurrent;
using System.Text.Json;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 嵌入式数据库服务（手写 ADO，无 EF；C222）。
/// </summary>
/// <remarks>
/// Embedded SQLite implementation of <see cref="IDatabaseService"/> (hand-written ADO, no EF; C222).
/// 存储模型 = PG StateRow 平移：每状态类型一张 <c>(id INTEGER PRIMARY KEY, doc TEXT NOT NULL)</c>，
/// 状态对象经 System.Text.Json 与 <c>doc</c> 互转；查询为「id 等值提取下推 + 剩余 filter 客户端求值」，
/// 客户端求值下与手写 InMemory 的查询语义完全等同（C# 全语义）。
///
/// 边界（启动警告 <c>Database.Provider.SqliteBoundaryWarning</c> 同源，Amendment 3）：
/// ① 复杂条件查询为客户端求值（可能全表扫描）；② 写路径单连接串行；③ 拓扑边界为单进程
/// （跨进程发现/路由不可用，发现层由 <c>InProcDiscoveryRuntime</c> 以自指路由表替代）；
/// ④ Availability 恒报 Healthy。生产规模化使用风险由使用方自担。
///
/// Degraded members (documented on purpose, C222 裁定):
/// <see cref="AvailabilityState"/> always reports <see cref="DatabaseAvailabilityState.Healthy"/> (embedded
/// engine, no network partition semantics); retry policy is single-pass direct execution (no retry loops);
/// there is no background recovery task (a local file either opens or fails fast during <c>Open</c>).
/// </remarks>
public sealed partial class SqliteDbService : IDatabaseService
{
    /// <summary>
    /// 状态对象 ↔ doc 文本的 JSON 序列化选项（进程级单例，启用序列化器缓存）。
    /// </summary>
    /// <remarks>
    /// Process-wide JSON options for state ↔ doc text conversion (a single static instance
    /// enables the serializer's metadata caching).
    /// </remarks>
    private static readonly JsonSerializerOptions DocumentJsonOptions = new()
    {
        IncludeFields = false,
    };

    /// <summary>
    /// 边界警告进程内只打一次的闸（控制库 + 业务库两次 Open 不刷屏，Amendment 3）。
    /// </summary>
    /// <remarks>
    /// Process-wide one-shot gate for the boundary warning so the control and business
    /// database Opens do not print it twice.
    /// </remarks>
    private static int _boundaryWarningEmitted;

    /// <summary>
    /// 单连接（<c>:memory:</c> 模式持连存活；文件模式 WAL）。所有操作经 <see cref="_gate"/> 串行复用。
    /// </summary>
    /// <remarks>
    /// The single connection (kept alive for <c>:memory:</c>; WAL journal mode for file databases).
    /// Every operation reuses it under <see cref="_gate"/>.
    /// </remarks>
    private SqliteConnection _connection;

    /// <summary>
    /// 串行化闸（单连接上的读写互斥；事务持有期间嵌套操作直接放行）。
    /// </summary>
    /// <remarks>
    /// The serialization gate over the single connection; operations nested inside an ambient
    /// transaction bypass the gate (they already run under it).
    /// </remarks>
    private readonly SemaphoreSlim _gate = new(1, 1);

    /// <summary>
    /// 环境事务标记（事务壳内嵌套操作/嵌套事务壳直接加入当前事务，不再重复拿闸/开事务）。
    /// </summary>
    /// <remarks>
    /// Ambient transaction flag: nested operations and nested transaction shells inside an active
    /// transaction join it instead of acquiring the gate / opening a second transaction.
    /// </remarks>
    private static readonly AsyncLocal<bool> AmbientTransaction = new();

    /// <summary>
    /// 表结构就绪缓存（表名 → 单飞建表任务；失败任务驱逐以便重试，对齐 PG 适配器语义）。
    /// </summary>
    /// <remarks>
    /// Table-readiness cache (table name to its single-flight DDL task); faulted tasks are
    /// evicted so the next caller retries, aligned with the PostgreSQL adapter's semantics.
    /// </remarks>
    private readonly ConcurrentDictionary<string, Lazy<Task>> _tableReady = new();

    /// <summary>
    /// 当前数据库配置选项（Open 成功后可用）。
    /// </summary>
    /// <remarks>
    /// The current database options (available after a successful Open).
    /// </remarks>
    private DbOptions _options;

    /// <summary>
    /// 可用性状态（退化成员：嵌入式引擎无网络分区语义，恒报 <see cref="DatabaseAvailabilityState.Healthy"/>）。
    /// </summary>
    /// <remarks>
    /// Degraded member: the embedded engine has no network-partition semantics, so this always
    /// reports <see cref="DatabaseAvailabilityState.Healthy"/>. Monitoring surfaces should not
    /// treat SQLite as a remotely-failable dependency (boundary ④ of the startup warning).
    /// </remarks>
    public DatabaseAvailabilityState AvailabilityState => DatabaseAvailabilityState.Healthy;

    /// <summary>
    /// 获取当前时间戳（毫秒，与 Mongo/PG 适配器逐字对齐）。
    /// </summary>
    /// <remarks>
    /// Gets the current timestamp in milliseconds, aligned verbatim with the Mongo / PostgreSQL
    /// adapters (<see cref="DbOptions.IsUseTimeZone"/> selects the time-zone-offset variant).
    /// </remarks>
    /// <returns>当前时间的 Unix 时间戳（毫秒）/ The current Unix timestamp in milliseconds</returns>
    private long GetCurrentTimestamp()
    {
        if (_options.IsUseTimeZone)
        {
            return TimerHelper.UnixTimeMillisecondsWithTimeZoneOffset();
        }

        return TimerHelper.UnixTimeMilliseconds();
    }

    /// <summary>
    /// 校验连接已打开（未 Open 即调用读写为编程错误，启动路径 Open 失败即 fail-fast 不会到达这里）。
    /// </summary>
    /// <remarks>
    /// Guards that the connection is open. A read/write call before <c>Open</c> is a programming
    /// error; the startup path fails fast inside <c>Open</c> and never reaches this state.
    /// </remarks>
    private void EnsureOpen()
    {
        if (_connection == null)
        {
            // Localization: Database.Sqlite.ServiceUnavailable - SqliteDbService 尚未打开（请先调用 Open）。
            throw new DatabaseUnavailableException(LocalizationService.GetString(Localization.Keys.Database.Sqlite.ServiceUnavailable));
        }
    }
}
