// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
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
using System.Reflection;
using System.Threading.Tasks;
using GameFrameX.Online.GameEvents;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 统一 Game Event Schema 测试（vault:C8 S7.5 / VC-7.9：脏事件拒绝且不进下游）。
    /// 覆盖登记表自检（17 个标准事件名一一对应）、版本策略、四类 L0 拒绝、
    /// 「受理 ⇔ 入存储 / 拒绝 ⇔ 入死信」两条不变量、事件标识去重。
    /// </summary>
    public class OnlineGameEventSchemaTests
    {
        /// <summary>
        /// 验证 VC-7.9-a：登记表与事件名常量一一对应（漏登记即该事件被判未知，守护测试锁定）。
        /// </summary>
        [Fact]
        public void Schema_ShouldRegisterEveryDeclaredName()
        {
            var declared = typeof(OnlineGameEventName)
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(field => field.IsLiteral && !field.IsInitOnly && field.FieldType == typeof(string))
                .Select(field => (string)field.GetRawConstantValue())
                .ToList();

            Assert.Equal(17, declared.Count);

            foreach (var name in declared)
            {
                Assert.True(OnlineGameEventSchema.Contains(name), "事件名未登记：" + name);
            }

            Assert.Equal(declared.Count, OnlineGameEventSchema.All.Count);
            Assert.Equal(declared.OrderBy(name => name, System.StringComparer.Ordinal).ToList(), OnlineGameEventSchema.All.Select(item => item.Name).ToList());

            // 每个事件名有分类、有当前版本、有必需字段；分类覆盖六类。
            foreach (var descriptor in OnlineGameEventSchema.All)
            {
                Assert.True(descriptor.CurrentVersion >= 1);
                Assert.NotEmpty(descriptor.RequiredFields);
            }

            var categories = OnlineGameEventSchema.All.Select(item => item.Category).Distinct().ToList();
            Assert.Equal(6, categories.Count);
            Assert.Contains(OnlineGameEventCategory.Login, categories);
            Assert.Contains(OnlineGameEventCategory.Matchmaking, categories);
            Assert.Contains(OnlineGameEventCategory.Match, categories);
            Assert.Contains(OnlineGameEventCategory.Reward, categories);
            Assert.Contains(OnlineGameEventCategory.Payment, categories);
            Assert.Contains(OnlineGameEventCategory.Report, categories);
        }

        /// <summary>
        /// 验证 VC-7.9-b：四类 L0 拒绝（未登记事件名 / 版本非法 / 必需字段缺失 / 作用域非法）逐个命中，
        /// 且**拒绝事件必进死信、绝不进事件存储**。
        /// </summary>
        [Fact]
        public async Task IngestAsync_DirtyEvents_ShouldRejectIntoDeadLetterAndNotStore()
        {
            var harness = new OnlineGameEventTestHarness();

            var cases = new[]
            {
                (Event: OnlineGameEventTestHarness.BuildEnvelope("NotARegisteredEvent", new Dictionary<string, string>()), Reason: OnlineGameEventRejectionReason.UnknownEventName),
                (Event: OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win), 0), Reason: OnlineGameEventRejectionReason.UnsupportedVersion),
                (Event: OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win), 2), Reason: OnlineGameEventRejectionReason.UnsupportedVersion),
                (Event: OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win, "PlayerId")), Reason: OnlineGameEventRejectionReason.MissingRequiredField),
                (Event: OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win), 1, 0, OnlineGameEventTestHarness.AppId), Reason: OnlineGameEventRejectionReason.InvalidScope),
                (Event: OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Win, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Win), 1, OnlineGameEventTestHarness.TenantId, 0), Reason: OnlineGameEventRejectionReason.InvalidScope),
            };

            foreach (var item in cases)
            {
                var outcome = await harness.Ingestor.IngestAsync(item.Event, OnlineGameEventTestHarness.Now);

                Assert.False(outcome.IsAccepted);
                Assert.Equal(item.Reason, outcome.RejectionReason);
                Assert.False(string.IsNullOrEmpty(outcome.RejectionMessage));
            }

            // 脏事件不污染下游：存储里一条都没有。
            Assert.Empty(await harness.StoredEventsAsync());

            // 死信按事件自身作用域归档：四条作用域合法者落在本作用域下。
            var deadLetters = await harness.DeadLettersAsync();
            Assert.Equal(4, deadLetters.Count);
            Assert.Equal(
                new[]
                {
                    OnlineGameEventRejectionReason.UnknownEventName,
                    OnlineGameEventRejectionReason.UnsupportedVersion,
                    OnlineGameEventRejectionReason.UnsupportedVersion,
                    OnlineGameEventRejectionReason.MissingRequiredField,
                },
                deadLetters.Select(item => item.Reason).ToArray());

            // 作用域非法的两条按「退化作用域」原样归档（可检索、不丢失）。
            var zeroTenant = await harness.DeadLetterSink.ListAsync(0, OnlineGameEventTestHarness.AppId, 0, long.MaxValue);
            var zeroApp = await harness.DeadLetterSink.ListAsync(OnlineGameEventTestHarness.TenantId, 0, 0, long.MaxValue);
            Assert.Single(zeroTenant);
            Assert.Single(zeroApp);
            Assert.Equal(OnlineGameEventRejectionReason.InvalidScope, zeroTenant[0].Reason);
            Assert.Equal(OnlineGameEventRejectionReason.InvalidScope, zeroApp[0].Reason);

            // 空信封是编程错误（拒绝意味着「有事件但脏」，空信封没有可归档的实体）。
            await Assert.ThrowsAsync<ArgumentNullException>(() => harness.Ingestor.IngestAsync(null));
        }

        /// <summary>
        /// 验证 VC-7.9-c：版本策略为「只增不减」——登记版本受理、低于 1 与高于当前版本拒绝，
        /// 受理路径无副作用（不进死信）。
        /// </summary>
        [Fact]
        public async Task IngestAsync_VersionPolicy_ShouldAcceptOnlyDeclaredRange()
        {
            var harness = new OnlineGameEventTestHarness();
            var accepted = await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.MatchEnd, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.MatchEnd)), OnlineGameEventTestHarness.Now);

            Assert.True(accepted.IsAccepted);
            Assert.Equal(OnlineGameEventRejectionReason.None, accepted.RejectionReason);
            Assert.False(accepted.IsDuplicate);
            Assert.Empty(await harness.DeadLettersAsync());
            Assert.Single(await harness.StoredEventsAsync());
        }

        /// <summary>
        /// 验证 VC-7.9-d：事件标识是存储幂等键——同一信封重投只落一条，回执标记为重复而非拒绝。
        /// </summary>
        [Fact]
        public async Task IngestAsync_RepeatedEnvelope_ShouldDeduplicate()
        {
            var harness = new OnlineGameEventTestHarness();
            var envelope = OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.Login, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.Login));

            var first = await harness.Ingestor.IngestAsync(envelope, OnlineGameEventTestHarness.Now);
            var second = await harness.Ingestor.IngestAsync(envelope, OnlineGameEventTestHarness.Now);

            Assert.True(first.IsAccepted);
            Assert.False(first.IsDuplicate);
            Assert.True(second.IsAccepted);
            Assert.True(second.IsDuplicate);
            Assert.Equal(OnlineGameEventRejectionReason.None, second.RejectionReason);
            Assert.Single(await harness.StoredEventsAsync());
        }

        /// <summary>
        /// 验证 VC-7.9-e：必需字段判定读的是信封的**载荷语义投影**（与序列化格式无关），
        /// 因此载荷为空但投影齐备的事件仍被受理（上游载荷格式演进不击穿本层）。
        /// </summary>
        [Fact]
        public async Task Validate_ShouldReadAuditFieldsNotPayloadBytes()
        {
            var harness = new OnlineGameEventTestHarness();
            var envelope = OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.SessionStart, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.SessionStart));

            // 载荷字节抹空、字段投影齐备 → 仍被受理（判定不看字节，故上游载荷格式演进不击穿本层）。
            var payloadBytes = envelope.Payload;
            envelope.Payload = default;
            Assert.True(OnlineGameEventValidator.Validate(envelope).IsAccepted);

            // 投影清空、载荷字节原样写回 → 仍判必需字段缺失（字节里的字段补不回投影）。
            envelope.Payload = payloadBytes;
            envelope.PayloadAuditFields = null;
            var rejected = OnlineGameEventValidator.Validate(envelope);

            Assert.False(rejected.IsAccepted);
            Assert.Equal(OnlineGameEventRejectionReason.MissingRequiredField, rejected.Reason);

            Assert.True((await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.SessionStart, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.SessionStart)), OnlineGameEventTestHarness.Now)).IsAccepted);
        }

        /// <summary>
        /// 验证 VC-7.9-f：存储与死信的作用域隔离（跨 App 查询不到，反预言）。
        /// </summary>
        [Fact]
        public async Task StoreAndDeadLetter_ShouldIsolateScope()
        {
            var harness = new OnlineGameEventTestHarness();
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope(OnlineGameEventName.MatchQueue, OnlineGameEventTestHarness.CompleteFields(OnlineGameEventName.MatchQueue)), OnlineGameEventTestHarness.Now);
            await harness.Ingestor.IngestAsync(OnlineGameEventTestHarness.BuildEnvelope("NotARegisteredEvent", new Dictionary<string, string>()), OnlineGameEventTestHarness.Now);

            Assert.Single(await harness.StoredEventsAsync());
            Assert.Single(await harness.DeadLettersAsync());
            Assert.Empty(await harness.EventStore.ListAsync(OnlineGameEventTestHarness.OtherAppId, OnlineGameEventTestHarness.AppId, 0, long.MaxValue));
            Assert.Empty(await harness.EventStore.ListAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.OtherAppId, 0, long.MaxValue));
            Assert.Empty(await harness.DeadLetterSink.ListAsync(OnlineGameEventTestHarness.TenantId, OnlineGameEventTestHarness.OtherAppId, 0, long.MaxValue));
        }
    }
}
