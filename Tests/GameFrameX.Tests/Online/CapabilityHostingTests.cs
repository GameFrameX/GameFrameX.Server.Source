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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Contracts;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 能力机制层测试（拓扑定序稳定性 / 闭包校验 fail-fast / 装配过滤器 / 报告与扫描发现 /
    /// Online 过渡装配的报告口径与 admin-only 未安装裁决）。
    /// </summary>
    public class CapabilityHostingTests
    {
        /// <summary>
        /// 测试模块甲（无依赖）。
        /// </summary>
        private sealed class AlphaModule : ICapabilityModule
        {
            public CapabilityManifest Manifest { get; } = new("test-alpha", "1.0.0", new[] { "alpha_action" });

            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// 测试模块乙（依赖甲）。
        /// </summary>
        private sealed class BetaModule : ICapabilityModule
        {
            public CapabilityManifest Manifest { get; } = new("test-beta", "1.0.0", Array.Empty<string>(), new[] { "test-alpha" });

            public void Register(CapabilityRegistrationContext context)
            {
            }
        }

        /// <summary>
        /// 构造合法选项。
        /// </summary>
        /// <returns>选项。</returns>
        private static CapabilityHostOptions ValidOptions()
        {
            return new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 };
        }

        /// <summary>
        /// 构造 Online 单入口组合产物（业务运行时形态；授权三元组 1/1/1001，C232 P6 起真扫描装配）。
        /// </summary>
        /// <returns>组合产物。</returns>
        private static OnlineCapabilityComposition CreateComposition()
        {
            return OnlineCapabilityComposition.AddOnlineCapabilities(options =>
            {
                options.TenantId = 1;
                options.AppId = 1;
                options.ServerId = 1001;
                options.EnableRuntime = true;
            });
        }

        /// <summary>
        /// 拓扑定序：依赖方排在被依赖方之后，同层保持加入序。
        /// </summary>
        [Fact]
        public void Build_OrdersByRequiresAndKeepsInsertionOrderWithinLevel()
        {
            var builder = new CapabilityHostBuilder();
            builder.AddModule(new BetaModule());
            builder.AddModule(new AlphaModule());

            var host = builder.Build(ValidOptions());

            Assert.Equal(new[] { "test-alpha", "test-beta" }, host.Report.EnabledIdsInOrder);
        }

        /// <summary>
        /// 闭包校验 fail-fast：Requires 指向未装配能力即抛。
        /// </summary>
        [Fact]
        public void Build_MissingRequirement_FailsFast()
        {
            var builder = new CapabilityHostBuilder();
            builder.AddModule(new BetaModule());

            Assert.Throws<InvalidOperationException>(() => builder.Build(ValidOptions()));
        }

        /// <summary>
        /// manifest Id 重复即装配失败。
        /// </summary>
        [Fact]
        public void Build_DuplicateCapabilityId_FailsFast()
        {
            var builder = new CapabilityHostBuilder();
            builder.AddModule(new AlphaModule());
            builder.AddModule(new AlphaModule());

            Assert.Throws<InvalidOperationException>(() => builder.Build(ValidOptions()));
        }

        /// <summary>
        /// 过滤器引用未发现的能力 Id 即装配失败。
        /// </summary>
        [Fact]
        public void Build_FilterTargetMissing_FailsFast()
        {
            var builder = new CapabilityHostBuilder();
            builder.AddModule(new AlphaModule());
            builder.SetFilter(new CapabilityAssemblyFilter(disabledIds: new[] { "no-such-capability" }));

            Assert.Throws<InvalidOperationException>(() => builder.Build(ValidOptions()));
        }

        /// <summary>
        /// 排除表与清单段语义：Disable 命中不装配；Capabilities 白名单未收录不装配；
        /// 被排除能力的依赖方也因闭包校验失败（本例乙无甲即 fail-fast）。
        /// </summary>
        [Fact]
        public void Build_DisabledCapability_ExcludedFromReportAndEnabledOrder()
        {
            var builder = new CapabilityHostBuilder();
            builder.AddModule(new AlphaModule());
            builder.SetFilter(new CapabilityAssemblyFilter(disabledIds: new[] { "test-alpha" }));

            var host = builder.Build(ValidOptions());

            Assert.Empty(host.Report.EnabledIdsInOrder);
            var entry = Assert.Single(host.Report.Entries);
            Assert.Equal(CapabilityFilterState.ExcludedByDisableList, entry.State);
        }

        /// <summary>
        /// 白名单形态：清单段未收录的能力标记 NotInEnableList，收录的照常装配。
        /// </summary>
        [Fact]
        public void Build_EnableList_FiltersUnlistedCapabilities()
        {
            var builder = new CapabilityHostBuilder();
            builder.AddModule(new AlphaModule());
            builder.AddModule(new BetaModule());
            builder.SetFilter(new CapabilityAssemblyFilter(enabledIds: new[] { "test-alpha" }));

            var host = builder.Build(ValidOptions());

            Assert.Equal(new[] { "test-alpha" }, host.Report.EnabledIdsInOrder);
            Assert.Equal(CapabilityFilterState.NotInEnableList, host.Report.Entries.Single(e => e.Manifest.Id == "test-beta").State);
        }

        /// <summary>
        /// 扫描发现：程序集扫描能发现测试程序集内的 ICapabilityModule 实现（机制开放性——无硬编码清单）。
        /// </summary>
        [Fact]
        public void DiscoverModules_FindsModulesFromAssembly()
        {
            var modules = CapabilityAssembly.DiscoverModules(new[] { typeof(CapabilityHostingTests).Assembly });

            Assert.Contains(modules, m => m.Manifest.Id == "test-alpha");
        }

        /// <summary>
        /// 选项非法（作用域三元组非正）即构造失败。
        /// </summary>
        [Fact]
        public void CapabilityHost_InvalidOptions_Throws()
        {
            Assert.Throws<InvalidOperationException>(() => new CapabilityHostBuilder().Build(new CapabilityHostOptions { TenantId = 0, AppId = 1, ServerId = 1 }));
        }

        /// <summary>
        /// Online 真扫描装配报告：7 个终态包 Id 全部启用，拓扑序满足 Requires 方向（依赖方后置；
        /// C232 P6 起经 Launcher 单入口扫描装配，装配序 = 扫描发现序 + Requires 拓扑）。
        /// </summary>
        [Fact]
        public void OnlineHost_ReportEnablesAllSevenCapabilitiesInTopologicalOrder()
        {
            var composition = OnlineCapabilityComposition.AddOnlineCapabilities(options =>
            {
                options.TenantId = 1;
                options.AppId = 1;
                options.ServerId = 1001;
                options.EnableRuntime = true;
            });

            var order = composition.Runtime.Report.EnabledIdsInOrder;
            Assert.Equal(new[]
            {
                "online-assets", "online-foundation", "online-competition", "online-match",
                "online-social", "online-matchmaking", "online-operations",
            }, order);
        }

        /// <summary>
        /// admin-only 形态：零能力装配，全部业务 action 按未安装（4003）裁决且载荷带已装清单（空），
        /// list_capabilities 如实返回全目录（NotAssembled）。
        /// </summary>
        [Fact]
        public async Task AdminOnlyDispatcher_AnswersNotInstalledAndListsCatalogAsync()
        {
            var dispatcher = OnlineAdminApiComposition.CreateAdminOnlyDispatcher(ValidOptions());

            var notInstalled = await dispatcher.DispatchAsync("grant_asset", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1,\"RequestId\":\"r1\"}");
            using (var outer = JsonDocument.Parse(notInstalled))
            {
                var innerJson = outer.RootElement.GetProperty("data").GetString();
                using (var inner = JsonDocument.Parse(innerJson))
                {
                    Assert.Equal((int)OnlineErrorCode.CapabilityNotInstalled, inner.RootElement.GetProperty("Code").GetInt32());
                    Assert.Equal("Online.Error.Param.CapabilityNotInstalled", inner.RootElement.GetProperty("MessageKey").GetString());
                    Assert.Equal("r1", inner.RootElement.GetProperty("RequestId").GetString());
                    using (var payload = JsonDocument.Parse(inner.RootElement.GetProperty("Data").GetRawText()))
                    {
                        Assert.Equal("online-assets", payload.RootElement.GetProperty("NotInstalledCapability").GetString());
                        Assert.Empty(payload.RootElement.GetProperty("InstalledCapabilities").EnumerateArray());
                    }
                }
            }

            var listing = await dispatcher.DispatchAsync("list_capabilities", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1}");
            using (var outer = JsonDocument.Parse(listing))
            {
                var innerJson = outer.RootElement.GetProperty("data").GetString();
                using (var inner = JsonDocument.Parse(innerJson))
                {
                    var capabilities = inner.RootElement.GetProperty("Data").GetProperty("Capabilities");
                    Assert.Equal(7, capabilities.GetArrayLength());
                    Assert.All(capabilities.EnumerateArray(), entry => Assert.Equal("NotAssembled", entry.GetProperty("State").GetString()));
                }
            }
        }

        /// <summary>
        /// 未安装裁决与未知 action 的区分：目录外的 action 仍按 4002 未知处理。
        /// </summary>
        [Fact]
        public async Task AdminOnlyDispatcher_UnknownActionOutsideCatalog_Answers4002Async()
        {
            var dispatcher = OnlineAdminApiComposition.CreateAdminOnlyDispatcher(ValidOptions());

            var response = await dispatcher.DispatchAsync("no_such_action", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1}");
            using (var outer = JsonDocument.Parse(response))
            {
                var innerJson = outer.RootElement.GetProperty("data").GetString();
                using (var inner = JsonDocument.Parse(innerJson))
                {
                    Assert.Equal((int)OnlineErrorCode.ResourceNotFound, inner.RootElement.GetProperty("Code").GetInt32());
                }
            }
        }

        /// <summary>
        /// 业务形态的 list_capabilities：29 个业务 action 之外返回全部启用能力与装配顺序。
        /// </summary>
        [Fact]
        public async Task FullComposition_ListCapabilities_ReportsEnabledOrderAsync()
        {
            var dispatcher = OnlineAdminApiComposition.CreateDispatcher(CreateComposition().Runtime);

            var response = await dispatcher.DispatchAsync("list_capabilities", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}");
            using (var outer = JsonDocument.Parse(response))
            {
                var innerJson = outer.RootElement.GetProperty("data").GetString();
                using (var inner = JsonDocument.Parse(innerJson))
                {
                    var data = inner.RootElement.GetProperty("Data");
                    Assert.Equal(7, data.GetProperty("Capabilities").GetArrayLength());
                    Assert.Equal(new[]
                    {
                        "online-assets", "online-foundation", "online-competition", "online-match",
                        "online-social", "online-matchmaking", "online-operations",
                    }, data.GetProperty("EnabledIdsInOrder").EnumerateArray().Select(e => e.GetString()).ToArray());
                }
            }
        }

        /// <summary>
        /// job 全名防重复：同能力内重复 jobId 即注册失败。
        /// </summary>
        [Fact]
        public void RegistrationContext_DuplicateJobName_FailsFast()
        {
            var context = new CapabilityRegistrationContext(new CapabilityManifest("dup", "1.0.0"));

            context.AddTickJob("sweep", _ => Task.CompletedTask);

            Assert.Throws<InvalidOperationException>(() => context.AddTickJob("sweep", _ => Task.CompletedTask));
        }
    }
}
