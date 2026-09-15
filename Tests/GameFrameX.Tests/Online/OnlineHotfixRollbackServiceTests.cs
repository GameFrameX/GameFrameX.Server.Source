// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Audit;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.HotfixRollback;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Hotfix 回滚服务测试（C106 / vault:C9 S8.5 · VC-8.9 服务端半边：协议兼容检查三类差异判定
    /// （Removed/Changed 不兼容拒绝、Added 兼容入报告）、回滚成功全链路（版本切换 + 审计 Domain=Operation + 事件）、
    /// 幂等重放同构（IsReplay、不重复执行、审计不重复落档）、目标未登记 4002 / 无活跃版本 5001 /
    /// 目标=当前 5003 / 协议不兼容 6002 且版本未切换 / 执行器失败 8002 且幂等键落 Fail 可重试 /
    /// 审计失败 1001 无审计不执行 / 参数缺失逐维 4001 / 只读预检无副作用 / 作用域隔离与登记簿记幂等）。
    /// </summary>
    public class OnlineHotfixRollbackServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 9201;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 9202;

        /// <summary>测试用其它 App 标识（隔离用例）。</summary>
        private const long OtherAppId = 9902;

        /// <summary>
        /// 步进时钟（Foundation 协调器依赖 IClock；每次读取前进 10ms，
        /// 保证单测试周期内记录远不过期）。
        /// </summary>
        private sealed class StepClock : IClock
        {
            private long _nowMilliseconds = 1760000000000;

            public long UtcNowTime
            {
                get
                {
                    _nowMilliseconds += 10;
                    return _nowMilliseconds;
                }
            }
        }

        /// <summary>
        /// 计数回滚执行器（记录调用与目标版本；可注入失败模式）。
        /// </summary>
        private sealed class CountingExecutor : IOnlineHotfixRollbackExecutor
        {
            public int CallCount
            {
                get;
                private set;
            }

            public List<string> TargetVersions
            {
                get;
            } = new List<string>();

            public string FailMessage
            {
                get;
                set;
            }

            public Task RollbackAsync(string targetVersion, CancellationToken cancellationToken = default)
            {
                CallCount++;
                TargetVersions.Add(targetVersion);
                if (!string.IsNullOrEmpty(FailMessage))
                {
                    throw new InvalidOperationException(FailMessage);
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 抛异常审计存储（验证「无审计不执行」：审计链路异常时拒绝回滚且切换未发生）。
        /// </summary>
        private sealed class ThrowingAuditStore : IOnlineAuditStore
        {
            public Task<bool> AppendAsync(OnlineAuditRecord record, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("audit store down");
            }

            public Task<IReadOnlyList<OnlineAuditRecord>> ListAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("audit store down");
            }
        }

        /// <summary>
        /// 被测组件集合（服务 + 各记录桩引用，供断言副作用）。
        /// </summary>
        private sealed class Harness
        {
            public OnlineHotfixRollbackService Service
            {
                get;
            }

            public InMemoryOnlineHotfixVersionStore Store
            {
                get;
            } = new InMemoryOnlineHotfixVersionStore();

            public OnlineAuditService AuditService
            {
                get;
            }

            public CountingExecutor Executor
            {
                get;
            } = new CountingExecutor();

            public OnlineEventRecorder EventRecorder
            {
                get;
            } = new OnlineEventRecorder();

            public Harness(OnlineAuditService auditService, bool withoutExecutor = false)
            {
                AuditService = auditService;
                var options = new IdempotencyOptions
                {
                    ConcurrentWaitTimeoutMilliseconds = 1,
                    // 回滚命令面向可重试的瞬时失败（执行器 8002）：失败记录允许同键重新执行（装配面口径）。
                    FailedReplayPolicy = FailedReplayPolicy.ReExecute,
                };
                var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
                var idempotencyService = new OnlineIdempotencyService(coordinator, null);
                Service = new OnlineHotfixRollbackService(Store, idempotencyService, auditService, withoutExecutor ? null : Executor, EventRecorder);
            }
        }

        /// <summary>
        /// 构造一份版本协议清单。
        /// </summary>
        /// <param name="version">版本号。</param>
        /// <param name="messages">协议消息契约行。</param>
        /// <returns>版本清单。</returns>
        private static OnlineHotfixProtocolManifest CreateManifest(string version, params OnlineHotfixProtocolMessage[] messages)
        {
            return new OnlineHotfixProtocolManifest
            {
                Version = version,
                TenantId = TenantId,
                AppId = AppId,
                Messages = messages.ToList(),
                RegisteredTime = 1_760_000_000_000L,
            };
        }

        /// <summary>
        /// 构造一条协议消息契约行。
        /// </summary>
        /// <param name="name">消息名。</param>
        /// <param name="id">消息号。</param>
        /// <returns>契约行。</returns>
        private static OnlineHotfixProtocolMessage Msg(string name, int id)
        {
            return new OnlineHotfixProtocolMessage { MessageName = name, MessageId = id };
        }

        /// <summary>
        /// 构造一条回滚命令请求。
        /// </summary>
        /// <param name="targetVersion">目标版本。</param>
        /// <param name="idempotencyKey">命令幂等键。</param>
        /// <returns>回滚命令请求。</returns>
        private static OnlineHotfixRollbackRequest CreateRollbackRequest(string targetVersion, string idempotencyKey)
        {
            return new OnlineHotfixRollbackRequest
            {
                Scope = new OnlineScope(TenantId, AppId, 0),
                TargetVersion = targetVersion,
                OperatorId = "admin-001",
                OperatorName = "运维一号",
                Reason = "线上协议异常，回滚到稳定版本",
                IdempotencyKey = idempotencyKey,
            };
        }

        /// <summary>
        /// 登记并激活基线：v1={A,B} 为初始活跃，v2={A,B}（热修逻辑未动协议面）为待回滚离开的版本并激活。
        /// </summary>
        /// <param name="harness">被测组件。</param>
        private static async Task SetupActiveV2Async(Harness harness)
        {
            await harness.Service.RegisterAsync(CreateManifest("1.0.0", Msg("LoginReq", 1), Msg("LoginRes", 2)));
            await harness.Service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), "1.0.0");
            await harness.Service.RegisterAsync(CreateManifest("1.1.0", Msg("LoginReq", 1), Msg("LoginRes", 2)));
            await harness.Service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), "1.1.0");
        }

        /// <summary>
        /// 查询当前作用域已落档的回滚审计记录数。
        /// </summary>
        /// <param name="harness">被测组件。</param>
        /// <returns>审计记录数。</returns>
        private static async Task<int> CountAuditRecordsAsync(Harness harness)
        {
            var page = await harness.AuditService.QueryAsync(new OnlineScope(TenantId, AppId, 0));
            return page.Data.Records.Count;
        }

        // ==================== 协议兼容检查（纯函数，三类差异判定） ====================

        /// <summary>
        /// Removed 差异（当前有、目标无）判不兼容：回滚丢失既有协议面必须被拒绝。
        /// </summary>
        [Fact]
        public void Check_CurrentHasExtraMessage_IncompatibleWithRemovedIssue()
        {
            var current = CreateManifest("1.1.0", Msg("LoginReq", 1), Msg("MatchReq", 5));
            var target = CreateManifest("1.0.0", Msg("LoginReq", 1));

            var report = OnlineProtocolCompatibilityChecker.Check(current, target);

            Assert.False(report.IsCompatible);
            Assert.Equal(1, report.RemovedCount);
            var issue = Assert.Single(report.Issues);
            Assert.Equal(OnlineProtocolCompatibilityIssue.IssueKind.RemovedMessage, issue.Kind);
            Assert.Equal("MatchReq", issue.MessageName);
        }

        /// <summary>
        /// Changed 差异（同名不同消息号）判不兼容：路由错乱必须被拒绝。
        /// </summary>
        [Fact]
        public void Check_SameNameDifferentId_IncompatibleWithChangedIssue()
        {
            var current = CreateManifest("1.1.0", Msg("LoginReq", 9));
            var target = CreateManifest("1.0.0", Msg("LoginReq", 1));

            var report = OnlineProtocolCompatibilityChecker.Check(current, target);

            Assert.False(report.IsCompatible);
            Assert.Equal(1, report.ChangedCount);
            Assert.Equal(OnlineProtocolCompatibilityIssue.IssueKind.ChangedMessage, Assert.Single(report.Issues).Kind);
        }

        /// <summary>
        /// Changed 差异（同消息号不同名）判不兼容。
        /// </summary>
        [Fact]
        public void Check_SameIdDifferentName_IncompatibleWithChangedIssue()
        {
            var current = CreateManifest("1.1.0", Msg("LoginReqV2", 1));
            var target = CreateManifest("1.0.0", Msg("LoginReq", 1));

            var report = OnlineProtocolCompatibilityChecker.Check(current, target);

            Assert.False(report.IsCompatible);
            Assert.Equal(1, report.ChangedCount);
            var issue = Assert.Single(report.Issues);
            Assert.Equal(OnlineProtocolCompatibilityIssue.IssueKind.ChangedMessage, issue.Kind);
            Assert.Equal("LoginReqV2", issue.MessageName);
        }

        /// <summary>
        /// Added 差异（目标独有）判兼容：回滚恢复旧处理器无害，差异入报告提示。
        /// </summary>
        [Fact]
        public void Check_TargetHasExtraMessage_CompatibleWithAddedIssue()
        {
            var current = CreateManifest("1.1.0", Msg("LoginReq", 1));
            var target = CreateManifest("1.0.0", Msg("LoginReq", 1), Msg("ChatReq", 7));

            var report = OnlineProtocolCompatibilityChecker.Check(current, target);

            Assert.True(report.IsCompatible);
            Assert.Equal(1, report.AddedCount);
            Assert.Equal(OnlineProtocolCompatibilityIssue.IssueKind.AddedMessage, Assert.Single(report.Issues).Kind);
        }

        /// <summary>
        /// 协议面一致（热修仅改内部逻辑）判兼容且零差异——回滚常态用例。
        /// </summary>
        [Fact]
        public void Check_IdenticalContracts_CompatibleWithNoIssues()
        {
            var current = CreateManifest("1.1.0", Msg("LoginReq", 1), Msg("LoginRes", 2));
            var target = CreateManifest("1.0.0", Msg("LoginReq", 1), Msg("LoginRes", 2));

            var report = OnlineProtocolCompatibilityChecker.Check(current, target);

            Assert.True(report.IsCompatible);
            Assert.Empty(report.Issues);
            Assert.Equal(0, report.RemovedCount + report.ChangedCount + report.AddedCount);
        }

        // ==================== 回滚命令全链路 ====================

        /// <summary>
        /// 回滚成功全链路：执行器切版本、活跃指针切换、审计落档 Domain=Operation 且含操作者/原因、事件发布。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_CompatibleTarget_SwitchesVersionAuditsAndPublishesEvent()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-001"));

            Assert.True(result.IsSuccess);
            Assert.False(result.Data.IsReplay);
            Assert.Equal("1.0.0", result.Data.TargetVersion);
            Assert.Equal("1.1.0", result.Data.PreviousVersion);
            Assert.Contains("Removed=0", result.Data.CompatibilitySummary);
            Assert.Equal(1, harness.Executor.CallCount);
            Assert.Equal("1.0.0", harness.Executor.TargetVersions[0]);

            var active = await harness.Store.FindActiveAsync(TenantId, AppId);
            Assert.Equal("1.0.0", active.Version);

            var page = await harness.AuditService.QueryAsync(new OnlineScope(TenantId, AppId, 0));
            var record = Assert.Single(page.Data.Records);
            Assert.Equal(OnlineAuditDomain.Operation, record.Domain);
            Assert.Equal("hotfix-rollback-rb-key-001", record.EventId);
            Assert.Equal("admin-001", record.OperatorId);
            Assert.Equal("线上协议异常，回滚到稳定版本", record.Reason);

            var published = harness.EventRecorder.Filter(OnlineHotfixEvents.RolledBack);
            var rolledBack = Assert.Single(published);
            Assert.Equal("online-hotfix", rolledBack.Source);
            Assert.Equal("1.0.0", rolledBack.CorrelationId);
        }

        /// <summary>
        /// 幂等重放同构：重复命令 IsReplay=true、不重复执行、审计不重复落档、回执同构。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_DuplicateCommand_ReplaysFirstResultWithoutSideEffects()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);

            var first = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-002"));
            Assert.True(first.IsSuccess);

            var replay = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-002"));

            Assert.True(replay.IsSuccess);
            Assert.True(replay.Data.IsReplay);
            Assert.Equal(first.Data.TargetVersion, replay.Data.TargetVersion);
            Assert.Equal(first.Data.PreviousVersion, replay.Data.PreviousVersion);
            Assert.Equal(first.Data.CompatibilitySummary, replay.Data.CompatibilitySummary);
            Assert.Equal(1, harness.Executor.CallCount);
            Assert.Equal(1, await CountAuditRecordsAsync(harness));
        }

        /// <summary>
        /// 相同幂等键承载不同意图（目标版本不同）判 6002 冲突。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_SameKeyDifferentIntent_RejectedAsConflict()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);
            await harness.Service.RegisterAsync(CreateManifest("0.9.0", Msg("LoginReq", 1), Msg("LoginRes", 2), Msg("LegacyReq", 3)));

            var first = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-003"));
            Assert.True(first.IsSuccess);

            var conflict = await harness.Service.RollbackAsync(CreateRollbackRequest("0.9.0", "rb-key-003"));

            Assert.False(conflict.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, conflict.Code);
        }

        /// <summary>
        /// 目标版本未登记清单 → 4002，且活跃版本未变、审计零落档。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_UnregisteredTarget_RejectedWith404()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("0.1.0", "rb-key-004"));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, result.Code);
            Assert.Equal(0, harness.Executor.CallCount);
            Assert.Equal(0, await CountAuditRecordsAsync(harness));
            Assert.Equal("1.1.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);
        }

        /// <summary>
        /// 从未激活任何版本 → 5001 业务状态未准备。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_NoActiveVersion_RejectedWithStateNotReady()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await harness.Service.RegisterAsync(CreateManifest("1.0.0", Msg("LoginReq", 1)));

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-005"));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, result.Code);
            Assert.Equal(0, harness.Executor.CallCount);
        }

        /// <summary>
        /// 目标即当前活跃版本 → 5003 状态机不允许（无意义的自反回滚）。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_TargetEqualsCurrent_RejectedWithStateForbidden()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("1.1.0", "rb-key-006"));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, result.Code);
            Assert.Equal(0, harness.Executor.CallCount);
            Assert.Equal(0, await CountAuditRecordsAsync(harness));
        }

        /// <summary>
        /// 协议不兼容（当前有目标无）→ 6002 拒绝且版本未切换、执行器未触达（兼容闸门先行于执行器）。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_IncompatibleTarget_RejectedAndVersionUnchanged()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await harness.Service.RegisterAsync(CreateManifest("1.0.0", Msg("LoginReq", 1)));
            await harness.Service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), "1.0.0");
            await harness.Service.RegisterAsync(CreateManifest("1.1.0", Msg("LoginReq", 1), Msg("MatchReq", 5)));
            await harness.Service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), "1.1.0");

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-007"));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.VersionConflict, result.Code);
            Assert.Contains("Removed=1", result.Message);
            Assert.Equal(0, harness.Executor.CallCount);
            Assert.Equal("1.1.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);
        }

        /// <summary>
        /// 执行器失败 → 8002 且幂等键落定 Fail（同键重试可重新执行并成功——失败不占死幂等占位）。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_ExecutorFails_RejectedAndIdempotencyReleased()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);
            harness.Executor.FailMessage = "dll load failure";

            var failed = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-008"));

            Assert.False(failed.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, failed.Code);
            Assert.Equal("1.1.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);

            harness.Executor.FailMessage = null;
            var retried = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-008"));
            Assert.True(retried.IsSuccess);
            Assert.False(retried.Data.IsReplay);
            Assert.Equal("1.0.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);
        }

        /// <summary>
        /// 执行器未装配（null）→ 8002 拒绝（装配面 X4 必须注入执行器才能回滚）。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_ExecutorMissing_RejectedWithDependencyUnavailable()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()), withoutExecutor: true);
            await SetupActiveV2Async(harness);

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-009"));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.DependencyUnavailable, result.Code);
            Assert.Equal("1.1.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);
        }

        /// <summary>
        /// 审计链路异常 → 1001 拒绝且切换未发生（「无审计不执行」——审计先行先于执行器）。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_AuditIngestFails_RejectedWithoutExecution()
        {
            var harness = new Harness(new OnlineAuditService(new ThrowingAuditStore()));
            await SetupActiveV2Async(harness);

            var result = await harness.Service.RollbackAsync(CreateRollbackRequest("1.0.0", "rb-key-010"));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.InternalError, result.Code);
            Assert.Contains("无审计不执行", result.Message);
            Assert.Equal(0, harness.Executor.CallCount);
            Assert.Equal("1.1.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);
        }

        // ==================== 参数校验（4001 宁拒毋缺） ====================

        /// <summary>
        /// 操作者 / 原因 / 目标版本 / 幂等键逐维缺失或非法 → 4001（宁拒毋缺）。
        /// </summary>
        [Theory]
        [InlineData("operator")]
        [InlineData("reason")]
        [InlineData("version")]
        [InlineData("idempotencyKey")]
        [InlineData("badKeyFormat")]
        [InlineData("versionWithPipe")]
        public async Task RollbackAsync_MissingOrInvalidDimension_RejectedWithParameterInvalid(string brokenDimension)
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);
            var request = CreateRollbackRequest("1.0.0", "rb-key-011");
            switch (brokenDimension)
            {
                case "operator":
                    request.OperatorId = string.Empty;
                    break;
                case "reason":
                    request.Reason = string.Empty;
                    break;
                case "version":
                    request.TargetVersion = string.Empty;
                    break;
                case "idempotencyKey":
                    request.IdempotencyKey = string.Empty;
                    break;
                case "badKeyFormat":
                    request.IdempotencyKey = "bad key";
                    break;
                case "versionWithPipe":
                    request.TargetVersion = "1.0|backdoor";
                    break;
                default:
                    break;
            }

            var result = await harness.Service.RollbackAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
            Assert.Equal(0, harness.Executor.CallCount);
            Assert.Equal(0, await CountAuditRecordsAsync(harness));
        }

        // ==================== 只读预检与登记 / 激活簿记 ====================

        /// <summary>
        /// 只读预检：产出兼容报告且无任何副作用（不落审计、不触执行器、活跃版本不变）——供回滚演练 Runbook 调用。
        /// </summary>
        [Fact]
        public async Task CheckProtocolCompatibilityAsync_ReturnsReportWithoutSideEffects()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);
            // 预检目标取超集版本（Added 兼容差异）：当前 1.1.0={LoginReq,LoginRes}，目标 2.0.0 多一条 ChatReq。
            await harness.Service.RegisterAsync(CreateManifest("2.0.0", Msg("LoginReq", 1), Msg("LoginRes", 2), Msg("ChatReq", 7)));

            var report = await harness.Service.CheckProtocolCompatibilityAsync(new OnlineScope(TenantId, AppId, 0), "2.0.0");

            Assert.True(report.IsSuccess);
            Assert.True(report.Data.IsCompatible);
            Assert.Equal(1, report.Data.AddedCount);
            Assert.Equal(0, harness.Executor.CallCount);
            Assert.Equal(0, await CountAuditRecordsAsync(harness));
            Assert.Empty(harness.EventRecorder.Filter(OnlineHotfixEvents.RolledBack));
            Assert.Equal("1.1.0", (await harness.Store.FindActiveAsync(TenantId, AppId)).Version);
        }

        /// <summary>
        /// 登记簿记幂等：同 (作用域, 版本号) 重复登记返回 false 且不覆盖既有清单、不重复发登记事件。
        /// </summary>
        [Fact]
        public async Task RegisterAsync_DuplicateVersion_IdempotentReceiptWithoutOverwrite()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            var first = await harness.Service.RegisterAsync(CreateManifest("1.0.0", Msg("LoginReq", 1)));
            Assert.True(first.IsSuccess);
            Assert.True(first.Data);

            var second = await harness.Service.RegisterAsync(CreateManifest("1.0.0", Msg("HackedReq", 99)));

            Assert.True(second.IsSuccess);
            Assert.False(second.Data);
            var stored = await harness.Store.FindAsync(TenantId, AppId, "1.0.0");
            Assert.Equal(1, stored.Messages.Count);
            Assert.Equal(1, harness.EventRecorder.Filter(OnlineHotfixEvents.VersionRegistered).Count);
        }

        /// <summary>
        /// 清单内消息名重复 → 4001（契约行唯一性是兼容判定的前提）。
        /// </summary>
        [Fact]
        public async Task RegisterAsync_DuplicateMessageName_RejectedWithParameterInvalid()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));

            var result = await harness.Service.RegisterAsync(CreateManifest("1.0.0", Msg("LoginReq", 1), Msg("LoginReq", 2)));

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }

        /// <summary>
        /// 激活未登记版本 → 4002。
        /// </summary>
        [Fact]
        public async Task ActivateAsync_UnregisteredVersion_RejectedWith404()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));

            var result = await harness.Service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), "404.0.0");

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, result.Code);
        }

        /// <summary>
        /// 作用域隔离：清单是 App 级资产，其它 App 查不到本 App 登记的版本（4002 反预言）。
        /// </summary>
        [Fact]
        public async Task RollbackAsync_CrossAppVersionInvisible_RejectedWith404()
        {
            var harness = new Harness(new OnlineAuditService(new InMemoryOnlineAuditStore()));
            await SetupActiveV2Async(harness);
            var request = CreateRollbackRequest("1.0.0", "rb-key-012");
            request.Scope = new OnlineScope(TenantId, OtherAppId, 0);

            var result = await harness.Service.RollbackAsync(request);

            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, result.Code);
            Assert.Equal(0, harness.Executor.CallCount);
        }
    }
}
