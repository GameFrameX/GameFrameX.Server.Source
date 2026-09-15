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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Online.Assets;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Idempotency;
using GameFrameX.Online.Scope;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 资产查询服务测试（vault:C4 VC-3.14：余额/库存/流水分页/交易反查；
    /// 跨作用域查询与不存在同构 ResourceNotFound；游标与页距参数校验）。
    /// </summary>
    public class OnlineAssetQueryServiceTests
    {
        /// <summary>
        /// 步进时钟。
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
        /// 空事件出口。
        /// </summary>
        private sealed class NullPublisher : IOnlineEventPublisher
        {
            public Task PublishAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default)
            {
                return Task.CompletedTask;
            }
        }

        /// <summary>
        /// 测试基座。
        /// </summary>
        private sealed class Harness
        {
            public InMemoryOnlineAssetStore AssetStore
            {
                get;
            } = new InMemoryOnlineAssetStore();

            public InMemoryOnlineAssetTransactionStore TransactionStore
            {
                get;
            } = new InMemoryOnlineAssetTransactionStore();

            public OnlineGrantService CreateGrantService()
            {
                var options = new IdempotencyOptions
                {
                    ConcurrentWaitTimeoutMilliseconds = 1,
                };
                var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
                return new OnlineGrantService(AssetStore, TransactionStore, new OnlineIdempotencyService(coordinator, null), new NullPublisher(), null);
            }

            public OnlineAssetQueryService CreateQueryService()
            {
                return new OnlineAssetQueryService(AssetStore, TransactionStore);
            }
        }

        /// <summary>
        /// 验证发放后余额与库存查询正确；未发生变更返回 0 占位。
        /// </summary>
        [Fact]
        public async Task GetWalletAndInventory_AfterGrant_ReturnsSnapshots()
        {
            // Arrange
            var harness = new Harness();
            var scope = new OnlineScope(1, 10, 100, 10001);
            await harness.CreateGrantService().ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.MailAttachment, OnlineGrantOperation.Grant, "邮件附件", "bo-q1", null, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 100), new OnlineAssetChangeLine(OnlineAssetKind.Item, "sword", 2) }, "key-q1"));
            var query = harness.CreateQueryService();

            // Act
            var wallet = await query.GetWalletAsync(scope, "gold");
            var missing = await query.GetWalletAsync(scope, "gem");
            var inventory = await query.ListInventoryAsync(scope);

            // Assert
            Assert.True(wallet.IsSuccess);
            Assert.Equal(100, wallet.Data.Balance);
            Assert.True(missing.IsSuccess);
            Assert.Equal(0, missing.Data.Balance);
            var stack = Assert.Single(inventory.Data);
            Assert.Equal("sword", stack.ItemId);
            Assert.Equal(2, stack.Quantity);
        }

        /// <summary>
        /// 验证流水游标分页不重不漏（稳定排序 = 账本序，翻页衔接）。
        /// </summary>
        [Fact]
        public async Task GetLedgerPageAsync_PagesWithoutOverlapOrGaps()
        {
            // Arrange
            var harness = new Harness();
            var grantService = harness.CreateGrantService();
            for (var index = 1; index <= 5; index++)
            {
                var scope = new OnlineScope(1, 10, 100, 10001);
                var ok = await grantService.ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "五连发", "bo-p" + index, null, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 10) }, "key-p" + index));
                Assert.True(ok.IsSuccess);
            }

            var query = harness.CreateQueryService();
            var scope2 = new OnlineScope(1, 10, 100, 10001);

            // Act
            var page1 = await query.GetLedgerPageAsync(scope2, string.Empty, 4);
            var page2 = await query.GetLedgerPageAsync(scope2, page1.Data.Page.Cursor, 4);

            // Assert
            Assert.True(page1.IsSuccess);
            Assert.True(page1.Data.Page.HasMore);
            Assert.Equal(4, page1.Data.Entries.Count);
            Assert.True(page2.IsSuccess);
            Assert.False(page2.Data.Page.HasMore);
            Assert.Equal(string.Empty, page2.Data.Page.Cursor);
            Assert.Equal(1, page2.Data.Entries.Count);
            var combined = page1.Data.Entries.Concat(page2.Data.Entries).ToList();
            Assert.Equal(5, combined.Count);
            Assert.Equal(5, combined.Select(entry => entry.SequenceNumber).Distinct().Count());
            Assert.Equal(combined.OrderBy(entry => entry.SequenceNumber).ToList(), combined);
        }

        /// <summary>
        /// 验证游标与页距参数校验（不透明令牌不可构造）。
        /// </summary>
        [Theory]
        [InlineData("abc", 10)]
        [InlineData("-1", 10)]
        [InlineData("", 0)]
        [InlineData("", 101)]
        public async Task GetLedgerPageAsync_InvalidArguments_ShouldReturnParameterInvalid(string cursor, int maxCount)
        {
            // Arrange
            var harness = new Harness();
            var query = harness.CreateQueryService();
            var scope = new OnlineScope(1, 10, 100, 10001);

            // Act
            var result = await query.GetLedgerPageAsync(scope, cursor, maxCount);

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }

        /// <summary>
        /// 验证 VC-3.14 交易反查：来源/原因/单号/操作者/前后值全字段可见。
        /// </summary>
        [Fact]
        public async Task GetTransactionDetailAsync_ReturnsFullAuditFields()
        {
            // Arrange
            var harness = new Harness();
            var scope = new OnlineScope(1, 10, 100, 10001);
            var granted = await harness.CreateGrantService().ExecuteAsync(new OnlineGrantRequest(scope, OnlineAssetChangeSource.AdminOperation, OnlineGrantOperation.Reissue, "客诉补发", "bo-detail", "op-42", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 88) }, "key-detail"));
            var query = harness.CreateQueryService();

            // Act
            var detail = await query.GetTransactionDetailAsync(scope, granted.Data.TransactionId);

            // Assert
            Assert.True(detail.IsSuccess);
            Assert.Equal(OnlineAssetChangeSource.AdminOperation, detail.Data.Transaction.Source);
            Assert.Equal(OnlineGrantOperation.Reissue, detail.Data.Transaction.Operation);
            Assert.Equal("客诉补发", detail.Data.Transaction.Reason);
            Assert.Equal("bo-detail", detail.Data.Transaction.BusinessOrderId);
            Assert.Equal("op-42", detail.Data.Transaction.OperatorId);
            var entry = Assert.Single(detail.Data.Entries);
            Assert.Equal(0, entry.AmountBefore);
            Assert.Equal(88, entry.AmountAfter);
        }

        /// <summary>
        /// 验证跨作用域反查与不存在同构 ResourceNotFound（防存在性探测）。
        /// </summary>
        [Fact]
        public async Task GetTransactionDetailAsync_CrossScopeOrMissing_ShouldReturnResourceNotFound()
        {
            // Arrange
            var harness = new Harness();
            var ownerScope = new OnlineScope(1, 10, 100, 10001);
            var granted = await harness.CreateGrantService().ExecuteAsync(new OnlineGrantRequest(ownerScope, OnlineAssetChangeSource.MatchReward, OnlineGrantOperation.Grant, "赛果", "bo-cross", null, new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 10) }, "key-cross"));
            var query = harness.CreateQueryService();
            var strangerScope = new OnlineScope(1, 10, 100, 10999);

            // Act
            var crossScope = await query.GetTransactionDetailAsync(strangerScope, granted.Data.TransactionId);
            var missing = await query.GetTransactionDetailAsync(ownerScope, "tx-not-exists");

            // Assert
            Assert.False(crossScope.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, crossScope.Code);
            Assert.False(missing.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, missing.Code);
        }

        /// <summary>
        /// 验证查询必须绑定玩家主体位。
        /// </summary>
        [Fact]
        public async Task GetWalletAsync_WithoutPlayerScope_ShouldReturnParameterInvalid()
        {
            // Arrange
            var harness = new Harness();
            var query = harness.CreateQueryService();
            var scope = new OnlineScope(1, 10, 100, 0);

            // Act
            var result = await query.GetWalletAsync(scope, "gold");

            // Assert
            Assert.False(result.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, result.Code);
        }
    }
}
