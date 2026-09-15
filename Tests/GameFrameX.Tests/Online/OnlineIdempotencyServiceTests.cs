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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// OnlineIdempotencyService 幂等 Server 侧组装测试（Foundation InMemory 存储；
    /// 相同键相同请求回放首次结果 / 相同键不同请求 6xxx 冲突 / 并发占位 8xxx 忙，VC-1.3/1.4/1.5）。
    /// </summary>
    public class OnlineIdempotencyServiceTests
    {
        /// <summary>
        /// 步进时钟（Foundation 协调器依赖 IClock；每次读取前进 10ms，
        /// 既保证并发等待能按超时退出，又保证单测试周期内记录远不过期）。
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
        /// 审计收集出口（验证每次判定都留痕）。
        /// </summary>
        private sealed class CollectingAuditSink : IOnlineIdempotencyAuditSink
        {
            public List<OnlineIdempotencyAuditRecord> Records
            {
                get;
            } = new List<OnlineIdempotencyAuditRecord>();

            public Task RecordAsync(OnlineIdempotencyAuditRecord record, CancellationToken cancellationToken)
            {
                Records.Add(record);
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 构造被测服务（InMemory 存储 + 并发等待超时 1ms，保证 Busy 分支快速返回）。
        /// </summary>
        private static OnlineIdempotencyService CreateService(CollectingAuditSink auditSink)
        {
            var options = new IdempotencyOptions
            {
                ConcurrentWaitTimeoutMilliseconds = 1,
            };
            var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
            return new OnlineIdempotencyService(coordinator, auditSink);
        }

        /// <summary>
        /// 验证非法幂等键被判 InvalidKey 并映射参数错误，且留审计（不触达存储）。
        /// </summary>
        [Theory]
        [InlineData("bad key")]
        [InlineData("")]
        public async Task BeginAsync_WithInvalidKey_ShouldReturnInvalidOutcome(string idempotencyKey)
        {
            // Arrange
            var auditSink = new CollectingAuditSink();
            var service = CreateService(auditSink);
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var outcome = await service.BeginAsync(scope, idempotencyKey, "{\"amount\":1}", true);

            // Assert
            Assert.Equal(OnlineIdempotencyOutcomeKind.InvalidKey, outcome.Kind);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.ErrorCode);
            Assert.False(outcome.CanExecute);
            Assert.Single(auditSink.Records);
            Assert.Equal(OnlineIdempotencyOutcomeKind.InvalidKey, auditSink.Records[0].OutcomeKind);
            Assert.Equal("online:1:10:100:10001", auditSink.Records[0].ScopeKey);
        }

        /// <summary>
        /// 验证首次请求获得 Execute 判定（允许执行业务）。
        /// </summary>
        [Fact]
        public async Task BeginAsync_FirstRequest_ShouldReturnExecute()
        {
            // Arrange
            var service = CreateService(new CollectingAuditSink());
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var outcome = await service.BeginAsync(scope, "order-create-001", "{\"amount\":1}", true);

            // Assert
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, outcome.Kind);
            Assert.Equal(OnlineErrorCode.None, outcome.ErrorCode);
            Assert.True(outcome.CanExecute);
            Assert.Equal("online:1:10:100:10001", outcome.ScopeKey);
            Assert.Equal("order-create-001", outcome.IdempotencyKey);
        }

        /// <summary>
        /// 验证相同键相同请求在完成后回放首次响应（透明回放非错误，VC-1.3 回放半边）。
        /// </summary>
        [Fact]
        public async Task BeginAsync_AfterComplete_WithSameKeyAndRequest_ShouldReplayFirstResponse()
        {
            // Arrange
            var service = CreateService(new CollectingAuditSink());
            var scope = new OnlineScope(1, 10, 100, 10001);
            var firstResponse = new byte[] { 1, 2, 3, 4 };

            // Act
            var first = await service.BeginAsync(scope, "order-create-002", "{\"amount\":2}", true);
            await service.CompleteAsync(scope, "order-create-002", firstResponse, true);
            var second = await service.BeginAsync(scope, "order-create-002", "{\"amount\":2}", true);

            // Assert
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, first.Kind);
            Assert.Equal(OnlineIdempotencyOutcomeKind.Replay, second.Kind);
            Assert.Equal(OnlineErrorCode.None, second.ErrorCode);
            Assert.False(second.CanExecute);
            Assert.Equal(firstResponse, second.FirstResponse.ToArray());
        }

        /// <summary>
        /// 验证相同键承载不同业务意图被判冲突并映射 6xxx（VC-1.4 冲突半边）。
        /// </summary>
        [Fact]
        public async Task BeginAsync_WithSameKeyButDifferentRequest_ShouldReturnConflict()
        {
            // Arrange
            var service = CreateService(new CollectingAuditSink());
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var first = await service.BeginAsync(scope, "order-create-003", "{\"amount\":3}", true);
            await service.CompleteAsync(scope, "order-create-003", new byte[] { 9 }, true);
            var conflicting = await service.BeginAsync(scope, "order-create-003", "{\"amount\":999}", true);

            // Assert
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, first.Kind);
            Assert.Equal(OnlineIdempotencyOutcomeKind.Conflict, conflicting.Kind);
            Assert.Equal(OnlineErrorCode.VersionConflict, conflicting.ErrorCode);
            Assert.False(conflicting.CanExecute);
        }

        /// <summary>
        /// 验证并发占位未落定时相同键相同请求被判忙并映射 8xxx（可安全重试，VC-1.3 并发半边）。
        /// </summary>
        [Fact]
        public async Task BeginAsync_WhileProcessing_ShouldReturnBusy()
        {
            // Arrange
            var service = CreateService(new CollectingAuditSink());
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var first = await service.BeginAsync(scope, "order-create-004", "{\"amount\":4}", true);
            var concurrent = await service.BeginAsync(scope, "order-create-004", "{\"amount\":4}", true);

            // Assert
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, first.Kind);
            Assert.Equal(OnlineIdempotencyOutcomeKind.Busy, concurrent.Kind);
            Assert.Equal(OnlineErrorCode.ServiceBusy, concurrent.ErrorCode);
            Assert.False(concurrent.CanExecute);
        }

        /// <summary>
        /// 验证玩家位绑定：不同玩家以相同键互相隔离（幂等键禁止跨玩家复用）。
        /// </summary>
        [Fact]
        public async Task BeginAsync_WithDifferentPlayers_ShouldIsolateRecords()
        {
            // Arrange
            var service = CreateService(new CollectingAuditSink());
            var playerA = new OnlineScope(1, 10, 100, 10001);
            var playerB = new OnlineScope(1, 10, 100, 10002);

            // Act
            var outcomeA = await service.BeginAsync(playerA, "order-create-005", "{\"amount\":5}", true);
            var outcomeB = await service.BeginAsync(playerB, "order-create-005", "{\"amount\":5}", true);

            // Assert
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, outcomeA.Kind);
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, outcomeB.Kind);
            Assert.Equal("online:1:10:100:10001", outcomeA.ScopeKey);
            Assert.Equal("online:1:10:100:10002", outcomeB.ScopeKey);
        }

        /// <summary>
        /// 验证每次判定（含 Execute/Replay）都写审计留痕。
        /// </summary>
        [Fact]
        public async Task BeginAsync_ShouldWriteAuditForEachDecision()
        {
            // Arrange
            var auditSink = new CollectingAuditSink();
            var service = CreateService(auditSink);
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            await service.BeginAsync(scope, "order-create-006", "{\"amount\":6}", true);
            await service.CompleteAsync(scope, "order-create-006", new byte[] { 6 }, true);
            await service.BeginAsync(scope, "order-create-006", "{\"amount\":6}", true);

            // Assert
            Assert.Equal(2, auditSink.Records.Count);
            Assert.Equal(OnlineIdempotencyOutcomeKind.Execute, auditSink.Records[0].OutcomeKind);
            Assert.Equal(OnlineIdempotencyOutcomeKind.Replay, auditSink.Records[1].OutcomeKind);
            Assert.Equal("order-create-006", auditSink.Records[0].IdempotencyKey);
            Assert.False(string.IsNullOrEmpty(auditSink.Records[0].RequestDigest));
        }
    }
}
