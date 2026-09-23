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
using System.Linq;
using System.Reflection;
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
    /// 资产域结构守卫测试（vault:C4 X5/VC-3.7：批次旁路构造结构性不可达、
    /// 账本条目不可变、账本条目审计身份完备、枚举契约面快照锁定）。
    /// </summary>
    public class OnlineAssetDomainGuardTests
    {
        /// <summary>
        /// 验证 X5：变更批次无公共构造器——程序集外（含宿主装配与业务代码）无法构造批次，
        /// 旁路写入在类型层面不可达（资产变更唯一入口 = OnlineGrantService）。
        /// </summary>
        [Fact]
        public void OnlineAssetChangeBatch_HasNoPublicConstructor()
        {
            // Act
            var publicConstructors = typeof(OnlineAssetChangeBatch).GetConstructors();

            // Assert
            Assert.Empty(publicConstructors);
        }

        /// <summary>
        /// 验证 VC-3.7：账本条目与变更行只读（不可变由类型面保证——只追加，无修改路径）。
        /// </summary>
        [Theory]
        [InlineData(typeof(OnlineLedgerEntry))]
        [InlineData(typeof(OnlineAssetChangeLine))]
        public void ImmutableContracts_HaveNoSettableProperties(Type contractType)
        {
            // Act
            var settable = contractType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetSetMethod(true) != null)
                .ToList();

            // Assert
            Assert.Empty(settable);
        }

        /// <summary>
        /// 验证落账条目审计身份完备（无来源/无单号的变更不可能进入账本：TransactionId、
        /// Source、Reason、BusinessOrderId、双服记账字段全量非空/非默认）。
        /// </summary>
        [Fact]
        public async Task LedgerEntries_CarryCompleteAuditIdentity()
        {
            // Arrange
            var options = new IdempotencyOptions
            {
                ConcurrentWaitTimeoutMilliseconds = 1,
            };
            var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), options);
            var assetStore = new InMemoryOnlineAssetStore();
            var grantService = new OnlineGrantService(assetStore, new InMemoryOnlineAssetTransactionStore(), new OnlineIdempotencyService(coordinator, null), new NullPublisher(), null);
            var scope = new OnlineScope(1, 10, 100, 10001);
            var request = new OnlineGrantRequest(scope, OnlineAssetChangeSource.PaymentConfirm, OnlineGrantOperation.Grant, "支付发货", "bo-guard", new[] { new OnlineAssetChangeLine(OnlineAssetKind.Currency, "gold", 60) }, "key-guard");

            // Act
            var granted = await grantService.ExecuteAsync(request);

            // Assert
            Assert.True(granted.IsSuccess);
            var entry = Assert.Single(granted.Data.Entries);
            Assert.StartsWith("tx-", entry.TransactionId);
            Assert.Equal(OnlineAssetChangeSource.PaymentConfirm, entry.Source);
            Assert.Equal("支付发货", entry.Reason);
            Assert.Equal("bo-guard", entry.BusinessOrderId);
            Assert.Equal(100, entry.HomeServerId);
            Assert.Equal(100, entry.InitiatingServerId);
            Assert.True(entry.SequenceNumber > 0);
        }

        /// <summary>
        /// 验证枚举契约面快照（成员数与显式取值锁定——序号是持久化与跨服协议的一部分）。
        /// </summary>
        [Fact]
        public void AssetEnums_MemberCountsAndValuesAreLocked()
        {
            // Assert
            Assert.Equal(new[] { "PaymentConfirm", "RedeemCode", "MailAttachment", "MatchReward", "AdminOperation", "ActivityTask", "SystemCompensation" }, Enum.GetNames(typeof(OnlineAssetChangeSource)));
            Assert.Equal(7, Enum.GetValues(typeof(OnlineAssetChangeSource)).Cast<int>().Distinct().Count());
            Assert.Equal(new[] { "Grant", "Deduct", "Revoke", "Reissue", "Adjust" }, Enum.GetNames(typeof(OnlineGrantOperation)));
            Assert.Equal(new[] { "Executing", "Succeeded", "Failed", "CompensationPending", "Compensated" }, Enum.GetNames(typeof(OnlineAssetTransactionState)));
            Assert.Equal(new[] { "NegativeBalanceAttempt", "DuplicateRequestObserved", "CompensatedAfterApplyFailure", "CompensationFailed", "CompensationSloExceeded" }, Enum.GetNames(typeof(OnlineAssetAlertKind)));
        }

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
    }
}
