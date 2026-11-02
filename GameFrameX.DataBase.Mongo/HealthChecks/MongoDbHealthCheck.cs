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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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


using Microsoft.Extensions.Diagnostics.HealthChecks;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo.HealthChecks;

/// <summary>
/// MongoDB 健康检查类。
/// </summary>
/// <remarks>
/// MongoDB health check class.
/// </remarks>
public sealed class MongoDbHealthCheck : IHealthCheck
{
    /// <summary>
    /// 共享健康检查客户端的创建锁。
    /// </summary>
    /// <remarks>
    /// The creation lock for the shared health check client.
    /// </remarks>
    private static readonly object SharedClientLock = new();

    /// <summary>
    /// 进程内共享的健康检查客户端（按连接串复用，避免每次探测重建连接池）。
    /// </summary>
    /// <remarks>
    /// The process-wide shared health check client (reused per connection string so each probe does not rebuild a connection pool).
    /// </remarks>
    private static IMongoClient _sharedHealthClient;

    /// <summary>
    /// 共享客户端当前对应的连接串。
    /// </summary>
    /// <remarks>
    /// The connection string the shared client currently belongs to.
    /// </remarks>
    private static string _sharedHealthClientConnectionString;

    private const int ConsecutiveFailureThreshold = 5;
    private readonly IMongoClient _mongoClient;
    private int _consecutiveFailures;

    /// <summary>
    /// 按连接串初始化 MongoDbHealthCheck（客户端在进程内按连接串共享缓存）。
    /// 供宿主在未直接引用 MongoDB.Driver 时经 Provider 程序集反射装配健康检查。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of MongoDbHealthCheck from a connection string (the underlying
    /// client is cached and shared per connection string within the process). Hosts that do not
    /// reference MongoDB.Driver directly resolve this constructor through the provider assembly.
    /// </remarks>
    /// <param name="connectionString">MongoDB 连接串 / The MongoDB connection string</param>
    /// <exception cref="ArgumentException">当 connectionString 为空白时抛出 / Thrown when connectionString is blank</exception>
    public MongoDbHealthCheck(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("The MongoDB connection string must not be blank.", nameof(connectionString));
        }

        _mongoClient = GetOrCreateSharedClient(connectionString);
    }

    /// <summary>
    /// 获取或创建进程内共享的健康检查客户端（双检锁；连接串变化时重建）。
    /// </summary>
    /// <remarks>
    /// Gets or creates the process-wide shared health check client (double-checked lock; rebuilt when the connection string changes).
    /// </remarks>
    /// <param name="connectionString">MongoDB 连接串 / The MongoDB connection string</param>
    /// <returns>共享客户端实例 / The shared client instance</returns>
    private static IMongoClient GetOrCreateSharedClient(string connectionString)
    {
        if (_sharedHealthClient != null && string.Equals(_sharedHealthClientConnectionString, connectionString, StringComparison.Ordinal))
        {
            return _sharedHealthClient;
        }

        lock (SharedClientLock)
        {
            if (_sharedHealthClient != null && string.Equals(_sharedHealthClientConnectionString, connectionString, StringComparison.Ordinal))
            {
                return _sharedHealthClient;
            }

            _sharedHealthClient = new MongoClient(connectionString);
            _sharedHealthClientConnectionString = connectionString;
            return _sharedHealthClient;
        }
    }


    /// <summary>
    /// 初始化 MongoDbHealthCheck 的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of MongoDbHealthCheck.
    /// </remarks>
    /// <param name="mongoClient">MongoDB 客户端 / MongoDB client</param>
    public MongoDbHealthCheck(IMongoClient mongoClient)
    {
        _mongoClient = mongoClient;
    }

    /// <summary>
    /// 执行健康检查。
    /// </summary>
    /// <remarks>
    /// Performs the health check.
    /// </remarks>
    /// <param name="context">健康检查上下文 / Health check context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>健康检查结果 / Health check result</returns>
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            var databases = await _mongoClient.ListDatabaseNamesAsync(cancellationToken);

            if (databases != null && await databases.AnyAsync(cancellationToken))
            {
                Interlocked.Exchange(ref _consecutiveFailures, 0);
                return HealthCheckResult.Healthy("MongoDB connection is healthy");
            }

            var currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            if (currentFailures >= ConsecutiveFailureThreshold)
            {
                return HealthCheckResult.Unhealthy($"MongoDB connection is unhealthy after {currentFailures} consecutive failures");
            }

            return HealthCheckResult.Degraded($"MongoDB connection is degraded: no databases found. consecutiveFailures={currentFailures}");
        }
        catch (Exception ex)
        {
            var currentFailures = Interlocked.Increment(ref _consecutiveFailures);
            if (currentFailures >= ConsecutiveFailureThreshold)
            {
                return HealthCheckResult.Unhealthy($"MongoDB connection is unhealthy after {currentFailures} consecutive failures", ex);
            }

            return HealthCheckResult.Degraded($"MongoDB connection is temporarily unavailable. consecutiveFailures={currentFailures}", ex);
        }
    }
}
