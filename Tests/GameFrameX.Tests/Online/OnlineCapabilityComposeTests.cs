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
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Foundation.Localization.Core;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Online 能力族单入口组合测试（C294：<see cref="CapabilityComposition.Compose"/> + <see cref="OnlineCapabilityFamily"/>）：
/// 双面形态（业务面 / 管理面 / 双面）、过滤器形态与规整（空列表 / 空白条目 / 首尾空白）、组合期 fail-fast
///（未知 Id / 闭包缺口 / 无面启用 / 作用域与选项非法 / 宿主扩展点缺失）、钩子声明释放回归、
/// 机制元端点（list_capabilities / list_capability_services / list_capability_settings）、可选依赖缺失披露与 Options 来源。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public class OnlineCapabilityComposeTests
{
    /// <summary>
    /// 合法作用域三元组请求体（与族配置段授权作用域一致）。
    /// </summary>
    private const string ScopeBody = "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}";

    /// <summary>
    /// 管理面测试专用端口（避开 28190 / 28191 的 Server / RateLimit 用例与默认 28090）。
    /// </summary>
    private const int AdminTestPort = 28196;

    /// <summary>
    /// 好友 Options 默认搜索上限（<see cref="OnlineFriendOptions.SearchLimit"/> 包内默认值）。
    /// </summary>
    private const int DefaultFriendSearchLimit = 20;

    /// <summary>
    /// 好友 Options 默认好友上限（<see cref="OnlineFriendOptions.FriendLimit"/> 包内默认值）。
    /// </summary>
    private const int DefaultFriendLimit = 200;

    /// <summary>
    /// HTTP 客户端（共享连接池）。
    /// </summary>
    private static readonly HttpClient SharedHttpClient = new HttpClient();

    /// <summary>
    /// 七能力全装配的拓扑序（依赖边：social→foundation、matchmaking→social、match 可选 assets/matchmaking、
    /// competition 可选 assets/match、operations→foundation 可选 assets/social/match/matchmaking；同层按能力 Id Ordinal）。
    /// </summary>
    private static readonly string[] ExpectedTopologicalOrder =
    {
        "online-assets", "online-foundation", "online-social", "online-matchmaking",
        "online-match", "online-competition", "online-operations",
    };

    /// <summary>
    /// 构造不带任何宿主 Override 的组合选项（宿主扩展点缺失 / 自定义 Override 用例自行补齐）。
    /// </summary>
    /// <param name="familySection">族配置段节点。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateBareOptions(JsonElement familySection)
    {
        return new CapabilityCompositionOptions(familySection, new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions
            {
                TenantId = OnlineCapabilityTestHarness.TenantId,
                AppId = OnlineCapabilityTestHarness.AppId,
                ServerId = OnlineCapabilityTestHarness.ServerId,
            },
        };
    }

    /// <summary>
    /// 以给定选项组合 Online 族并在主体结束后恒停止（撤桥 + 释放钩子声明）。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <param name="body">用例主体。</param>
    /// <returns>异步任务。</returns>
    private static async Task WithComposedAsync(CapabilityCompositionOptions options, Func<CapabilityRuntime, Task> body)
    {
        var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
        try
        {
            await body(runtime);
        }
        finally
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// 解析外层信封（HTTP 状态恒 200、外层 code 恒 0），返回内层业务文档（调用方负责释放）。
    /// </summary>
    /// <param name="envelopeJson">响应体原文。</param>
    /// <returns>内层业务 JSON 文档。</returns>
    private static JsonDocument ParseInner(string envelopeJson)
    {
        using (var outer = JsonDocument.Parse(envelopeJson))
        {
            Assert.Equal(0, outer.RootElement.GetProperty("code").GetInt32());
            return JsonDocument.Parse(outer.RootElement.GetProperty("data").GetString());
        }
    }

    /// <summary>
    /// 读取 JSON 字符串数组。
    /// </summary>
    /// <param name="array">JSON 数组节点。</param>
    /// <returns>字符串数组。</returns>
    private static string[] ReadStrings(JsonElement array)
    {
        return array.EnumerateArray().Select(item => item.GetString()).ToArray();
    }

    /// <summary>
    /// 按指定键把 JSON 对象数组索引成字典。
    /// </summary>
    /// <param name="array">JSON 对象数组。</param>
    /// <param name="keyProperty">键属性名。</param>
    /// <returns>键 → 条目字典。</returns>
    private static Dictionary<string, JsonElement> IndexBy(JsonElement array, string keyProperty)
    {
        return array.EnumerateArray().ToDictionary(item => item.GetProperty(keyProperty).GetString());
    }

    /// <summary>
    /// 取报告中指定能力的条目。
    /// </summary>
    /// <param name="report">能力报告。</param>
    /// <param name="capabilityId">能力 Id。</param>
    /// <returns>报告条目。</returns>
    private static CapabilityReportEntry FindEntry(CapabilityReport report, string capabilityId)
    {
        return report.Entries.Single(entry => entry.Manifest.Id == capabilityId);
    }

    /// <summary>
    /// 经真实 HTTP 向管理面 POST 一个 action（断言 HTTP 200，返回响应体原文）。
    /// </summary>
    /// <param name="action">action 名。</param>
    /// <param name="body">请求体。</param>
    /// <returns>响应体原文。</returns>
    private static async Task<string> PostAsync(string action, string body)
    {
        using (var content = new StringContent(body, Encoding.UTF8, "application/json"))
        {
            var response = await SharedHttpClient.PostAsync("http://127.0.0.1:" + AdminTestPort + "/online/admin/" + action, content);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return await response.Content.ReadAsStringAsync();
        }
    }

    /// <summary>
    /// 业务面单开：产出业务宿主、无管理面；七能力全部启用，装配序 = 拓扑序（依赖恒先于依赖方），宿主模块表同序。
    /// </summary>
    [Fact]
    public async Task Compose_RuntimeOnly_ComposesHostWithoutAdminInTopologicalOrder()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.NotNull(runtime.Host);
            Assert.Null(runtime.AdminApi);
            Assert.Equal(ExpectedTopologicalOrder, runtime.Report.EnabledIdsInOrder);
            Assert.Equal(7, runtime.Report.Entries.Count(entry => entry.State == CapabilityFilterState.Enabled));
            Assert.Equal(ExpectedTopologicalOrder, runtime.Host.Modules.Select(module => module.Manifest.Id).ToArray());

            var order = runtime.Report.EnabledIdsInOrder.ToList();
            foreach (var entry in runtime.Report.Entries)
            {
                foreach (var dependency in entry.Dependencies)
                {
                    if (dependency.State == CapabilityDependencyAssemblyState.Assembled)
                    {
                        Assert.True(order.IndexOf(dependency.CapabilityId) < order.IndexOf(entry.Manifest.Id),
                            dependency.CapabilityId + " must be ordered before " + entry.Manifest.Id);
                    }
                }
            }

            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 管理面单开（EnableRuntime=false + UseAdminApi）：业务宿主不交付（Host 为 null），管理面产出；
    /// 新设计下每个模块的 Register 仍执行，报告完整（七能力全部启用、拓扑序不变）。
    /// </summary>
    [Fact]
    public async Task Compose_AdminOnly_HostNullAdminPresentReportComplete()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.EnableRuntime = false;
        options.UseAdminApi(adminOptions =>
        {
            adminOptions.AdminPort = AdminTestPort;
            adminOptions.AdminApiPrefix = "online/admin";
        });

        await WithComposedAsync(options, runtime =>
        {
            Assert.Null(runtime.Host);
            Assert.IsType<AdminApiServer>(runtime.AdminApi);
            Assert.Equal(ExpectedTopologicalOrder, runtime.Report.EnabledIdsInOrder);
            Assert.All(runtime.Report.Entries, entry => Assert.Equal(CapabilityFilterState.Enabled, entry.State));
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 双面齐开：一次组合同时产出业务宿主与管理面（管理面复用同一次组合的模块与报告）。
    /// </summary>
    [Fact]
    public async Task Compose_BothPlanes_ComposesHostAndAdmin()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.NotNull(runtime.Host);
            Assert.IsType<AdminApiServer>(runtime.AdminApi);
            Assert.Same(runtime.Host.Report, runtime.Report);
            return Task.CompletedTask;
        }, null, options => options.UseAdminApi(adminOptions =>
        {
            adminOptions.AdminPort = AdminTestPort;
            adminOptions.AdminApiPrefix = "online/admin";
        }));
    }

    /// <summary>
    /// 空列表 = 全装配红线：白名单与排除表均为空数组时不过滤，七能力全部启用。
    /// </summary>
    [Fact]
    public async Task Compose_EmptyFilterLists_AssembleAll()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.Equal(7, runtime.Report.Entries.Count(entry => entry.State == CapabilityFilterState.Enabled));
            Assert.Equal(ExpectedTopologicalOrder, runtime.Report.EnabledIdsInOrder);
            return Task.CompletedTask;
        }, null, options => options.Filter = new CapabilityAssemblyFilter(Array.Empty<string>(), Array.Empty<string>()));
    }

    /// <summary>
    /// 空白条目规整：两张表都只含空白条目时视为未过滤（机制层过滤器丢弃空白条目，不误报为未发现能力）。
    /// </summary>
    [Fact]
    public async Task Compose_BlankEntries_TreatedAsUnfiltered()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.Equal(7, runtime.Report.Entries.Count(entry => entry.State == CapabilityFilterState.Enabled));
            return Task.CompletedTask;
        }, null, options => options.Filter = new CapabilityAssemblyFilter(new[] { string.Empty, "  " }, new[] { string.Empty, "  " }));
    }

    /// <summary>
    /// 首尾空白条目规整：白名单条目 Trim 后精确命中（只装配该闭包，其余为清单段未收录）。
    /// </summary>
    [Fact]
    public async Task Compose_PaddedEnableListEntries_TrimmedToExactWhitelist()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.Equal(new[] { "online-foundation", "online-social" }, runtime.Report.EnabledIdsInOrder);
            Assert.Equal(CapabilityFilterState.NotInEnableList, FindEntry(runtime.Report, "online-assets").State);
            Assert.Equal(CapabilityFilterState.NotInEnableList, FindEntry(runtime.Report, "online-operations").State);
            return Task.CompletedTask;
        }, null, options => options.Filter = new CapabilityAssemblyFilter(new[] { " online-foundation ", "online-social" }));
    }

    /// <summary>
    /// 排除表生效：叶子能力 online-operations（无下游必选依赖）为 ExcludedByDisableList，其余六能力保持装配。
    /// </summary>
    [Fact]
    public async Task Compose_DisableList_ExcludesCapabilityFromReport()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.Equal(CapabilityFilterState.ExcludedByDisableList, FindEntry(runtime.Report, "online-operations").State);
            Assert.Equal(6, runtime.Report.EnabledIdsInOrder.Count);
            Assert.DoesNotContain("online-operations", runtime.Report.EnabledIdsInOrder);
            return Task.CompletedTask;
        }, null, options => options.Filter = new CapabilityAssemblyFilter(null, new[] { "online-operations" }));
    }

    /// <summary>
    /// 被排除能力的 admin action 按未安装（4003）裁决：载荷指向该能力，已装能力清单不含它。
    /// </summary>
    [Fact]
    public async Task Dispatch_DisabledCapabilityAction_AnswersNotInstalled()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);
            var envelope = await dispatcher.DispatchAsync("query_online_overview", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"RequestId\":\"r1\"}");
            using (var inner = ParseInner(envelope))
            {
            Assert.Equal((int)OnlineErrorCode.CapabilityNotInstalled, inner.RootElement.GetProperty("Code").GetInt32());
            var payload = inner.RootElement.GetProperty("Data");
            Assert.Equal("online-operations", payload.GetProperty("NotInstalledCapability").GetString());
            var installed = ReadStrings(payload.GetProperty("InstalledCapabilities"));
            Assert.Equal(6, installed.Length);
            Assert.DoesNotContain("online-operations", installed);
            }
        }, null, options => options.Filter = new CapabilityAssemblyFilter(null, new[] { "online-operations" }));
    }

    /// <summary>
    /// admin-only 形态经真实 HTTP：list_capabilities 如实反映排除表（online-assets 排除、其余因 Register 照常执行而为 Enabled——
    /// 旧设计下为 NotAssembled），被排除能力的 action 答 4003。
    /// </summary>
    [Fact]
    public async Task AdminOnly_OverHttp_ListCapabilitiesReflectsFilterAndDisabledActionAnswersNotInstalled()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.EnableRuntime = false;
        options.Filter = new CapabilityAssemblyFilter(null, new[] { "online-assets" });
        options.UseAdminApi(adminOptions =>
        {
            adminOptions.AdminPort = AdminTestPort;
            adminOptions.AdminApiPrefix = "online/admin";
        });

        await WithComposedAsync(options, async runtime =>
        {
            Assert.Null(runtime.Host);
            await runtime.StartAsync();

            using (var inner = ParseInner(await PostAsync("list_capabilities", ScopeBody)))
            {
                Assert.Equal(0, inner.RootElement.GetProperty("Code").GetInt32());
                var data = inner.RootElement.GetProperty("Data");
                var entries = IndexBy(data.GetProperty("Capabilities"), "Id");
                Assert.Equal(7, entries.Count);
                Assert.Equal("ExcludedByDisableList", entries["online-assets"].GetProperty("State").GetString());
                Assert.Equal("Enabled", entries["online-foundation"].GetProperty("State").GetString());
                Assert.Equal(6, data.GetProperty("EnabledIdsInOrder").GetArrayLength());
            }

            using (var inner = ParseInner(await PostAsync("grant_asset", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"RequestId\":\"r2\"}")))
            {
                Assert.Equal((int)OnlineErrorCode.CapabilityNotInstalled, inner.RootElement.GetProperty("Code").GetInt32());
                Assert.Equal("online-assets", inner.RootElement.GetProperty("Data").GetProperty("NotInstalledCapability").GetString());
            }
        });
    }

    /// <summary>
    /// 白名单闭包缺口：白名单含 online-matchmaking 而未含其必选依赖 online-social——依赖检查一次报全并 fail-fast，装配日志记 Error。
    /// </summary>
    [Fact]
    public void Compose_EnableListClosureGap_ThrowsAssemblyException()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.Filter = new CapabilityAssemblyFilter(new[] { "online-matchmaking" });

        var exception = Assert.Throws<CapabilityAssemblyException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));

        Assert.Contains(exception.Problems, problem => problem.Contains("online-matchmaking") && problem.Contains("online-social"));
        Assert.NotEmpty(((CollectingCapabilityAssemblyLog)options.AssemblyLog).Errors);
    }

    /// <summary>
    /// 白名单闭包完整：含全部必选依赖链时按白名单装配（拓扑序）。
    /// </summary>
    [Fact]
    public async Task Compose_EnableListWithClosure_AssemblesExactlyThatList()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.Equal(new[] { "online-foundation", "online-social", "online-matchmaking" }, runtime.Report.EnabledIdsInOrder);
            return Task.CompletedTask;
        }, null, options => options.Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social", "online-matchmaking" }));
    }

    /// <summary>
    /// 未知能力 Id（非空白、未被部署扫描发现）：白名单与排除表引用都在组合期 fail-fast，消息带该 Id。
    /// </summary>
    [Fact]
    public void Compose_UnknownCapabilityId_FailsFast()
    {
        var enableOptions = OnlineCapabilityTestHarness.CreateOptions();
        enableOptions.Filter = new CapabilityAssemblyFilter(new[] { "online-ghost" });
        var enableException = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), enableOptions));
        Assert.Contains("online-ghost", enableException.Message);

        var disableOptions = OnlineCapabilityTestHarness.CreateOptions();
        disableOptions.Filter = new CapabilityAssemblyFilter(null, new[] { "online-ghost" });
        var disableException = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), disableOptions));
        Assert.Contains("online-ghost", disableException.Message);
    }

    /// <summary>
    /// 双面均未启用：组合期 fail-fast，消息为 Capability.Hosting.Composition.NoPlaneEnabled 本地化文案。
    /// </summary>
    [Fact]
    public void Compose_NoPlaneEnabled_FailsFastWithKey()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.EnableRuntime = false;

        var exception = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));

        Assert.Equal(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.Composition.NoPlaneEnabled), exception.Message);
    }

    /// <summary>
    /// 组合选项缺失：参数校验直接拒绝。
    /// </summary>
    [Fact]
    public void Compose_NullOptions_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), null));
    }

    /// <summary>
    /// 族配置段作用域非法（缺 TenantId / TenantId=0）：族基建读取三元组即 fail-fast，消息带配置路径。
    /// </summary>
    /// <param name="familySectionJson">族配置段原文。</param>
    [Theory]
    [InlineData("{\"AppId\":1,\"ServerId\":1001}")]
    [InlineData("{\"TenantId\":0,\"AppId\":1,\"ServerId\":1001}")]
    public void Compose_FamilySectionTenantInvalid_FailsFast(string familySectionJson)
    {
        var options = CreateBareOptions(JsonDocument.Parse(familySectionJson).RootElement.Clone());
        options.Override<IOnlinePlayerDirectory>(new OnlineCapabilityTestHarness.EmptyPlayerDirectory());
        options.Override<IOnlineHotfixRollbackExecutor>(new OnlineCapabilityTestHarness.NoopHotfixRollbackExecutor());

        var exception = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));

        Assert.Contains("Online.TenantId", exception.Message);
    }

    /// <summary>
    /// 宿主选项作用域非法（TenantId=0）：机制校验宿主选项 fail-fast（Capability.Hosting.OptionsInvalid）。
    /// </summary>
    [Fact]
    public void Compose_HostOptionsInvalid_FailsFast()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.HostOptions.TenantId = 0;

        var exception = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));

        Assert.Equal(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.OptionsInvalid), exception.Message);
    }

    /// <summary>
    /// 管理面监听选项非法（端口 0）：管理面组装 fail-fast；失败组合已就地释放钩子声明，随后同进程再次组合成功。
    /// </summary>
    [Fact]
    public async Task Compose_InvalidAdminOptions_FailsFastAndReleasesHook()
    {
        var options = OnlineCapabilityTestHarness.CreateOptions();
        options.UseAdminApi(adminOptions =>
        {
            adminOptions.AdminPort = 0;
            adminOptions.AdminApiPrefix = "online/admin";
        });

        var exception = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));
        Assert.Equal(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.AdminApiOptionsInvalid), exception.Message);

        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            Assert.Equal(ExpectedTopologicalOrder, runtime.Report.EnabledIdsInOrder);
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 宿主扩展点缺失：Social 的 RequireHostService&lt;IOnlinePlayerDirectory&gt; 无宿主 Override 即 fail-fast（HostServiceMissing）。
    /// </summary>
    [Fact]
    public void Compose_PlayerDirectoryMissing_FailsFast()
    {
        var options = CreateBareOptions(OnlineCapabilityTestHarness.FamilySection());
        options.Override<IOnlineHotfixRollbackExecutor>(new OnlineCapabilityTestHarness.NoopHotfixRollbackExecutor());

        var exception = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));

        Assert.Contains("online-social", exception.Message);
        Assert.Contains(typeof(IOnlinePlayerDirectory).FullName, exception.Message);
    }

    /// <summary>
    /// 宿主扩展点缺失：Operations 的 RequireHostService&lt;IOnlineHotfixRollbackExecutor&gt; 无宿主 Override 即 fail-fast（HostServiceMissing）。
    /// </summary>
    [Fact]
    public void Compose_HotfixRollbackExecutorMissing_FailsFast()
    {
        var options = CreateBareOptions(OnlineCapabilityTestHarness.FamilySection());
        options.Override<IOnlinePlayerDirectory>(new OnlineCapabilityTestHarness.EmptyPlayerDirectory());

        var exception = Assert.Throws<InvalidOperationException>(() => CapabilityComposition.Compose(new OnlineCapabilityFamily(), options));

        Assert.Contains("online-operations", exception.Message);
        Assert.Contains(typeof(IOnlineHotfixRollbackExecutor).FullName, exception.Message);
    }

    /// <summary>
    /// 宿主扩展点满足：热更回滚执行器解析为宿主 Override 实例；制品获取器解析为 Operations 包内默认实现
    ///（生产路径不再恒缺席 8002）。
    /// </summary>
    [Fact]
    public async Task Compose_HostOverrides_ExecutorResolvesToOverrideAndFetcherToPackageDefault()
    {
        var executor = new OnlineCapabilityTestHarness.NoopHotfixRollbackExecutor();
        var options = CreateBareOptions(OnlineCapabilityTestHarness.FamilySection());
        options.Override<IOnlinePlayerDirectory>(new OnlineCapabilityTestHarness.EmptyPlayerDirectory());
        options.Override<IOnlineHotfixRollbackExecutor>(executor);

        await WithComposedAsync(options, runtime =>
        {
            Assert.Same(executor, runtime.Host.Services.GetService<IOnlineHotfixRollbackExecutor>());
            Assert.IsType<OnlineHotfixArtifactFetcher>(runtime.Host.Services.GetService<IOnlineHotfixArtifactFetcher>());
            return Task.CompletedTask;
        });
    }

    /// <summary>
    /// 钩子声明释放回归（真实族）：Social 在 Register 期声明 IOnlineChatModerationHook——组合存活期间再次组合 fail-fast
    ///（HookDuplicateDeclaration）；StopAsync 后再次组合成功（启动过 / 从未启动两条停止路径都释放声明）。
    /// </summary>
    [Fact]
    public async Task Compose_StopThenComposeAgain_ReleasesHookDeclaration()
    {
        var first = OnlineCapabilityTestHarness.Compose();
        try
        {
            var duplicate = Assert.Throws<InvalidOperationException>(() => OnlineCapabilityTestHarness.Compose());
            Assert.Contains(typeof(IOnlineChatModerationHook).FullName, duplicate.Message);
            await first.Host.StartAsync();
        }
        finally
        {
            await first.StopAsync();
        }

        var neverStarted = OnlineCapabilityTestHarness.Compose();
        await neverStarted.StopAsync();

        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            Assert.Equal(ExpectedTopologicalOrder, runtime.Report.EnabledIdsInOrder);
            await runtime.Host.StartAsync();
        });
    }

    /// <summary>
    /// list_capabilities 元端点：启用序 = 报告拓扑序；条目携带必选 / 可选依赖状态与可选依赖 Id 集合（全装配时无缺失功能）。
    /// </summary>
    [Fact]
    public async Task Dispatch_ListCapabilities_ReportsOrderDependenciesAndOptionalIds()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);
            using (var inner = ParseInner(await dispatcher.DispatchAsync("list_capabilities", ScopeBody)))
            {
            Assert.Equal(0, inner.RootElement.GetProperty("Code").GetInt32());
            var data = inner.RootElement.GetProperty("Data");
            Assert.Equal(ExpectedTopologicalOrder, ReadStrings(data.GetProperty("EnabledIdsInOrder")));

            var entries = IndexBy(data.GetProperty("Capabilities"), "Id");
            Assert.Equal(7, entries.Count);

            var match = entries["online-match"];
            Assert.Equal(new[] { "online-assets", "online-matchmaking" }, ReadStrings(match.GetProperty("OptionalCapabilityIds")));
            var matchDependencies = IndexBy(match.GetProperty("Dependencies"), "CapabilityId");
            Assert.Equal("Optional", matchDependencies["online-assets"].GetProperty("Kind").GetString());
            Assert.Equal("Assembled", matchDependencies["online-assets"].GetProperty("State").GetString());
            Assert.Equal("None", matchDependencies["online-assets"].GetProperty("AbsenceReason").GetString());
            Assert.Equal(0, match.GetProperty("AbsentFeatures").GetArrayLength());

            var social = entries["online-social"];
            Assert.Contains("online-foundation", ReadStrings(social.GetProperty("Requires")));
            var socialDependencies = IndexBy(social.GetProperty("Dependencies"), "CapabilityId");
            Assert.Equal("Required", socialDependencies["online-foundation"].GetProperty("Kind").GetString());
            Assert.Equal("Assembled", socialDependencies["online-foundation"].GetProperty("State").GetString());
            }
        });
    }

    /// <summary>
    /// list_capability_services 元端点：宿主扩展点以覆盖身份登记（来源 = 族 Id），模块默认 store 以本能力来源登记且未被覆盖。
    /// </summary>
    [Fact]
    public async Task Dispatch_ListCapabilityServices_ReportsOwnersAndOverrideFlags()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);
            using (var inner = ParseInner(await dispatcher.DispatchAsync("list_capability_services", ScopeBody)))
            {
            Assert.Equal(0, inner.RootElement.GetProperty("Code").GetInt32());
            var services = inner.RootElement.GetProperty("Data").GetProperty("Services").EnumerateArray().ToList();

            var directory = services.Single(service => service.GetProperty("ServiceType").GetString() == typeof(IOnlinePlayerDirectory).FullName);
            Assert.True(directory.GetProperty("IsOverride").GetBoolean());
            Assert.Equal("online", directory.GetProperty("OwnerCapabilityId").GetString());

            var friendStore = services.Single(service => service.GetProperty("ServiceType").GetString() == typeof(IOnlineFriendStore).FullName);
            Assert.Equal("online-social", friendStore.GetProperty("OwnerCapabilityId").GetString());
            Assert.False(friendStore.GetProperty("IsOverride").GetBoolean());
            Assert.False(friendStore.GetProperty("IsOverridden").GetBoolean());
            }
        });
    }

    /// <summary>
    /// list_capability_services 元端点：宿主 Override 一个模块默认 store 时两条登记并列——默认实现标注 IsOverridden，覆盖实现标注 IsOverride，
    /// 宿主侧解析命中覆盖实例。
    /// </summary>
    [Fact]
    public async Task Dispatch_ListCapabilityServices_OverrideShadowsModuleDefault()
    {
        var overrideStore = new InMemoryOnlineFriendStore();
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            Assert.Same(overrideStore, runtime.Host.Services.GetService<IOnlineFriendStore>());
            var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);
            using (var inner = ParseInner(await dispatcher.DispatchAsync("list_capability_services", ScopeBody)))
            {
            var friendStores = inner.RootElement.GetProperty("Data").GetProperty("Services").EnumerateArray()
                .Where(service => service.GetProperty("ServiceType").GetString() == typeof(IOnlineFriendStore).FullName)
                .ToList();

            Assert.Equal(2, friendStores.Count);
            var moduleDefault = friendStores.Single(service => !service.GetProperty("IsOverride").GetBoolean());
            Assert.Equal("online-social", moduleDefault.GetProperty("OwnerCapabilityId").GetString());
            Assert.True(moduleDefault.GetProperty("IsOverridden").GetBoolean());
            var hostOverride = friendStores.Single(service => service.GetProperty("IsOverride").GetBoolean());
            Assert.Equal("online", hostOverride.GetProperty("OwnerCapabilityId").GetString());
            }
        }, null, options => options.Override<IOnlineFriendStore>(overrideStore));
    }

    /// <summary>
    /// 配置段覆盖 Options 字段：Friend.SearchLimit=5 生效且来源为 ConfigurationSection，未配置字段保持 Default；
    /// 报告与 list_capability_settings 元端点同源一致。
    /// </summary>
    [Fact]
    public async Task Settings_SectionOverride_ReportsConfigurationSectionSource()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            var social = FindEntry(runtime.Report, "online-social");
            var searchLimit = social.SettingsFields.Single(field => field.FieldPath == "Friend.SearchLimit");
            Assert.Equal(5, (int)searchLimit.EffectiveValue);
            Assert.Equal(CapabilitySettingsFieldSource.ConfigurationSection, searchLimit.Source);
            var friendLimit = social.SettingsFields.Single(field => field.FieldPath == "Friend.FriendLimit");
            Assert.Equal(DefaultFriendLimit, (int)friendLimit.EffectiveValue);
            Assert.Equal(CapabilitySettingsFieldSource.Default, friendLimit.Source);

            var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);
            using (var inner = ParseInner(await dispatcher.DispatchAsync("list_capability_settings", ScopeBody)))
            {
            Assert.Equal(0, inner.RootElement.GetProperty("Code").GetInt32());
            var socialEntry = IndexBy(inner.RootElement.GetProperty("Data").GetProperty("Capabilities"), "Id")["online-social"];
            var fields = IndexBy(socialEntry.GetProperty("SettingsFields"), "FieldPath");
            Assert.Equal(5, fields["Friend.SearchLimit"].GetProperty("Value").GetInt32());
            Assert.Equal("ConfigurationSection", fields["Friend.SearchLimit"].GetProperty("Source").GetString());
            Assert.Equal(DefaultFriendLimit, fields["Friend.FriendLimit"].GetProperty("Value").GetInt32());
            Assert.Equal("Default", fields["Friend.FriendLimit"].GetProperty("Source").GetString());
            }
        }, "{\"online-social\":{\"Friend\":{\"SearchLimit\":5}}}");
    }

    /// <summary>
    /// 代码 Configure 胜出配置段：配置段 SearchLimit=5、Configure 改为 7——生效值 7、来源 Code（合并优先级 默认 &lt; 配置段 &lt; 代码）。
    /// </summary>
    [Fact]
    public async Task Settings_ConfigureBeatsSection_ReportsCodeSource()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(runtime =>
        {
            var searchLimit = FindEntry(runtime.Report, "online-social").SettingsFields.Single(field => field.FieldPath == "Friend.SearchLimit");
            Assert.Equal(7, (int)searchLimit.EffectiveValue);
            Assert.Equal(CapabilitySettingsFieldSource.Code, searchLimit.Source);
            Assert.NotEqual(DefaultFriendSearchLimit, (int)searchLimit.EffectiveValue);
            return Task.CompletedTask;
        }, "{\"online-social\":{\"Friend\":{\"SearchLimit\":5}}}", options => options.Configure<OnlineSocialOptions>(social => social.Friend.SearchLimit = 7));
    }

    /// <summary>
    /// 可选依赖缺失（排除 online-assets 与 online-matchmaking）：online-match 照常装配并可激活，报告标注两条可选依赖
    /// NotAssembled / ExcludedByDisableList，缺失功能文案为对应本地化键文本。
    /// </summary>
    [Fact]
    public async Task Compose_OptionalDependenciesMissing_ComposesMatchWithAbsentFeatures()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            var match = FindEntry(runtime.Report, "online-match");
            Assert.Equal(CapabilityFilterState.Enabled, match.State);
            Assert.Contains("online-match", runtime.Report.EnabledIdsInOrder);
            Assert.Equal(5, runtime.Report.EnabledIdsInOrder.Count);

            foreach (var absentId in new[] { "online-assets", "online-matchmaking" })
            {
                var dependency = match.Dependencies.Single(item => item.CapabilityId == absentId);
                Assert.Equal(CapabilityDependencyKind.Optional, dependency.Kind);
                Assert.Equal(CapabilityDependencyAssemblyState.NotAssembled, dependency.State);
                Assert.Equal(CapabilityDependencyAbsenceReason.ExcludedByDisableList, dependency.AbsenceReason);
            }

            Assert.Equal(2, match.AbsentFeatures.Count);
            Assert.Contains(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Online.Match.OptionalAssetsAbsent), match.AbsentFeatures);
            Assert.Contains(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Online.Match.OptionalMatchmakingAbsent), match.AbsentFeatures);

            Assert.Contains(runtime.Host.Modules, module => module.Manifest.Id == "online-match");
            await runtime.Host.StartAsync();
        }, null, options => options.Filter = new CapabilityAssemblyFilter(null, new[] { "online-assets", "online-matchmaking" }));
    }

    /// <summary>
    /// 目录外未知 action：既非已注册 action、也不属于任何被禁用能力——答 4002（HTTP 恒 200，外层恒 0）。
    /// </summary>
    [Fact]
    public async Task Dispatch_UnknownActionOutsideCatalog_AnswersResourceNotFound()
    {
        await OnlineCapabilityTestHarness.WithRuntimeAsync(async runtime =>
        {
            var dispatcher = OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host);
            using (var inner = ParseInner(await dispatcher.DispatchAsync("definitely_not_an_action", ScopeBody)))
            {
            Assert.Equal((int)OnlineErrorCode.ResourceNotFound, inner.RootElement.GetProperty("Code").GetInt32());
            }
        });
    }
}
