// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Diagnostics.Metrics;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo;
using GameFrameX.DataBase.PostgreSql;
using GameFrameX.DataBase.Sqlite;
using Microsoft.Data.Sqlite;
using MongoDB.Driver;
using Npgsql;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// 共享弹性引擎契约矩阵：同一延迟序列 / 异常注入下三家 provider 行为逐项一致
/// （注入假时钟与脚本化宿主，无外部依赖；断言按数据身份核对——重试次数、退避序列、降级读触发、写门控拒绝、三指标计数）。
/// </summary>
/// <remarks>
/// Contract matrix for the shared <c>DbResilienceExecutor</c>: under identical delay
/// sequences and exception injections the three provider configurations behave identically
/// (fake clock + scripted host, no external dependencies; assertions verify data identity —
/// retry counts, backoff sequence, degraded-read trigger, write-gate rejection, metric counts).
/// </remarks>
public sealed class DbResilienceExecutorContractTests
{
    private const string ReadOperationName = "ReadOp";
    private const string WriteOperationName = "WriteOp";

    public static TheoryData<IDbOperationExceptionClassifier> ProviderClassifiers => new()
    {
        MongoDbServiceClassifier(),
        PostgreSqlDbServiceClassifier(),
        SqliteDbServiceClassifier(),
    };

    internal static IDbOperationExceptionClassifier MongoDbServiceClassifier()
    {
        return new MongoDbExceptionClassifier();
    }

    internal static IDbOperationExceptionClassifier PostgreSqlDbServiceClassifier()
    {
        return new PostgreSqlDbExceptionClassifier();
    }

    internal static IDbOperationExceptionClassifier SqliteDbServiceClassifier()
    {
        return new SqliteDbExceptionClassifier();
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public async Task RetryableThenSuccess_ShouldRetryOnceWithExactBackoffAndCountMetrics(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier, readDelays: new[] { 50, 60, 70 }, writeDelays: new[] { 80, 90 }))
        {
            var attempts = 0;

            var result = await harness.Executor.ExecuteReadWithRetryAsync(_ =>
            {
                attempts++;
                return attempts == 1 ? Task.FromException<int>(new TimeoutException("transient-1")) : Task.FromResult(42);
            }, CancellationToken.None, ReadOperationName);

            Assert.Equal(42, result);
            Assert.Equal(2, attempts);
            Assert.Equal(new[] { 50 }, harness.Delays);
            Assert.Single(harness.ValuesOf("db_operation_retry_total"), 1D);
            Assert.Empty(harness.ValuesOf("db_operation_fail_total"));
            Assert.Single(harness.ValuesOf("db_operation_latency_ms"));
            Assert.Equal(1, harness.Host.SuccessRecorded);
            Assert.Equal(1, harness.Host.FailuresRecorded);
            Assert.Single(harness.Warnings, warning => warning.Contains(ReadOperationName) && warning.Contains("1/3"));
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public void NonRetryableFailure_ShouldRethrowImmediatelyWithoutMetricsOrStateChange(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier))
        {
            var original = new ArgumentNullException("op");
            var attempts = 0;

            var thrown = Assert.Throws<ArgumentNullException>(() => harness.Executor.ExecuteReadWithRetryAsync(_ =>
            {
                attempts++;
                return Task.FromException<int>(original);
            }, CancellationToken.None, ReadOperationName).GetAwaiter().GetResult());

            Assert.Same(original, thrown);
            Assert.Equal(1, attempts);
            Assert.Empty(harness.Delays);
            Assert.Empty(harness.Events);
            Assert.Equal(0, harness.Host.SuccessRecorded);
            Assert.Equal(0, harness.Host.FailuresRecorded);
            Assert.Empty(harness.Warnings);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public void ExhaustedRetries_ShouldThrowDatabaseUnavailableWithExactSequenceAndFailureMetrics(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier, readDelays: new[] { 10, 20, 30 }, writeDelays: new[] { 40, 50, 60 }))
        {
            var attempts = 0;

            var thrown = Assert.Throws<DatabaseUnavailableException>(() => harness.Executor.ExecuteReadWithRetryAsync(_ =>
            {
                attempts++;
                return Task.FromException<int>(new TimeoutException("boom-" + attempts));
            }, CancellationToken.None, ReadOperationName).GetAwaiter().GetResult());

            Assert.Equal("retry-failed " + ReadOperationName, thrown.Message);
            var lastFailure = Assert.IsType<TimeoutException>(thrown.InnerException);
            Assert.Equal("boom-4", lastFailure.Message);
            Assert.Equal(4, attempts);
            Assert.Equal(new[] { 10, 20, 30 }, harness.Delays);
            Assert.Equal(3, harness.ValuesOf("db_operation_retry_total").Count);
            Assert.Single(harness.ValuesOf("db_operation_fail_total"), 1D);
            Assert.Single(harness.ValuesOf("db_operation_latency_ms"));
            Assert.Equal(4, harness.Host.FailuresRecorded);
            Assert.Equal(0, harness.Host.SuccessRecorded);
            Assert.Equal(3, harness.Warnings.Count);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public void WriteGateRejection_ShouldFailFastBeforeFirstAttempt(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier))
        {
            harness.Host.AllowWrite = false;
            var attempts = 0;

            var thrown = Assert.Throws<DatabaseUnavailableException>(() => harness.Executor.ExecuteWriteWithRetryAsync(_ =>
            {
                attempts++;
                return Task.FromResult(true);
            }, CancellationToken.None, WriteOperationName, true).GetAwaiter().GetResult());

            Assert.Equal("service-unavailable", thrown.Message);
            Assert.Equal(0, attempts);
            Assert.Empty(harness.Delays);
            Assert.Empty(harness.Events);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public async Task DegradedRead_ShouldReturnFallbackIdentityWithoutTouchingDatabase(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier))
        {
            harness.Host.DegradeReads = true;
            var sentinel = new List<string> { "fallback" };
            var attempts = 0;

            var result = await harness.Executor.ExecuteReadWithRetryAsync(_ =>
            {
                attempts++;
                return Task.FromResult(new List<string> { "database" });
            }, CancellationToken.None, ReadOperationName, () => sentinel);

            Assert.Same(sentinel, result);
            Assert.Equal(0, attempts);
            Assert.Empty(harness.Delays);
            Assert.Empty(harness.Events);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public void NonIdempotentWrite_ShouldAttemptOnceWithoutRetries(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier, readDelays: new[] { 10, 20 }, writeDelays: new[] { 40, 50 }))
        {
            var attempts = 0;

            Assert.Throws<DatabaseUnavailableException>(() => harness.Executor.ExecuteWriteWithRetryAsync(_ =>
            {
                attempts++;
                return Task.FromException<bool>(new TimeoutException("write-once"));
            }, CancellationToken.None, WriteOperationName, false).GetAwaiter().GetResult());

            Assert.Equal(1, attempts);
            Assert.Empty(harness.Delays);
            Assert.Empty(harness.ValuesOf("db_operation_retry_total"));
            Assert.Single(harness.ValuesOf("db_operation_fail_total"), 1D);
            Assert.Equal(1, harness.Host.FailuresRecorded);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public void WriteGateRecheckBetweenAttempts_ShouldSurfaceGateRejectionAfterRetry(IDbOperationExceptionClassifier classifier)
    {
        using (var harness = CreateHarness(classifier, readDelays: new[] { 10, 20 }, writeDelays: new[] { 30, 40 }))
        {
            var attempts = 0;

            var thrown = Assert.Throws<DatabaseUnavailableException>(() => harness.Executor.ExecuteWriteWithRetryAsync(_ =>
            {
                attempts++;
                harness.Host.AllowWrite = false;
                return Task.FromException<bool>(new TimeoutException("then-gated"));
            }, CancellationToken.None, WriteOperationName, true).GetAwaiter().GetResult());

            // 第二次尝试前的门控复检抛出 DatabaseUnavailableException；三家分类器均不将其判为可重试，故原样上抛而非包装。
            Assert.Equal("service-unavailable", thrown.Message);
            Assert.Equal(1, attempts);
            Assert.Equal(new[] { 30 }, harness.Delays);
            Assert.Single(harness.ValuesOf("db_operation_retry_total"), 1D);
        }
    }

    [Theory]
    [MemberData(nameof(ProviderClassifiers))]
    public void TimeoutException_ShouldClassifyRetryableAcrossAllProviders(IDbOperationExceptionClassifier classifier)
    {
        // 「三家一致」的注入根：TimeoutException 是三家分类器共享的可重试异常身份。
        Assert.True(classifier.IsRetryable(new TimeoutException()));
        Assert.Equal("timeout", classifier.GetFailureReason(new TimeoutException()));
    }

    [Fact]
    public void MongoClassifier_ShouldClassifyStatementAndTransactionLevels()
    {
        var classifier = new MongoDbExceptionClassifier();

        var retryableRead = new MongoException("retryable");
        retryableRead.AddErrorLabel("RetryableReadError");
        var retryableWrite = new MongoException("retryable");
        retryableWrite.AddErrorLabel("RetryableWriteError");
        var transientTransaction = new MongoException("tx transient");
        transientTransaction.AddErrorLabel("TransientTransactionError");
        var unknownCommit = new MongoException("commit transient");
        unknownCommit.AddErrorLabel("UnknownTransactionCommitResult");
        Assert.True(classifier.IsRetryable(retryableRead));
        Assert.True(classifier.IsRetryable(retryableWrite));
        Assert.Equal("retryable_read", classifier.GetFailureReason(retryableRead));
        Assert.Equal("retryable_write", classifier.GetFailureReason(retryableWrite));
        Assert.True(classifier.ShouldRetryTransaction(transientTransaction));
        Assert.True(classifier.ShouldRetryTransactionCommit(unknownCommit));
        Assert.False(classifier.IsRetryable(new MongoException("plain")));
        Assert.False(classifier.ShouldRetryTransaction(new MongoException("plain")));
        Assert.False(classifier.ShouldRetryTransactionCommit(new MongoException("plain")));
        Assert.False(classifier.IsRetryable(new DatabaseUnavailableException("x")));
        Assert.Equal("mongo_exception", classifier.GetFailureReason(new MongoException("plain")));
        Assert.Equal("database_unavailable", classifier.GetFailureReason(new DatabaseUnavailableException("x")));
        Assert.Equal("unknown", classifier.GetFailureReason(null));
    }

    [Fact]
    public void PostgreSqlClassifier_ShouldClassifyTransientStatesAndTransactionNarrowSet()
    {
        var classifier = new PostgreSqlDbExceptionClassifier();
        var serializationFailure = new PostgresException("conflict", "ERROR", "ERROR", "40001");
        var uniqueViolation = new PostgresException("dup", "ERROR", "ERROR", "23505");

        Assert.True(classifier.IsRetryable(serializationFailure));
        Assert.True(classifier.ShouldRetryTransaction(serializationFailure));
        Assert.True(classifier.ShouldRetryTransactionCommit(serializationFailure));
        Assert.False(classifier.IsRetryable(uniqueViolation));
        Assert.False(classifier.ShouldRetryTransaction(uniqueViolation));
        Assert.Equal("postgres_40001", classifier.GetFailureReason(serializationFailure));
        Assert.Equal("postgres_23505", classifier.GetFailureReason(uniqueViolation));
    }

    [Fact]
    public void SqliteClassifier_ShouldClassifyBusyLockedAndBoundedSet()
    {
        var classifier = new SqliteDbExceptionClassifier();
        var busy = new SqliteException("database is locked", 5);
        var locked = new SqliteException("table is locked", 6);
        var constraint = new SqliteException("constraint failed", 19);

        Assert.True(classifier.IsRetryable(busy));
        Assert.True(classifier.ShouldRetryTransaction(busy));
        Assert.True(classifier.ShouldRetryTransactionCommit(busy));
        Assert.True(classifier.IsRetryable(locked));
        Assert.False(classifier.IsRetryable(constraint));
        Assert.False(classifier.ShouldRetryTransaction(new TimeoutException()));
        Assert.Equal("busy", classifier.GetFailureReason(busy));
        Assert.Equal("locked", classifier.GetFailureReason(locked));
        Assert.Equal("sqlite_19", classifier.GetFailureReason(constraint));
    }

    [Fact]
    public void AlwaysHealthyHost_ShouldNeverDegradeOrGate()
    {
        var host = DbResilienceHosts.AlwaysHealthy;

        Assert.False(host.TryReturnDegradedReadFallback<int>("FindListAsync", () => 7, out var fallback));
        Assert.Equal(0, fallback);
        Assert.True(host.IsCoreWriteAllowed("AddAsync"));
        host.RecordOperationSuccess("AddAsync", "write");
        host.RecordRetryableFailure("AddAsync", "write", new TimeoutException());
    }

    private static Harness CreateHarness(IDbOperationExceptionClassifier classifier, int[] readDelays = null, int[] writeDelays = null)
    {
        return new Harness(classifier, readDelays ?? new[] { 120, 300, 700 }, writeDelays ?? new[] { 150, 400, 900 });
    }

    private sealed class Harness : IDisposable
    {
        public readonly ScriptedResilienceHost Host = new();
        public readonly List<int> Delays = new();
        public readonly List<string> Warnings = new();
        public readonly List<(string Instrument, double Value)> Events = new();
        private readonly Meter _meter;
        private readonly MeterListener _listener;

        public Harness(IDbOperationExceptionClassifier classifier, int[] readDelays, int[] writeDelays)
        {
            _meter = new Meter("GameFrameX.Tests." + Guid.NewGuid().ToString("N"));
            var retryTotal = _meter.CreateCounter<long>("db_operation_retry_total");
            var failTotal = _meter.CreateCounter<long>("db_operation_fail_total");
            var latency = _meter.CreateHistogram<double>("db_operation_latency_ms", unit: "ms");
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (ReferenceEquals(instrument.Meter, _meter))
                    {
                        listener.EnableMeasurementEvents(instrument);
                    }
                },
            };
            _listener.SetMeasurementEventCallback<long>((instrument, value, tags, state) => Events.Add((instrument.Name, value)));
            _listener.SetMeasurementEventCallback<double>((instrument, value, tags, state) => Events.Add((instrument.Name, value)));
            _listener.Start();
            Executor = new DbResilienceExecutor(new DbResilienceExecutorOptions
            {
                Classifier = classifier,
                Host = Host,
                OperationRetryTotal = retryTotal,
                OperationFailTotal = failTotal,
                OperationLatencyMilliseconds = latency,
                GetReadRetryDelaysMilliseconds = () => readDelays,
                GetIdempotentWriteRetryDelaysMilliseconds = () => writeDelays,
                GetServiceUnavailableMessage = () => "service-unavailable",
                GetTransientErrorMessage = (operationName, attempt, retries, error) => $"transient {operationName} {attempt}/{retries} {error}",
                GetRetryFailedMessage = operationName => "retry-failed " + operationName,
                LogWarning = Warnings.Add,
                DelayAsync = (milliseconds, _) =>
                {
                    Delays.Add(milliseconds);
                    return Task.CompletedTask;
                },
                NextJitter = () => 0,
            });
        }

        public DbResilienceExecutor Executor { get; }

        public IReadOnlyList<double> ValuesOf(string instrumentName)
        {
            return Events.Where(e => e.Instrument == instrumentName).Select(e => e.Value).ToList();
        }

        public void Dispose()
        {
            _listener.Dispose();
            _meter.Dispose();
        }
    }

    /// <summary>
    /// 脚本化可用性宿主：矩阵层以相同 hook 响应驱动三家引擎配置（生产上 Mongo/PG 为真状态机、Sqlite 恒 Healthy——边界④）。
    /// </summary>
    private sealed class ScriptedResilienceHost : IDbResilienceHost
    {
        public bool AllowWrite = true;
        public bool DegradeReads;
        public int SuccessRecorded;
        public int FailuresRecorded;

        public bool TryReturnDegradedReadFallback<T>(string operationName, Func<T> fallbackValueFactory, out T fallbackValue)
        {
            if (!DegradeReads)
            {
                fallbackValue = default;
                return false;
            }

            fallbackValue = fallbackValueFactory != null ? fallbackValueFactory() : default;
            return true;
        }

        public bool IsCoreWriteAllowed(string operationName)
        {
            return AllowWrite;
        }

        public void RecordOperationSuccess(string operationName, string operationType)
        {
            SuccessRecorded++;
        }

        public void RecordRetryableFailure(string operationName, string operationType, Exception exception)
        {
            FailuresRecorded++;
        }
    }
}
