// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目 实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// Online admin API 调度器契约测试（双层信封、协议码、作用域守卫、幂等 Replay 透传与发放→账本→明细往返；
    /// 对齐 Admin 侧 OnlineServerClient 线缆契约）。C294 起调度器经真实族组合（<see cref="CapabilityComposition.Compose"/>）
    /// 后由 <see cref="OnlineCapabilityTestHarness.CreateDispatcher"/> 按机制同口径组装；业务面先启动（事件总线 Activate 前拒绝发布）。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineAdminApiDispatcherTests
    {
        /// <summary>
        /// 组合并启动 Online 族（授权作用域 租户 1 / 应用 1 / 区服 1001），以调度器执行用例主体，结束后恒停止。
        /// </summary>
        /// <param name="body">用例主体（入参为调度器）。</param>
        /// <returns>异步任务。</returns>
        private static Task WithDispatcherAsync(Func<AdminApiDispatcher, Task> body)
        {
            return OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                await body(OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host));
            });
        }

        /// <summary>
        /// 解析外层信封（Foundation camelCase 形态），取内层 JSON 字符串（data 字段）。
        /// </summary>
        /// <param name="envelopeJson">HTTP 响应体原文。</param>
        /// <returns>内层 JSON 字符串。</returns>
        private static string ReadInnerJson(string envelopeJson)
        {
            using (var document = JsonDocument.Parse(envelopeJson))
            {
                Assert.Equal(0, document.RootElement.GetProperty("code").GetInt32());
                var data = document.RootElement.GetProperty("data");
                Assert.Equal(JsonValueKind.String, data.ValueKind);
                return data.GetString();
            }
        }

        /// <summary>
        /// 断言内层业务码。
        /// </summary>
        /// <param name="innerJson">内层 JSON。</param>
        /// <param name="expectedCode">期望业务码。</param>
        private static void AssertInnerCode(string innerJson, OnlineErrorCode expectedCode)
        {
            using (var document = JsonDocument.Parse(innerJson))
            {
                Assert.Equal((int)expectedCode, document.RootElement.GetProperty("Code").GetInt32());
            }
        }

        /// <summary>
        /// 验证注册表 = 七能力 manifest 声明的全部业务 action + 三个机制元端点（双向对账后两边严格相等）。
        /// </summary>
        [Fact]
        public async Task ActionNames_ShouldRegisterAllThirtyFiveDeclaredActionsPlusThreeMetaEndpoints()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);

                // 业务 action 35 = assets 5（grant_asset / revoke_asset / query_wallet_ledger / query_asset_audit / query_transaction_detail）
                //   + foundation 1（suspend_resume_player）
                //   + social 8（kick_player / revoke_token / mute_player / query_report_cases / update_report_case /
                //     send_admin_notification / query_chat_messages / match_isolation）
                //   + matchmaking 4（query_match_queue_overview / query_match_ticket_failures / query_match_abnormal_tickets / cancel_match_ticket）
                //   + match 2（end_abnormal_match / query_match_settlement）
                //   + competition 8（LiveOps 7 + query_leaderboard）
                //   + operations 7（query_online_overview / query_player_timeline / query_online_audit_events + hotfix 受控操作族 4）；
                // 机制元端点 3：list_capabilities + list_capability_settings（调度器持有报告）+ list_capability_services（调度器持有服务面）。
                var declaredActions = runtime.Report.Entries.SelectMany(entry => entry.Manifest.Actions).ToList();
                Assert.Equal(35, declaredActions.Count);
                Assert.Equal(declaredActions.Count + 3, dispatcher.ActionNames.Count);
                Assert.Equal(38, dispatcher.ActionNames.Count);
                foreach (var action in declaredActions)
                {
                    Assert.Contains(action, dispatcher.ActionNames);
                }

                Assert.Contains("list_capabilities", dispatcher.ActionNames);
                Assert.Contains("list_capability_settings", dispatcher.ActionNames);
                Assert.Contains("list_capability_services", dispatcher.ActionNames);
                Assert.Contains("query_match_settlement", dispatcher.ActionNames);
                Assert.Contains("query_leaderboard", dispatcher.ActionNames);
                Assert.Contains("rollbackConfigRollout", dispatcher.ActionNames);
                Assert.Contains("rollback_hotfix_version", dispatcher.ActionNames);
                return Task.CompletedTask;
            });
        }

        /// <summary>
        /// 验证未知 action 返回内层 4002（HTTP 恒 200，外层恒 0）。
        /// </summary>
        [Fact]
        public async Task Dispatch_UnknownAction_ShouldReturnInner4002()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("definitely_not_an_action", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ResourceNotFound);
            });
        }

        /// <summary>
        /// 验证畸形 JSON 请求体返回内层 4001。
        /// </summary>
        [Fact]
        public async Task Dispatch_MalformedJson_ShouldReturnInner4001()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("grant_asset", "{\"GrantItems\": [oops");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ParameterInvalid);
            });
        }

        /// <summary>
        /// 验证作用域三元组缺失返回内层 3005。
        /// </summary>
        [Fact]
        public async Task Dispatch_MissingScope_ShouldReturnInner3005()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("query_online_overview", "{}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ScopeMissing);
            });
        }

        /// <summary>
        /// 验证跨租户 / 跨应用 / 跨区服分别返回 3002 / 3003 / 3004。
        /// </summary>
        /// <param name="body">请求体。</param>
        /// <param name="expectedCode">期望业务码。</param>
        [Theory]
        [InlineData("{\"TenantId\":2,\"AppId\":1,\"ServerId\":1001}", OnlineErrorCode.CrossTenantDenied)]
        [InlineData("{\"TenantId\":1,\"AppId\":2,\"ServerId\":1001}", OnlineErrorCode.CrossAppDenied)]
        [InlineData("{\"TenantId\":1,\"AppId\":1,\"ServerId\":2002}", OnlineErrorCode.ServerScopeDenied)]
        public async Task Dispatch_ScopeMismatch_ShouldReturnGuardCode(string body, OnlineErrorCode expectedCode)
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("query_online_overview", body);
                AssertInnerCode(ReadInnerJson(envelope), expectedCode);
            });
        }

        /// <summary>
        /// 验证 grant_asset 成功路径：内层 Code 0 + Data 非空 + 交易标识与幂等键回显（发放→账本→明细往返）。
        /// </summary>
        [Fact]
        public async Task Dispatch_GrantAsset_ShouldRoundTripThroughLedgerAndDetail()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var grantEnvelope = await dispatcher.DispatchAsync("grant_asset",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"PlayerId\":\"9001\",\"Reason\":\"admin-test\",\"IdempotencyKey\":\"GFX-GRANT-test-1\",\"GrantItems\":[{\"ItemOrCurrencyType\":\"Coin\",\"Quantity\":100}]}");
                string transactionId;
                using (var document = JsonDocument.Parse(ReadInnerJson(grantEnvelope)))
                {
                    var root = document.RootElement;
                    Assert.Equal(0, root.GetProperty("Code").GetInt32());
                    var data = root.GetProperty("Data");
                    Assert.Equal(JsonValueKind.Object, data.ValueKind);
                    transactionId = data.GetProperty("TransactionId").GetString();
                    Assert.False(string.IsNullOrEmpty(transactionId));
                    Assert.Equal("GFX-GRANT-test-1", data.GetProperty("IdempotencyKey").GetString());
                    Assert.Equal("Succeeded", data.GetProperty("Status").GetString());
                }

                var ledgerEnvelope = await dispatcher.DispatchAsync("query_wallet_ledger",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"PlayerId\":\"9001\",\"PageSize\":20}");
                using (var ledgerDocument = JsonDocument.Parse(ReadInnerJson(ledgerEnvelope)))
                {
                    var items = ledgerDocument.RootElement.GetProperty("Data").GetProperty("Items");
                    Assert.Equal(JsonValueKind.Array, items.ValueKind);
                    Assert.True(items.GetArrayLength() > 0);
                    Assert.Equal(transactionId, items[0].GetProperty("TransactionId").GetString());
                    Assert.Equal("9001", items[0].GetProperty("PlayerId").GetString());
                    Assert.Equal("Coin", items[0].GetProperty("CurrencyOrItemType").GetString());
                }

                var detailEnvelope = await dispatcher.DispatchAsync("query_transaction_detail",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"TransactionId\":\"" + transactionId + "\"}");
                using (var detailDocument = JsonDocument.Parse(ReadInnerJson(detailEnvelope)))
                {
                    var data = detailDocument.RootElement.GetProperty("Data");
                    Assert.Equal("GFX-GRANT-test-1", data.GetProperty("IdempotencyKey").GetString());
                    var grantItems = data.GetProperty("GrantItems");
                    Assert.Equal(1, grantItems.GetArrayLength());
                    Assert.Equal("Coin", grantItems[0].GetProperty("ItemOrCurrencyType").GetString());
                    Assert.Equal(100, grantItems[0].GetProperty("Quantity").GetInt64());
                }
            });
        }

        /// <summary>
        /// 验证 grant_asset 缺 PlayerId 返回内层 4001。
        /// </summary>
        [Fact]
        public async Task Dispatch_GrantAsset_WithoutPlayer_ShouldReturnInner4001()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("grant_asset",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"Reason\":\"admin-test\",\"GrantItems\":[{\"ItemOrCurrencyType\":\"Coin\",\"Quantity\":100}]}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ParameterInvalid);
            });
        }

        /// <summary>
        /// 验证 mute_player 幂等 Replay 原样透传首次响应（处罚族调度器层包裹）。
        /// </summary>
        [Fact]
        public async Task Dispatch_MutePlayer_TwiceWithSameKey_ShouldReplayFirstResponse()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                const string body = "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"PlayerId\":\"9002\",\"PenaltyId\":77,\"IdempotencyKey\":\"penalty-77-mute-1001\"}";
                var first = await dispatcher.DispatchAsync("mute_player", body);
                var second = await dispatcher.DispatchAsync("mute_player", body);
                Assert.Equal(first, second);
                using (var document = JsonDocument.Parse(ReadInnerJson(first)))
                {
                    var root = document.RootElement;
                    Assert.Equal(0, root.GetProperty("Code").GetInt32());
                    Assert.True(root.GetProperty("Data").GetProperty("TokenRevokedConfirmed").GetBoolean());
                }
            });
        }

        /// <summary>
        /// 验证 kick_player 无活跃会话时仍确认受理（玩家级语义：无会话 = 无需踢）。
        /// </summary>
        [Fact]
        public async Task Dispatch_KickPlayer_WithoutSessions_ShouldConfirm()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("kick_player",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"PlayerId\":\"9003\",\"PenaltyId\":78,\"IdempotencyKey\":\"penalty-78-kick-1001\"}");
                using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                {
                    var root = document.RootElement;
                    Assert.Equal(0, root.GetProperty("Code").GetInt32());
                    Assert.True(root.GetProperty("Data").GetProperty("TokenRevokedConfirmed").GetBoolean());
                }
            });
        }

        /// <summary>
        /// 验证 query_online_overview 空查询返回内层 0 + Data 非 null + 计数字段存在。
        /// </summary>
        [Fact]
        public async Task Dispatch_QueryOnlineOverview_ShouldReturnCounters()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("query_online_overview", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}");
                using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                {
                    var root = document.RootElement;
                    Assert.Equal(0, root.GetProperty("Code").GetInt32());
                    var data = root.GetProperty("Data");
                    Assert.Equal(JsonValueKind.Object, data.ValueKind);
                    Assert.True(data.GetProperty("OnlinePlayerCount").GetInt64() >= 0);
                    Assert.True(data.GetProperty("SessionCount").GetInt64() >= 0);
                    Assert.True(data.GetProperty("MatchCount").GetInt64() >= 0);
                    Assert.Equal(JsonValueKind.Array, data.GetProperty("QueueSummaries").ValueKind);
                    Assert.True(data.GetProperty("ServerTime").GetInt64() > 0);
                }
            });
        }

        /// <summary>
        /// 验证 end_abnormal_match 固定返回 5003。
        /// </summary>
        [Fact]
        public async Task Dispatch_EndAbnormalMatch_ShouldReturnInner5003()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("end_abnormal_match",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"MatchId\":\"m-1\",\"Reason\":\"test\",\"ReviewerOperatorId\":\"admin\",\"IdempotencyKey\":\"onlineControlled-end_abnormal_match-m-1-1001\"}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.StateOperationForbidden);
            });
        }

        /// <summary>
        /// 验证 query_player_timeline 空时间线返回空事件列表（游标 null 字段按序列化器约定省略）。
        /// </summary>
        [Fact]
        public async Task Dispatch_QueryPlayerTimeline_Empty_ShouldReturnEmptyEvents()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("query_player_timeline",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"PlayerId\":\"9004\"}");
                using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                {
                    var data = document.RootElement.GetProperty("Data");
                    Assert.Equal(0, data.GetProperty("Events").GetArrayLength());
                    Assert.False(data.GetProperty("HasMore").GetBoolean());
                }
            });
        }

        /// <summary>
        /// 验证 publishRemoteConfig（LiveOps 族）成功返回确认版本。
        /// </summary>
        [Fact]
        public async Task Dispatch_PublishRemoteConfig_ShouldReturnConfirmVersion()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("publishRemoteConfig",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"ConfigId\":5001,\"Reason\":\"admin-test\"}");
                using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                {
                    var root = document.RootElement;
                    Assert.Equal(0, root.GetProperty("Code").GetInt32());
                    var data = root.GetProperty("Data");
                    Assert.False(string.IsNullOrEmpty(data.GetProperty("ConfirmVersion").GetString()));
                    Assert.True(data.GetProperty("ServerTime").GetInt64() > 0);
                }
            });
        }

        /// <summary>
        /// 验证 publishConfigRollout 灰度百分比超界（101 / -1）返回内层 4001（边界校验拒绝非法值）。
        /// </summary>
        [Fact]
        public async Task Dispatch_PublishConfigRollout_PercentOutOfRange_ShouldReturnInner4001()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("publishConfigRollout",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"ConfigKind\":1,\"ConfigKey\":\"remote-config-key\",\"ConfigVersion\":3,\"Percent\":101,\"Reason\":\"admin-test\"}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ParameterInvalid);

                envelope = await dispatcher.DispatchAsync("publishConfigRollout",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"ConfigKind\":1,\"ConfigKey\":\"remote-config-key\",\"ConfigVersion\":3,\"Percent\":-1,\"Reason\":\"admin-test\"}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ParameterInvalid);
            });
        }

        /// <summary>
        /// 验证 publishConfigRollout 灰度百分比合法边界（0 / 100 / 缺省）成功返回确认版本。
        /// </summary>
        [Fact]
        public async Task Dispatch_PublishConfigRollout_PercentBoundary_ShouldReturnConfirmVersion()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                foreach (var percent in new[] { "\"Percent\":0,", "\"Percent\":100,", "" })
                {
                    var envelope = await dispatcher.DispatchAsync("publishConfigRollout",
                        "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"ConfigKind\":1,\"ConfigKey\":\"remote-config-key\",\"ConfigVersion\":3," + percent + "\"Reason\":\"admin-test\"}");
                    using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                    {
                        var root = document.RootElement;
                        Assert.Equal(0, root.GetProperty("Code").GetInt32());
                        Assert.False(string.IsNullOrEmpty(root.GetProperty("Data").GetProperty("ConfirmVersion").GetString()));
                    }
                }
            });
        }

        /// <summary>
        /// 验证 suspend_resume_player 缺 Direction 返回内层 4001（受控双向命令必填）。
        /// </summary>
        [Fact]
        public async Task Dispatch_SuspendResumePlayer_WithoutDirection_ShouldReturnInner4001()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("suspend_resume_player",
                    "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"PlayerId\":\"9005\",\"Reason\":\"test\",\"ReviewerOperatorId\":\"admin\",\"IdempotencyKey\":\"onlineControlled-suspend_resume_player-9005-1001\"}");
                AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.ParameterInvalid);
            });
        }

        /// <summary>
        /// 验证 query_report_cases 无过滤返回空案件列表（Data 非 null）。
        /// </summary>
        [Fact]
        public async Task Dispatch_QueryReportCases_Empty_ShouldReturnEmptyItems()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("query_report_cases", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}");
                using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                {
                    var data = document.RootElement.GetProperty("Data");
                    Assert.Equal(0, data.GetProperty("Items").GetArrayLength());
                    Assert.False(data.GetProperty("HasMore").GetBoolean());
                }
            });
        }

        /// <summary>
        /// 验证 query_match_queue_overview 返回队列数组（空快照 = 空数组）。
        /// </summary>
        [Fact]
        public async Task Dispatch_QueryMatchQueueOverview_ShouldReturnQueuesArray()
        {
            await WithDispatcherAsync(async dispatcher =>
            {
                var envelope = await dispatcher.DispatchAsync("query_match_queue_overview", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}");
                using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
                {
                    var data = document.RootElement.GetProperty("Data");
                    Assert.Equal(JsonValueKind.Array, data.GetProperty("Queues").ValueKind);
                    Assert.True(data.GetProperty("ServerTime").GetInt64() > 0);
                }
            });
        }
    }
}
