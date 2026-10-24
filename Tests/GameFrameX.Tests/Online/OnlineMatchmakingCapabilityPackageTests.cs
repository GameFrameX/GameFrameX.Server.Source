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

using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Foundation.Presence;
using GameFrameX.Capability.Online.Matchmaking;
using GameFrameX.Capability.Online.Matchmaking.Party;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 匹配能力包（含 Party 域）验收测试（C294 机制化）：无参模块扫描可发现 / 真实族白名单 foundation+social+matchmaking 装配、
    /// 票据 / 撮合 / 观察器 / 队伍产物经宿主共享服务面取用 / 宿主 Override 替换缝恒胜出包内默认票据存储 /
    /// Configure 与配置段 Settings 承载 Options / 队伍以 "party" 命名注册成员资格探针、Activate 期枚举注入在线探针。
    /// </summary>
    [Collection(CapabilityRuntimeCollection.Name)]
    public class OnlineMatchmakingCapabilityPackageTests
    {
        /// <summary>
        /// 白名单圈定 foundation + social + matchmaking（matchmaking 必选 social、social 必选 foundation）。
        /// </summary>
        /// <param name="options">组合选项。</param>
        private static void MatchmakingClosure(CapabilityCompositionOptions options)
        {
            options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social", "online-matchmaking" });
        }

        /// <summary>
        /// 无参模块扫描可发现：匹配包程序集扫描产出唯一的 online-matchmaking 模块。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsMatchmakingCapabilityModule()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(OnlineMatchmakingCapabilityModule).Assembly });

            var module = Assert.Single(modules);
            Assert.Equal("online-matchmaking", module.Manifest.Id);
        }

        /// <summary>
        /// 白名单闭包装配：票据服务 / 撮合协调器 / 队列观察器 / 票据与队伍存储 / 队伍服务经宿主共享服务面可取用，
        /// 装配序 foundation → social → matchmaking。
        /// </summary>
        [Fact]
        public async Task Compose_MatchmakingClosure_ExposesProductsIncludingPartyService()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var services = runtime.Host.Services;
                Assert.NotNull(services.GetService<OnlineMatchTicketService>());
                Assert.NotNull(services.GetService<OnlineMatchmakerCoordinator>());
                Assert.NotNull(services.GetService<OnlineMatchQueueObserver>());
                Assert.NotNull(services.GetService<IOnlineMatchTicketStore>());
                Assert.NotNull(services.GetService<IOnlinePartyStore>());
                Assert.NotNull(services.GetService<OnlinePartyService>());
                Assert.Equal(new[] { "online-foundation", "online-social", "online-matchmaking" }, runtime.Report.EnabledIdsInOrder);
                return Task.CompletedTask;
            }, configure: MatchmakingClosure);
        }

        /// <summary>
        /// 存储替换缝：宿主 Override 的票据存储恒胜出包内 InMemory 默认。
        /// </summary>
        [Fact]
        public async Task OverrideMatchTicketStore_WinsOverInMemoryDefault()
        {
            var replacement = new InMemoryOnlineMatchTicketStore();

            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                Assert.Same(replacement, runtime.Host.Services.GetService<IOnlineMatchTicketStore>());
                return Task.CompletedTask;
            }, configure: options =>
            {
                MatchmakingClosure(options);
                options.Override<IOnlineMatchTicketStore>(replacement);
            });
        }

        /// <summary>
        /// 包级 Options 承载：Configure 改写字段报告来源 Code；配置段 Settings 写入的字段生效值为配置值；未触碰字段保持默认。
        /// </summary>
        [Fact]
        public async Task MatchmakingOptions_CarriedThroughSettingsAndConfigure()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
            {
                var entry = Assert.Single(runtime.Report.Entries, candidate => candidate.Manifest.Id == "online-matchmaking");
                Assert.Equal(typeof(OnlineMatchmakingOptions), entry.Manifest.OptionsType);

                var configured = Assert.Single(entry.SettingsFields, candidate => candidate.FieldPath == "Matchmaker.TicketTimeToLiveSeconds");
                Assert.Equal(120, (int)configured.EffectiveValue);
                Assert.Equal(CapabilitySettingsFieldSource.Code, configured.Source);

                var fromSection = Assert.Single(entry.SettingsFields, candidate => candidate.FieldPath == "Matchmaker.RateLimitMaxOperations");
                Assert.Equal(4, (int)fromSection.EffectiveValue);

                var untouched = Assert.Single(entry.SettingsFields, candidate => candidate.FieldPath == "Matchmaker.WaitExpansionThresholdSeconds");
                Assert.Equal(30, (int)untouched.EffectiveValue);
                Assert.Equal(CapabilitySettingsFieldSource.Default, untouched.Source);
                return Task.CompletedTask;
            }, "{\"online-matchmaking\":{\"Matchmaker\":{\"RateLimitMaxOperations\":4}}}", options =>
            {
                MatchmakingClosure(options);
                options.Configure<OnlineMatchmakingOptions>(matchmaking => matchmaking.Matchmaker.TicketTimeToLiveSeconds = 120);
            });
        }

        /// <summary>
        /// 队伍探针：队伍服务以 "party" 命名注册成员资格探针（聊天队伍频道判定面）；
        /// 启动后在线探针扩展点可枚举到 foundation 提供的队伍在线探针（Activate 期注入队伍服务）。
        /// </summary>
        [Fact]
        public async Task Start_ExposesPartyProbes()
        {
            await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
            {
                await runtime.Host.StartAsync();

                var services = runtime.Host.Services;
                var parties = services.GetService<OnlinePartyService>();
                Assert.Same(parties, services.GetService<IOnlineChannelMembershipProbe>("party"));
                Assert.Contains<IOnlineChannelMembershipProbe>(parties, services.GetServices<IOnlineChannelMembershipProbe>());

                var presenceProbe = Assert.Single(services.GetServices<IOnlinePartyPresenceProbe>());
                Assert.IsType<OnlinePartyPresenceProbe>(presenceProbe);
            }, configure: MatchmakingClosure);
        }
    }
}
