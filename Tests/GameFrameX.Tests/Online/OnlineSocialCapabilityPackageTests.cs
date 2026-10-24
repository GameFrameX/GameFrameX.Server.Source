// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 社交能力包验收测试（C294 机制化）：无参模块扫描可发现 / 真实族白名单 foundation+social 装配、产物与 gate 面经宿主共享服务面取用 /
    /// 宿主 Override 替换缝恒胜出包内默认聊天存储 / Configure 代码层覆盖 Options 字段 / 群组探针 Activate 期注入扩展点 /
    /// 聊天审核钩子声明默认放行、热更侧 Attach 原子替换、Dispose 回默认。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineSocialCapabilityPackageTests
    {
        /// <summary>发言玩家。</summary>
        private const long PlayerOne = 2101;

        /// <summary>
        /// 白名单圈定 foundation + social（social 必选 foundation，二者构成闭包）。
        /// </summary>
        /// <param name="options">组合选项。</param>
        private static void SocialClosure(CapabilityCompositionOptions options)
        {
            options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social" });
        }

        /// <summary>
        /// 构造玩家作用域（三元组与族配置段一致）。
        /// </summary>
        /// <param name="playerId">玩家标识。</param>
        /// <returns>玩家作用域。</returns>
        private static OnlineScope PlayerScope(long playerId)
        {
            return new OnlineScope(OnlineCapabilityTestHarness.TenantId, OnlineCapabilityTestHarness.AppId, OnlineCapabilityTestHarness.ServerId, playerId);
        }

        /// <summary>
        /// 无参模块扫描可发现：社交包程序集扫描产出唯一的 online-social 模块。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsSocialCapabilityModule()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(OnlineSocialCapabilityModule).Assembly });

            var module = Assert.Single(modules);
            Assert.Equal("online-social", module.Manifest.Id);
        }

        /// <summary>
        /// 白名单 foundation+social 装配：好友 / 裁决 / 处罚 / 通知 / 聊天审计 / 聊天 / 群组服务与 gate 面经宿主共享服务面可取用，
        /// gate 面与裁决服务同一实例，装配序 foundation 先于 social。
        /// </summary>
        [Fact]
        public async Task Compose_FoundationAndSocial_ExposesProductsAndGate()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var services = runtime.Host.Services;
                Assert.NotNull(services.GetService<OnlineFriendService>());
                Assert.NotNull(services.GetService<OnlinePunishmentService>());
                Assert.NotNull(services.GetService<OnlineNotificationService>());
                Assert.NotNull(services.GetService<OnlineChatAuditProjection>());
                Assert.NotNull(services.GetService<OnlineChatService>());
                Assert.NotNull(services.GetService<OnlineGroupService>());
                var decisions = services.GetService<OnlineSocialDecisionService>();
                Assert.NotNull(decisions);
                Assert.Same(decisions, services.GetService<IOnlineSocialGate>());
                Assert.Equal(new[] { "online-foundation", "online-social" }, runtime.Report.EnabledIdsInOrder);
                return Task.CompletedTask;
            }, configure: SocialClosure);
        }

        /// <summary>
        /// 存储替换缝：宿主 Override 的聊天存储恒胜出包内 InMemory 默认。
        /// </summary>
        [Fact]
        public async Task OverrideChatStore_WinsOverInMemoryDefault()
        {
            var replacement = new InMemoryOnlineChatStore();

            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                Assert.Same(replacement, runtime.Host.Services.GetService<IOnlineChatStore>());
                return Task.CompletedTask;
            }, configure: options =>
            {
                SocialClosure(options);
                options.Override<IOnlineChatStore>(replacement);
            });
        }

        /// <summary>
        /// 包级 Options 经 Configure 代码层覆盖：被改写字段报告来源 Code 且生效值为改写值，未改写字段保持包内默认。
        /// </summary>
        [Fact]
        public async Task ConfigureSocialOptions_AppliesCodeOverrideVisibleInReport()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var entry = Assert.Single(runtime.Report.Entries, candidate => candidate.Manifest.Id == "online-social");
                Assert.Equal(typeof(OnlineSocialOptions), entry.Manifest.OptionsType);

                var configured = Assert.Single(entry.SettingsFields, candidate => candidate.FieldPath == "Friend.SearchLimit");
                Assert.Equal(7, (int)configured.EffectiveValue);
                Assert.Equal(CapabilitySettingsFieldSource.Code, configured.Source);

                var untouched = Assert.Single(entry.SettingsFields, candidate => candidate.FieldPath == "Friend.FriendLimit");
                Assert.Equal(200, (int)untouched.EffectiveValue);
                Assert.Equal(CapabilitySettingsFieldSource.Default, untouched.Source);
                return Task.CompletedTask;
            }, configure: options =>
            {
                SocialClosure(options);
                options.Configure<OnlineSocialOptions>(social => social.Friend.SearchLimit = 7);
            });
        }

        /// <summary>
        /// 群组成员资格探针：以 "group" 命名注册；扩展点枚举只在 Activate 开始后可用，启动后枚举结果包含群组服务。
        /// </summary>
        [Fact]
        public async Task Start_ExposesGroupMembershipProbeThroughExtensionPoint()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                var services = runtime.Host.Services;
                Assert.Throws<InvalidOperationException>(() => services.GetServices<IOnlineChannelMembershipProbe>());

                await runtime.Host.StartAsync();

                var groups = services.GetService<OnlineGroupService>();
                Assert.Same(groups, services.GetService<IOnlineChannelMembershipProbe>("group"));
                Assert.Contains<IOnlineChannelMembershipProbe>(groups, services.GetServices<IOnlineChannelMembershipProbe>());
            }, configure: SocialClosure);
        }

        /// <summary>
        /// 聊天审核钩子：默认放行；Attach 后逐调用读到挂载实现（拒绝）；再次 Attach 原子替换且旧租约 Dispose 为空操作；
        /// 当前租约 Dispose 后回默认放行策略。
        /// </summary>
        [Fact]
        public async Task ModerationHook_AttachReplaceDispose_ChangesChatBehavior()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();
                var chat = runtime.Host.Services.GetService<OnlineChatService>();
                var channel = await chat.OpenGlobalChannelAsync(PlayerScope(PlayerOne));
                Assert.True(channel.IsSuccess, channel.Message);
                var channelId = channel.Data.ChannelId;

                var allowedByDefault = await chat.SendAsync(PlayerScope(PlayerOne), channelId, "默认放行");
                Assert.True(allowedByDefault.IsSuccess, allowedByDefault.Message);

                CapabilityHookLease<IOnlineChatModerationHook> firstLease = null;
                CapabilityHookLease<IOnlineChatModerationHook> secondLease = null;
                try
                {
                    firstLease = CapabilityHook<IOnlineChatModerationHook>.Attach(new RejectingModerationHook("first"));
                    secondLease = CapabilityHook<IOnlineChatModerationHook>.Attach(new RejectingModerationHook("second"));

                    var rejected = await chat.SendAsync(PlayerScope(PlayerOne), channelId, "被替换后的审核拒绝");
                    Assert.False(rejected.IsSuccess);
                    Assert.Equal(OnlineErrorCode.RiskControlRejected, rejected.Code);
                    Assert.Contains("second", rejected.Message);

                    // 已被替换的旧租约 Dispose 为空操作：当前挂载仍生效。
                    firstLease.Dispose();
                    var stillRejected = await chat.SendAsync(PlayerScope(PlayerOne), channelId, "旧租约摘下无效");
                    Assert.False(stillRejected.IsSuccess);
                    Assert.Equal(OnlineErrorCode.RiskControlRejected, stillRejected.Code);

                    // 当前租约 Dispose：回声明的默认放行策略。
                    secondLease.Dispose();
                    var allowedAgain = await chat.SendAsync(PlayerScope(PlayerOne), channelId, "回默认放行");
                    Assert.True(allowedAgain.IsSuccess, allowedAgain.Message);
                }
                finally
                {
                    firstLease?.Dispose();
                    secondLease?.Dispose();
                }
            }, configure: SocialClosure);
        }

        /// <summary>
        /// 恒拒绝审核钩子桩（模拟热更侧挂载的审核插件；拒绝理由用于区分哪一个挂载生效）。
        /// </summary>
        private sealed class RejectingModerationHook : IOnlineChatModerationHook
        {
            /// <summary>拒绝理由。</summary>
            private readonly string _reason;

            /// <summary>
            /// 初始化恒拒绝审核钩子桩。
            /// </summary>
            /// <param name="reason">拒绝理由。</param>
            public RejectingModerationHook(string reason)
            {
                _reason = reason;
            }

            /// <summary>
            /// 审核聊天消息；本桩不看消息内容，始终以构造时给定的理由返回拒绝判定，用于验证拒绝路径。
            /// </summary>
            /// <remarks>
            /// Moderates a chat message; this stub ignores the content and always returns a rejection verdict with the configured reason.
            /// </remarks>
            /// <param name="message">待审核的聊天消息（本桩忽略） / Chat message to moderate (ignored by this stub)</param>
            /// <param name="cancellationToken">取消令牌（本桩忽略） / Cancellation token (ignored by this stub)</param>
            /// <returns>携带固定拒绝理由的拒绝判定 / Rejection verdict carrying the fixed reason</returns>
            public Task<OnlineChatModerationVerdict> CheckAsync(OnlineChatMessage message, CancellationToken cancellationToken = default)
            {
                return Task.FromResult(OnlineChatModerationVerdict.Reject(_reason));
            }
        }
    }
}
