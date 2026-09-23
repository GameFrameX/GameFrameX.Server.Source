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
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Scope;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 聊天服务测试（vault:C7 S6.5/S6.6/S6.7：VC-6.10 游标翻页不重复不漏项、VC-6.15 审核插件不阻断主流程、
    /// 以及频道成员资格 fail closed、频控、撤回窗口与事件脱敏）。
    /// </summary>
    public class OnlineChatServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家一标识。</summary>
        private const long PlayerOne = 1001;

        /// <summary>测试用玩家二标识。</summary>
        private const long PlayerTwo = 1002;

        /// <summary>测试用玩家三标识（无关第三方）。</summary>
        private const long PlayerThree = 1003;

        /// <summary>测试用队伍标识。</summary>
        private const string PartyId = "party-1";

        /// <summary>
        /// 验证频道标识派生与方向无关：A→B 与 B→A 必须落在同一个定向频道。
        /// <para>
        /// 这是「私聊历史不被劈成两条单向流」的实现依据：若两侧各自生成频道，双方会各说各话，
        /// 对方的消息永远读不到，而系统不会报任何错。
        /// </para>
        /// </summary>
        [Fact]
        public void BuildChannelId_ShouldBeIndependentOfDirection()
        {
            // Arrange & Act
            var forward = OnlineChatService.BuildChannelId(TenantId, AppId, OnlineChatChannelKind.Direct, PlayerOne, PlayerTwo, string.Empty);
            var backward = OnlineChatService.BuildChannelId(TenantId, AppId, OnlineChatChannelKind.Direct, PlayerTwo, PlayerOne, string.Empty);
            var otherPair = OnlineChatService.BuildChannelId(TenantId, AppId, OnlineChatChannelKind.Direct, PlayerOne, PlayerThree, string.Empty);

            // Assert
            Assert.Equal(forward, backward);
            Assert.NotEqual(forward, otherPair);
        }

        /// <summary>
        /// 验证双方各自打开私聊得到同一个频道（确定性标识带来的天然幂等）。
        /// </summary>
        [Fact]
        public async Task OpenDirectChannelAsync_WhenOpenedFromBothSides_ShouldReturnSameChannel()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var fromOne = await service.OpenDirectChannelAsync(CreatePlayerScope(PlayerOne), PlayerTwo);
            var fromTwo = await service.OpenDirectChannelAsync(CreatePlayerScope(PlayerTwo), PlayerOne);

            // Assert
            Assert.True(fromOne.IsSuccess);
            Assert.True(fromTwo.IsSuccess);
            Assert.Equal(fromOne.Data.ChannelId, fromTwo.Data.ChannelId);
            Assert.Equal(2, fromOne.Data.Participants.Count);
        }

        /// <summary>
        /// 验证私聊对端非法（自己或非正数）时被参数拒绝。
        /// </summary>
        [Fact]
        public async Task OpenDirectChannelAsync_WhenTargetInvalid_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var self = await service.OpenDirectChannelAsync(CreatePlayerScope(PlayerOne), PlayerOne);
            var invalid = await service.OpenDirectChannelAsync(CreatePlayerScope(PlayerOne), 0);

            // Assert
            Assert.False(self.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, self.Code);
            Assert.False(invalid.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, invalid.Code);
        }

        /// <summary>
        /// 验证全局频道在同一 App 内对任何玩家都是同一个（不按玩家切分）。
        /// </summary>
        [Fact]
        public async Task OpenGlobalChannelAsync_ShouldBeSharedAcrossPlayers()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var fromOne = await service.OpenGlobalChannelAsync(CreatePlayerScope(PlayerOne));
            var fromTwo = await service.OpenGlobalChannelAsync(CreatePlayerScope(PlayerTwo));

            // Assert
            Assert.True(fromOne.IsSuccess);
            Assert.Equal(fromOne.Data.ChannelId, fromTwo.Data.ChannelId);
            Assert.Equal(OnlineChatChannelKind.Global, fromOne.Data.Kind);
        }

        /// <summary>
        /// 验证发送成功会落库并发出消息事件，且事件载荷**不含消息正文**（C93 脱敏红线）。
        /// <para>
        /// 事件会流向审计、Admin 与离线消费方；一旦带上正文，就等于绕过了频道成员资格校验。
        /// </para>
        /// </summary>
        [Fact]
        public async Task SendAsync_ShouldPersistAndPublishSanitizedEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "这是一条机密内容");

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal("这是一条机密内容", outcome.Data.Content);
            Assert.True(outcome.Data.Sequence > 0);

            var events = recorder.Filter(OnlineChatEvents.MessageSent);
            Assert.Single(events);
            Assert.Equal(OnlineChatEvents.Source, events[0].Source);
            Assert.DoesNotContain("机密内容", System.Text.Encoding.UTF8.GetString(events[0].Payload.Span));
            Assert.False(events[0].PayloadAuditFields.ContainsKey("Content"));
        }

        /// <summary>
        /// 验证同一去重键重复发送返回同一条消息，不产生第二条（重试与补发同时到达时业务只执行一次）。
        /// <para>
        /// 同时锁住重放的三个「不应该」：不重复外发 <c>MessageSent</c>（否则消费方会看到一条并不存在的新消息）、
        /// 不消耗频控配额（配额已满时重放仍须成功，否则客户端把「已入库」当成「发送失败」再重试，
        /// 而重试照样被频控拒绝，消息永远送不出去）、不再走内容审核。
        /// </para>
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenDedupeKeyRepeated_ShouldReturnSameMessage()
        {
            // Arrange
            var service = CreateService(out var recorder, 1);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var first = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "重试内容", "dk-1");
            var second = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "重试内容", "dk-1");

            // Assert
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.Equal(first.Data.MessageId, second.Data.MessageId);

            var history = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId);
            Assert.Single(history.Data.Messages);
            Assert.Single(recorder.Filter(OnlineChatEvents.MessageSent));

            // 配额已被首条用尽（上限 = 1）：重放没吃掉配额，故此刻换新去重键的新消息才该被频控拦下。
            var fresh = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "新内容", "dk-2");
            Assert.False(fresh.IsSuccess);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, fresh.Code);
        }

        /// <summary>
        /// 验证空内容与超长内容被参数拒绝（内容上限来自可配置项）。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenContentEmptyOrTooLong_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var tooLong = new string('x', 11);

            // Act
            var empty = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "   ");
            var overLong = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, tooLong);

            // Assert
            Assert.False(empty.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, empty.Code);
            Assert.False(overLong.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, overLong.Code);
        }

        /// <summary>
        /// 验证窗口内超出条数上限被频控拒绝（限额按玩家 × 频道计）。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenRateLimitExceeded_ShouldDeny()
        {
            // Arrange
            var service = CreateService(out _, rateLimitMaxMessages: 2);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var first = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "1");
            var second = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "2");
            var third = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "3");

            // Assert
            Assert.True(first.IsSuccess);
            Assert.True(second.IsSuccess);
            Assert.False(third.IsSuccess);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, third.Code);
        }

        /// <summary>
        /// 验证被内容校验打回的消息**不占用**频控配额（校验早于频控）。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenContentRejected_ShouldNotConsumeRateQuota()
        {
            // Arrange
            var service = CreateService(out _, rateLimitMaxMessages: 2);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var tooLong = new string('x', 11);

            // Act
            for (var index = 0; index < 5; index++)
            {
                var rejected = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, tooLong);
                Assert.Equal(OnlineErrorCode.ParameterInvalid, rejected.Code);
            }

            var firstValid = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "合法一");
            var secondValid = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "合法二");
            var overQuota = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "合法三");

            // Assert
            Assert.True(firstValid.IsSuccess);
            Assert.True(secondValid.IsSuccess);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, overQuota.Code);
        }

        /// <summary>
        /// 验证被审核拒绝的消息不入库、不返回成功，**且占用一个频控配额槽**。
        /// <para>
        /// 这个取舍是刻意的：频控在审核之前生效，否则恶意玩家可以绕过频率限制直接打满审核服务。
        /// 代价是被误伤的内容少一次配额，窗口结束即恢复。
        /// </para>
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenModerationRejects_ShouldDenyWithoutPersistButConsumeQuota()
        {
            // Arrange
            var service = CreateService(out _, rateLimitMaxMessages: 2, moderationHook: new RejectingModerationHook("含敏感词"));
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var rejected = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "敏感内容");
            var alsoRejected = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "敏感内容");
            var overQuota = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "敏感内容");

            // Assert
            Assert.False(rejected.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, rejected.Code);
            Assert.False(alsoRejected.IsSuccess);
            Assert.Equal(OnlineErrorCode.RateLimitExceeded, overQuota.Code);

            var history = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId);
            Assert.Empty(history.Data.Messages);
        }

        /// <summary>
        /// 验证审核插件抛异常时**放行**并发出留痕事件（VC-6.15：审核服务故障不得让全服聊天停摆）。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenModerationHookThrows_ShouldAllowAndPublishModerationFailed()
        {
            // Arrange
            var service = CreateService(out var recorder, moderationHook: new ThrowingModerationHook());
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "宕机期间的内容");

            // Assert
            Assert.True(outcome.IsSuccess);
            var failures = recorder.Filter(OnlineChatEvents.ModerationFailed);
            Assert.Single(failures);
            Assert.Equal("审核服务不可用", failures[0].PayloadAuditFields["Reason"]);

            var history = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId);
            Assert.Single(history.Data.Messages);
        }

        /// <summary>
        /// 验证未装配审核插件时全部放行（审核是扩展能力，不是聊天可用的前提）。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenModerationHookMissing_ShouldAllow()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "内容");

            // Assert
            Assert.True(outcome.IsSuccess);
        }

        /// <summary>
        /// 验证队伍 / 群组频道在成员资格探针未装配时 **fail closed**（返回未就绪而不是放行）。
        /// <para>
        /// 放行等于把频道内容广播给任何知道频道标识的人——宁可不可用，也不能静默泄露。
        /// </para>
        /// </summary>
        [Fact]
        public async Task OpenBoundChannelAsync_WhenMembershipProbeMissing_ShouldFailClosed()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var party = await service.OpenBoundChannelAsync(CreatePlayerScope(PlayerOne), OnlineChatChannelKind.Party, PartyId);
            var group = await service.OpenBoundChannelAsync(CreatePlayerScope(PlayerOne), OnlineChatChannelKind.Group, "group-1");

            // Assert
            Assert.False(party.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, party.Code);
            Assert.False(group.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateNotReady, group.Code);
        }

        /// <summary>
        /// 验证探针判定非成员时按反预言返回未找到（不泄露频道是否存在）。
        /// </summary>
        [Fact]
        public async Task OpenBoundChannelAsync_WhenProbeSaysNotMember_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out _, membershipProbe: new StubMembershipProbe(PlayerTwo));

            // Act
            var member = await service.OpenBoundChannelAsync(CreatePlayerScope(PlayerTwo), OnlineChatChannelKind.Party, PartyId);
            var outsider = await service.OpenBoundChannelAsync(CreatePlayerScope(PlayerOne), OnlineChatChannelKind.Party, PartyId);

            // Assert
            Assert.True(member.IsSuccess);
            Assert.False(outsider.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outsider.Code);
        }

        /// <summary>
        /// 验证绑定频道类型非法（不接受绑定主体的类型）与绑定主体为空时被参数拒绝。
        /// <para>
        /// 空白串（如 <c>"  "</c>）不在拒绝范围内：本仓对标识的"空"判据统一是
        /// <see cref="string.IsNullOrEmpty"/>，不做裁剪——此处按既有口径断言，不额外收紧契约。
        /// </para>
        /// </summary>
        [Fact]
        public async Task OpenBoundChannelAsync_WhenKindOrBoundIdInvalid_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _, membershipProbe: new StubMembershipProbe(PlayerOne));

            // Act
            var direct = await service.OpenBoundChannelAsync(CreatePlayerScope(PlayerOne), OnlineChatChannelKind.Direct, PartyId);
            var noBound = await service.OpenBoundChannelAsync(CreatePlayerScope(PlayerOne), OnlineChatChannelKind.Party, string.Empty);

            // Assert
            Assert.False(direct.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, direct.Code);
            Assert.False(noBound.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noBound.Code);
        }

        /// <summary>
        /// 验证定向私聊受屏蔽约束（VC-6.3 落到聊天链路上：A 屏蔽 B 后 A 发不出去，B 也发不出去）。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenParticipantsBlocked_ShouldDenyBothDirections()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            Assert.True((await service.DecisionService.BlockAsync(CreatePlayerScope(PlayerOne), PlayerTwo)).IsSuccess);

            // Act
            var fromOwner = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "你好");
            var fromBlocked = await service.SendAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId, "你好");

            // Assert
            Assert.False(fromOwner.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, fromOwner.Code);
            Assert.False(fromBlocked.IsSuccess);
            Assert.Equal(OnlineErrorCode.RiskControlRejected, fromBlocked.Code);
        }

        /// <summary>
        /// 验证非频道参与者读历史按反预言返回未找到（不泄露频道存在性）。
        /// </summary>
        [Fact]
        public async Task GetHistoryAsync_WhenNotParticipant_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.GetHistoryAsync(CreatePlayerScope(PlayerThree), channel.ChannelId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
        }

        /// <summary>
        /// 验证分页游标格式非法时被参数拒绝（客户端不得自行编造游标）。
        /// </summary>
        [Fact]
        public async Task GetHistoryAsync_WhenCursorMalformed_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "not-a-cursor");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 验证连发后翻页**不重复、不漏项**（VC-6.10），且顺序为 <c>(SentAtTime, Sequence)</c> 升序。
        /// </summary>
        [Fact]
        public async Task GetHistoryAsync_ShouldPageCompleteSequenceWithoutDuplicateOrMissing()
        {
            // Arrange
            var service = CreateService(out _, rateLimitMaxMessages: 1000);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var expected = new List<string>();
            for (var index = 0; index < 25; index++)
            {
                var sent = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "消息-" + index);
                Assert.True(sent.IsSuccess);
                expected.Add(sent.Data.MessageId);
            }

            // Act
            var collected = await ReadAllPagesAsync(service, CreatePlayerScope(PlayerOne), channel.ChannelId, 7);

            // Assert
            Assert.Equal(expected.Count, collected.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in collected)
            {
                Assert.True(seen.Add(message.MessageId), "翻页返回了重复消息：" + message.MessageId);
            }

            foreach (var messageId in expected)
            {
                Assert.Contains(messageId, seen);
            }

            for (var index = 1; index < collected.Count; index++)
            {
                var previous = collected[index - 1];
                var current = collected[index];
                var ascending = previous.SentAtTime < current.SentAtTime
                    || (previous.SentAtTime == current.SentAtTime && previous.Sequence < current.Sequence);
                Assert.True(ascending, "翻页顺序不是 (SentAtTime, Sequence) 升序");
            }
        }

        /// <summary>
        /// 验证同一毫秒内的多条消息（排序键完全相同的时刻分量）仍能完整翻页 ——
        /// 这是频道内序号作为第二排序键的价值所在：只按时间排序会让同刻消息在翻页时被跳过或重复。
        /// </summary>
        [Fact]
        public async Task GetHistoryAsync_WhenMessagesShareSameMillisecond_ShouldStillPageEachExactlyOnce()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var store = service.Store;
            var sharedSentAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var expected = new List<string>();
            for (var index = 0; index < 5; index++)
            {
                var draft = new OnlineChatMessage
                {
                    MessageId = "msg-fixed-" + index,
                    TenantId = TenantId,
                    AppId = AppId,
                    ChannelId = channel.ChannelId,
                    ChannelKind = OnlineChatChannelKind.Direct,
                    SenderId = PlayerOne,
                    Content = "同刻-" + index,
                    SentAtTime = sharedSentAtTime,
                    Sequence = 0,
                    State = OnlineChatMessageState.Normal,
                    DedupeKey = string.Empty,
                };
                var stored = await store.AppendAsync(draft, CancellationToken.None);
                expected.Add(stored.MessageId);
            }

            // Act
            var collected = await ReadAllPagesAsync(service, CreatePlayerScope(PlayerOne), channel.ChannelId, 2);

            // Assert
            Assert.Equal(expected.Count, collected.Count);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in collected)
            {
                Assert.True(seen.Add(message.MessageId), "同刻消息翻页出现重复：" + message.MessageId);
            }

            foreach (var messageId in expected)
            {
                Assert.Contains(messageId, seen);
            }
        }

        /// <summary>
        /// 验证翻页期间新到达的消息不会让旧游标重复返回已翻过的内容（离线补拉的稳定性）。
        /// </summary>
        [Fact]
        public async Task GetHistoryAsync_WhenNewMessagesArriveDuringPaging_ShouldNotDuplicateSeenOnes()
        {
            // Arrange
            var service = CreateService(out _, rateLimitMaxMessages: 1000);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            for (var index = 0; index < 6; index++)
            {
                Assert.True((await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "首批-" + index)).IsSuccess);
            }

            var firstPage = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, null, 3);
            Assert.True(firstPage.IsSuccess);
            Assert.True(firstPage.Data.PageCursor.HasMore);

            // 翻页途中有新消息落库（最常见的中断场景）。
            for (var index = 0; index < 3; index++)
            {
                Assert.True((await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "插队-" + index)).IsSuccess);
            }

            // Act
            var secondPage = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, firstPage.Data.PageCursor.Cursor, 3);

            // Assert
            Assert.True(secondPage.IsSuccess);
            var firstPageIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var message in firstPage.Data.Messages)
            {
                firstPageIds.Add(message.MessageId);
            }

            foreach (var message in secondPage.Data.Messages)
            {
                Assert.False(firstPageIds.Contains(message.MessageId), "续页重复返回了首页已给过的消息：" + message.MessageId);
            }
        }

        /// <summary>
        /// 验证已撤回消息在下发面被抹去内容（审计留原文，下发面清空）。
        /// </summary>
        [Fact]
        public async Task GetHistoryAsync_ShouldRedactRecalledContent()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var sent = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "将被撤回");
            Assert.True((await service.RecallAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, sent.Data.MessageId)).IsSuccess);

            // Act
            var history = await service.GetHistoryAsync(CreatePlayerScope(PlayerOne), channel.ChannelId);

            // Assert
            Assert.Single(history.Data.Messages);
            Assert.Equal(OnlineChatMessageState.Recalled, history.Data.Messages[0].State);
            Assert.Equal(string.Empty, history.Data.Messages[0].Content);
        }

        /// <summary>
        /// 验证撤回在窗口内成功：内容抹去、状态置撤回、发出撤回事件。
        /// </summary>
        [Fact]
        public async Task RecallAsync_WhenSenderWithinWindow_ShouldRedactAndPublishEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var sent = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "发错了");

            // Act
            var outcome = await service.RecallAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, sent.Data.MessageId);

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlineChatMessageState.Recalled, outcome.Data.State);
            Assert.Equal(string.Empty, outcome.Data.Content);
            Assert.Equal(PlayerOne, outcome.Data.RecalledByPlayerId);
            Assert.True(outcome.Data.RecalledAtTime > 0);
            Assert.Single(recorder.Filter(OnlineChatEvents.MessageRecalled));
        }

        /// <summary>
        /// 验证非发送者撤回别人的消息返回未找到（反预言：看不到这条消息，也谈不上撤回）。
        /// </summary>
        [Fact]
        public async Task RecallAsync_WhenNotSender_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out _, membershipProbe: new StubMembershipProbe(PlayerOne, PlayerTwo));
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var sent = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "别人发的");

            // Act
            var outcome = await service.RecallAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId, sent.Data.MessageId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
        }

        /// <summary>
        /// 验证超出撤回窗口的撤回被拒（按业务状态不允许，而不是参数错误）。
        /// </summary>
        [Fact]
        public async Task RecallAsync_WhenBeyondWindow_ShouldReturnStateOperationForbidden()
        {
            // Arrange
            var service = CreateService(out _, recallWindowSeconds: 120);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var old = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() - 600000;
            var draft = new OnlineChatMessage
            {
                MessageId = "msg-old",
                TenantId = TenantId,
                AppId = AppId,
                ChannelId = channel.ChannelId,
                ChannelKind = OnlineChatChannelKind.Direct,
                SenderId = PlayerOne,
                Content = "很久以前",
                SentAtTime = old,
                Sequence = 0,
                State = OnlineChatMessageState.Normal,
                DedupeKey = string.Empty,
            };
            Assert.NotNull(await service.Store.AppendAsync(draft, CancellationToken.None));

            // Act
            var outcome = await service.RecallAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "msg-old");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.StateOperationForbidden, outcome.Code);
        }

        /// <summary>
        /// 验证重复撤回幂等（返回既有终态而不是报错）。
        /// </summary>
        [Fact]
        public async Task RecallAsync_WhenAlreadyRecalled_ShouldBeIdempotent()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var sent = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "发错了");
            Assert.True((await service.RecallAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, sent.Data.MessageId)).IsSuccess);

            // Act
            var again = await service.RecallAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, sent.Data.MessageId);

            // Assert
            Assert.True(again.IsSuccess);
            Assert.Equal(OnlineChatMessageState.Recalled, again.Data.State);
        }

        /// <summary>
        /// 验证未读数排除本人发送的与已撤回的消息，且标记已读后归零。
        /// </summary>
        [Fact]
        public async Task GetUnreadCountAsync_ShouldExcludeOwnMessagesAndResetAfterMarkRead()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            Assert.True((await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "来自一方")).IsSuccess);
            var second = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "将被撤回");
            Assert.True((await service.RecallAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, second.Data.MessageId)).IsSuccess);

            // Act
            var beforeRead = await service.GetUnreadCountAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId);
            var ownCount = await service.GetUnreadCountAsync(CreatePlayerScope(PlayerOne), channel.ChannelId);
            var firstMessageId = (await service.GetHistoryAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId)).Data.Messages[0].MessageId;
            Assert.True((await service.MarkReadAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId, firstMessageId)).IsSuccess);
            var afterRead = await service.GetUnreadCountAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId);

            // Assert
            Assert.Equal(1, beforeRead.Data);
            Assert.Equal(0, ownCount.Data);
            Assert.Equal(0, afterRead.Data);
        }

        /// <summary>
        /// 验证已读位点只进不退（乱序到达的旧「已读」不得把位点推回去，否则红点会重新亮起）。
        /// </summary>
        [Fact]
        public async Task MarkReadAsync_ShouldBeMonotonic()
        {
            // Arrange
            var service = CreateService(out _, rateLimitMaxMessages: 1000);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);
            var first = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "第一条");
            var second = await service.SendAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "第二条");
            var forwarded = await service.MarkReadAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId, second.Data.MessageId);
            Assert.True(forwarded.IsSuccess);

            // Act
            var backward = await service.MarkReadAsync(CreatePlayerScope(PlayerTwo), channel.ChannelId, first.Data.MessageId);

            // Assert
            Assert.True(backward.IsSuccess);
            Assert.Equal(second.Data.MessageId, backward.Data.LastReadMessageId);
            Assert.Equal(forwarded.Data.LastReadSequence, backward.Data.LastReadSequence);
        }

        /// <summary>
        /// 验证标记已读到不存在的消息返回未找到。
        /// </summary>
        [Fact]
        public async Task MarkReadAsync_WhenMessageMissing_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.MarkReadAsync(CreatePlayerScope(PlayerOne), channel.ChannelId, "msg-missing");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
        }

        /// <summary>
        /// 验证发送 / 读取在缺少玩家主体位时被参数拒绝。
        /// </summary>
        [Fact]
        public async Task SendAsync_WhenScopeLacksPlayer_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var channel = await OpenDirectAsync(service, PlayerOne, PlayerTwo);

            // Act
            var outcome = await service.SendAsync(new OnlineScope(TenantId, AppId, ServerId, 0), channel.ChannelId, "内容");

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 翻完整个频道历史并返回按序遍历的消息（测试用分页驱动，带收敛保护）。
        /// </summary>
        /// <param name="service">聊天服务。</param>
        /// <param name="scope">读取者作用域。</param>
        /// <param name="channelId">频道标识。</param>
        /// <param name="pageSize">每页条数。</param>
        /// <returns>按序收集到的全部消息。</returns>
        private static async Task<List<OnlineChatMessage>> ReadAllPagesAsync(TestChatFixture fixture, OnlineScope scope, string channelId, int pageSize)
        {
            var collected = new List<OnlineChatMessage>();
            var cursor = string.Empty;
            for (var guard = 0; guard < 100; guard++)
            {
                var page = await fixture.GetHistoryAsync(scope, channelId, string.IsNullOrEmpty(cursor) ? null : cursor, pageSize);
                Assert.True(page.IsSuccess);
                collected.AddRange(page.Data.Messages);
                if (!page.Data.PageCursor.HasMore)
                {
                    return collected;
                }

                cursor = page.Data.PageCursor.Cursor;
            }

            Assert.Fail("分页未在预期轮次内收敛");
            return collected;
        }

        /// <summary>
        /// 打开双人私聊频道。
        /// </summary>
        /// <param name="fixture">聊天夹具。</param>
        /// <param name="leftPlayerId">一侧玩家。</param>
        /// <param name="rightPlayerId">另一侧玩家。</param>
        /// <returns>私聊频道。</returns>
        private static async Task<OnlineChatChannel> OpenDirectAsync(TestChatFixture fixture, long leftPlayerId, long rightPlayerId)
        {
            var outcome = await fixture.OpenDirectChannelAsync(CreatePlayerScope(leftPlayerId), rightPlayerId);
            Assert.True(outcome.IsSuccess);
            return outcome.Data;
        }

        /// <summary>
        /// 构造玩家作用域。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>作用域。</returns>
        private static OnlineScope CreatePlayerScope(long playerId)
        {
            return new OnlineScope(TenantId, AppId, ServerId, playerId);
        }

        /// <summary>
        /// 创建被测聊天服务（并把内部件暴露给断言用）。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <param name="rateLimitMaxMessages">频控窗口内允许的条数（默认取上限避免干扰非频控用例）。</param>
        /// <param name="recallWindowSeconds">撤回窗口（秒）。</param>
        /// <param name="membershipProbe">频道成员资格探针（可空）。</param>
        /// <param name="moderationHook">内容审核插件（可空）。</param>
        /// <returns>聊天服务实例（已把存储、裁决服务挂在测试夹具上）。</returns>
        private static TestChatFixture CreateService(out OnlineEventRecorder recorder, int rateLimitMaxMessages = 1000, int recallWindowSeconds = 120, IOnlineChannelMembershipProbe membershipProbe = null, IOnlineChatModerationHook moderationHook = null)
        {
            recorder = new OnlineEventRecorder();
            var store = new InMemoryOnlineChatStore();
            var decisionService = new OnlineSocialDecisionService(new InMemoryOnlineSocialGraphStore(), new InMemoryOnlineReportStore(), recorder);
            var options = new OnlineChatOptions
            {
                MaxContentLength = 10,
                MaxMessagesPerPage = 50,
                RecallWindowSeconds = recallWindowSeconds,
                RateLimitWindowSeconds = 10,
                RateLimitMaxMessages = rateLimitMaxMessages,
                HistoryRetentionSeconds = 0,
            };
            var service = new OnlineChatService(store, recorder, decisionService, options, membershipProbe, moderationHook);
            return new TestChatFixture(service, store, decisionService);
        }

        /// <summary>
        /// 聊天服务测试夹具（把服务与其内部依赖一并带出，供断言直接观察存储与裁决）。
        /// </summary>
        private sealed class TestChatFixture
        {
            /// <summary>初始化夹具。</summary>
            /// <param name="service">聊天服务。</param>
            /// <param name="store">消息存储。</param>
            /// <param name="decisionService">社交裁决服务。</param>
            public TestChatFixture(OnlineChatService service, InMemoryOnlineChatStore store, OnlineSocialDecisionService decisionService)
            {
                Service = service;
                Store = store;
                DecisionService = decisionService;
            }

            /// <summary>聊天服务。</summary>
            public OnlineChatService Service { get; }

            /// <summary>消息存储。</summary>
            public InMemoryOnlineChatStore Store { get; }

            /// <summary>社交裁决服务。</summary>
            public OnlineSocialDecisionService DecisionService { get; }

            /// <summary>按玩家打开私聊频道。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="otherPlayerId">对端玩家。</param>
            /// <returns>频道开启结果。</returns>
            public Task<OnlineResult<OnlineChatChannel>> OpenDirectChannelAsync(OnlineScope scope, long otherPlayerId)
            {
                return Service.OpenDirectChannelAsync(scope, otherPlayerId);
            }

            /// <summary>打开绑定主体的频道。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="kind">频道类型。</param>
            /// <param name="boundId">绑定主体标识。</param>
            /// <returns>频道开启结果。</returns>
            public Task<OnlineResult<OnlineChatChannel>> OpenBoundChannelAsync(OnlineScope scope, OnlineChatChannelKind kind, string boundId)
            {
                return Service.OpenBoundChannelAsync(scope, kind, boundId);
            }

            /// <summary>打开全局频道。</summary>
            /// <param name="scope">作用域。</param>
            /// <returns>频道开启结果。</returns>
            public Task<OnlineResult<OnlineChatChannel>> OpenGlobalChannelAsync(OnlineScope scope)
            {
                return Service.OpenGlobalChannelAsync(scope);
            }

            /// <summary>发送消息。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="channelId">频道标识。</param>
            /// <param name="content">内容。</param>
            /// <param name="dedupeKey">去重键（可空）。</param>
            /// <returns>发送结果。</returns>
            public Task<OnlineResult<OnlineChatMessage>> SendAsync(OnlineScope scope, string channelId, string content, string dedupeKey = null)
            {
                return Service.SendAsync(scope, channelId, content, dedupeKey);
            }

            /// <summary>读取历史分页。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="channelId">频道标识。</param>
            /// <param name="cursor">游标（可空）。</param>
            /// <param name="pageSize">每页条数。</param>
            /// <returns>历史分页结果。</returns>
            public Task<OnlineResult<OnlineChatHistoryPage>> GetHistoryAsync(OnlineScope scope, string channelId, string cursor = null, int pageSize = 0)
            {
                return Service.GetHistoryAsync(scope, channelId, cursor, pageSize);
            }

            /// <summary>统计未读数。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="channelId">频道标识。</param>
            /// <returns>未读数结果。</returns>
            public Task<OnlineResult<int>> GetUnreadCountAsync(OnlineScope scope, string channelId)
            {
                return Service.GetUnreadCountAsync(scope, channelId);
            }

            /// <summary>标记已读。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="channelId">频道标识。</param>
            /// <param name="messageId">消息标识。</param>
            /// <returns>已读位点结果。</returns>
            public Task<OnlineResult<OnlineChatReadMark>> MarkReadAsync(OnlineScope scope, string channelId, string messageId)
            {
                return Service.MarkReadAsync(scope, channelId, messageId);
            }

            /// <summary>撤回消息。</summary>
            /// <param name="scope">作用域。</param>
            /// <param name="channelId">频道标识。</param>
            /// <param name="messageId">消息标识。</param>
            /// <returns>撤回结果。</returns>
            public Task<OnlineResult<OnlineChatMessage>> RecallAsync(OnlineScope scope, string channelId, string messageId)
            {
                return Service.RecallAsync(scope, channelId, messageId);
            }
        }

        /// <summary>
        /// 成员资格探针桩：仅白名单内玩家视为频道成员。
        /// </summary>
        private sealed class StubMembershipProbe : IOnlineChannelMembershipProbe
        {
            /// <summary>视为成员的玩家集合。</summary>
            private readonly HashSet<long> _members;

            /// <summary>
            /// 初始化 <see cref="StubMembershipProbe"/>。
            /// </summary>
            /// <param name="memberPlayerIds">视为成员的玩家标识。</param>
            public StubMembershipProbe(params long[] memberPlayerIds)
            {
                _members = new HashSet<long>(memberPlayerIds);
            }

            /// <summary>
            /// 仅判定玩家是否在构造时给定的成员白名单中，忽略频道类型与绑定主体。
            /// </summary>
            /// <remarks>
            /// Only checks whether the player is in the member whitelist supplied at
            /// construction, ignoring the channel kind and bound id.
            /// </remarks>
            /// <param name="tenantId">租户标识 / Tenant id</param>
            /// <param name="appId">App 标识 / App id</param>
            /// <param name="kind">频道类型 / The channel kind</param>
            /// <param name="boundId">绑定主体标识 / The bound id</param>
            /// <param name="playerId">待判定的玩家 / The player to check</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>在白名单中返回 true / True when present in the whitelist</returns>
            public Task<bool> IsChannelMemberAsync(long tenantId, long appId, OnlineChatChannelKind kind, string boundId, long playerId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(_members.Contains(playerId));
            }
        }

        /// <summary>
        /// 审核插件桩：一律拒绝。
        /// </summary>
        private sealed class RejectingModerationHook : IOnlineChatModerationHook
        {
            /// <summary>拒绝理由。</summary>
            private readonly string _reason;

            /// <summary>
            /// 初始化 <see cref="RejectingModerationHook"/>。
            /// </summary>
            /// <param name="reason">拒绝理由。</param>
            public RejectingModerationHook(string reason)
            {
                _reason = reason;
            }

            /// <summary>
            /// 一律返回以构造时给定理由的拒绝裁决。
            /// </summary>
            /// <remarks>
            /// Always returns a reject verdict carrying the reason supplied at construction.
            /// </remarks>
            /// <param name="message">待发送消息 / The message to moderate</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>恒为拒绝裁决 / Always a reject verdict</returns>
            public Task<OnlineChatModerationVerdict> CheckAsync(OnlineChatMessage message, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineChatModerationVerdict.Reject(_reason));
            }
        }

        /// <summary>
        /// 审核插件桩：一律抛异常（模拟审核服务故障）。
        /// </summary>
        private sealed class ThrowingModerationHook : IOnlineChatModerationHook
        {
            /// <summary>
            /// 一律抛出 <see cref="InvalidOperationException"/>，模拟审核服务不可用。
            /// </summary>
            /// <remarks>
            /// Always throws <see cref="InvalidOperationException"/> to simulate an unavailable moderation service.
            /// </remarks>
            /// <param name="message">待发送消息 / The message to moderate</param>
            /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
            /// <returns>此桩恒抛异常，不会正常返回 / This stub always throws and never returns normally</returns>
            /// <exception cref="InvalidOperationException">无条件抛出，模拟审核服务不可用 / Thrown unconditionally to simulate an unavailable moderation service</exception>
            public Task<OnlineChatModerationVerdict> CheckAsync(OnlineChatMessage message, CancellationToken cancellationToken = default)
            {
                throw new InvalidOperationException("审核服务不可用");
            }
        }
    }
}
