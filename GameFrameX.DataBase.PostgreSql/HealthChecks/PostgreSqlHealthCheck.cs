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
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;

namespace GameFrameX.DataBase.PostgreSql.HealthChecks;

/// <summary>
/// PostgreSQL 健康检查类（连续失败阈值与降级语义对齐 <c>MongoDbHealthCheck</c>）。
/// </summary>
/// <remarks>
/// PostgreSQL health check class with consecutive-failure threshold and degradation semantics
/// aligned with <c>MongoDbHealthCheck</c>.
/// </remarks>
public sealed class PostgreSqlHealthCheck : IHealthCheck
{
    private const int ConsecutiveFailureThreshold = 5;
    private readonly NpgsqlDataSource _dataSource;
    private int _consecutiveFailures;

    /// <summary>
    /// 初始化 PostgreSqlHealthCheck 的新实例。
    /// </summary>
    /// <param name="dataSource">PostgreSQL 数据源 / PostgreSQL data source</param>
    public PostgreSqlHealthCheck(NpgsqlDataSource dataSource)
    {
        _dataSource = dataSource;
    }

    /// <summary>
    /// 执行健康检查。
    /// </summary>
    /// <param name="context">健康检查上下文 / Health check context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>健康检查结果 / Health check result</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var connection = _dataSource.CreateConnection();
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT 1", connection);
            var scalar = await command.ExecuteScalarAsync(cancellationToken);
            if (scalar is 1)
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return HealthCheckResult.Healthy("PostgreSQL connection is healthy");
            }

            var currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            if (currentFailures >= ConsecutiveFailureThreshold)
            {
                return HealthCheckResult.Unhealthy($"PostgreSQL connection is unhealthy after {currentFailures} consecutive failures");
            }

            return HealthCheckResult.Degraded($"PostgreSQL connection is degraded: probe returned unexpected result. consecutiveFailures={currentFailures}");
        }
        catch (Exception ex)
        {
            var currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            if (currentFailures >= ConsecutiveFailureThreshold)
            {
                return HealthCheckResult.Unhealthy($"PostgreSQL connection is unhealthy after {currentFailures} consecutive failures", ex);
            }

            return HealthCheckResult.Degraded($"PostgreSQL connection is temporarily unavailable. consecutiveFailures={currentFailures}", ex);
        }
    }
}
