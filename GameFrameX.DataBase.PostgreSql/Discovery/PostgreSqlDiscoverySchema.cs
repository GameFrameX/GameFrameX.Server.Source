// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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


using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql.Discovery;

/// <summary>
/// 发现层建表脚本的逐语句幂等执行助手（心跳与路由两 store 共享的单一事实源）。
/// </summary>
/// <remarks>
/// Executes the EF-generated discovery create script statement by statement. The multi-table
/// script must NOT run as a single batch: on a partially provisioned control database the first
/// pre-existing object (SQLSTATE 42P07) aborts every later statement and the swallowed exception
/// silently skips the missing tables — later heartbeats/routes then fail with undefined-table
/// (42P01). Per-statement execution with independent tolerance for 42P07 / 42710 makes the
/// bootstrap converge on both fresh and partially existing schemas.
/// </remarks>
internal static class PostgreSqlDiscoverySchema
{
    /// <summary>
    /// 逐条执行建表脚本，独立容忍「同名表 / 同型对象已存在」（42P07 / 42710）。
    /// </summary>
    /// <remarks>
    /// Runs each statement of the create script independently; pre-existing tables and objects
    /// count as success for their statement, so remaining statements still execute.
    /// </remarks>
    /// <param name="context">发现层 EF 上下文 / The discovery EF context</param>
    /// <param name="createScript">EF 生成的多语句建表脚本 / The EF-generated multi-statement create script</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public static async Task ExecuteIdempotentCreateAsync(PostgreSqlDiscoveryDbContext context, string createScript, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(createScript))
        {
            return;
        }

        foreach (var statement in SplitStatements(createScript))
        {
            try
            {
                await context.Database.ExecuteSqlRawAsync(statement, cancellationToken).ConfigureAwait(false);
            }
            catch (PostgresException exception) when (exception.SqlState == PostgreSqlSqlState.DuplicateTable || exception.SqlState == PostgreSqlSqlState.DuplicateObject)
            {
                // 该语句对象已存在（存量控制库 / 并发装配 / 部分建库场景）：视作本语句成功，继续后续语句。
                // The statement's object already exists (stored control database / concurrent bootstrap /
                // partially provisioned schema): success for this statement, keep going with the rest.
            }
        }
    }

    /// <summary>
    /// 幂等补列 server_heartbeat.additional_advertise_endpoints（text[]，可空）。
    /// </summary>
    /// <remarks>
    /// Idempotently adds the heartbeat table's <c>additional_advertise_endpoints</c> column
    /// (a nullable <c>text[]</c>). The EF create script covers fresh databases; this upgrade
    /// statement covers control databases created before the additional-endpoint field
    /// existed. Table and column names are derived through
    /// <see cref="DiscoveryStorageNaming"/> (the zero-name-literal convention);
    /// <c>ADD COLUMN IF NOT EXISTS</c> is itself idempotent.
    /// </remarks>
    /// <param name="context">发现层 EF 上下文 / The discovery EF context</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    public static async Task EnsureHeartbeatAdditionalEndpointsColumnAsync(PostgreSqlDiscoveryDbContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        await context.Database.ExecuteSqlRawAsync(BuildAdditionalEndpointsUpgradeStatement(), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 构建心跳表补列 SQL 文本（表 / 列名 = 编译期封闭类型 / 属性名经 DiscoveryStorageNaming 推导，非运行期输入）。
    /// </summary>
    /// <remarks>
    /// Builds the heartbeat additional-endpoint upgrade SQL text. The table and column names derive
    /// from the compile-time-closed entity / property names through <see cref="DiscoveryStorageNaming"/>
    /// (the zero-name-literal convention) — never runtime input; SQL identifiers cannot be bound
    /// parameters, so the composition is centralized in this dedicated builder with that invariant
    /// stated (Sonar S2077).
    /// </remarks>
    /// <returns>升级 SQL 文本 / The upgrade SQL text</returns>
    private static string BuildAdditionalEndpointsUpgradeStatement()
    {
        var tableName = DiscoveryStorageNaming.TableName<ServerHeartbeatEntity>();
        var columnName = DiscoveryStorageNaming.SnakeCase(nameof(ServerHeartbeatEntity.AdditionalAdvertiseEndpoints));
        return "ALTER TABLE " + tableName + " ADD COLUMN IF NOT EXISTS " + columnName + " text[]";
    }

    /// <summary>
    /// 按分号拆分脚本为非空语句（EF 生成的 DDL 不含数据行 / 字符串字面量中的分号）。
    /// </summary>
    /// <remarks>
    /// Splits the script on semicolons into non-blank statements (EF-generated DDL contains no
    /// semicolons inside row data or string literals).
    /// </remarks>
    /// <param name="createScript">建表脚本 / The create script</param>
    /// <returns>非空语句序列 / The non-blank statements</returns>
    private static IEnumerable<string> SplitStatements(string createScript)
    {
        foreach (var statement in createScript.Split(';'))
        {
            var trimmed = statement.Trim();
            if (trimmed.Length > 0)
            {
                yield return trimmed;
            }
        }
    }
}
