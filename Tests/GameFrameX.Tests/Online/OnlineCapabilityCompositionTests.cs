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
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Online 能力单入口组合测试（C229）：双闸组合 × 过滤器形态、空列表全装配红线、
/// admin-only 目录如实反映配置排除、组合期 fail-fast（未知 Id / 闭包缺口 / 无面启用 / 选项非法）。
/// </summary>
public class OnlineCapabilityCompositionTests
{
    /// <summary>
    /// 以最小合法作用域构造编排回调（双闸由用例覆写）。
    /// </summary>
    /// <param name="configure">用例追加的编排动作。</param>
    /// <returns>组合回调。</returns>
    private static Action<OnlineCapabilityCompositionOptions> Compose(Action<OnlineCapabilityCompositionOptions> configure = null)
    {
        return options =>
        {
            options.TenantId = 1;
            options.AppId = 1;
            options.ServerId = 1001;
            configure?.Invoke(options);
        };
    }

    /// <summary>
    /// 业务闸单开：组合产物只含运行时，报告全装配（7 能力拓扑序）。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_RuntimeGate_ComposesRuntimeWithoutAdmin()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options => options.EnableRuntime = true));

        Assert.NotNull(composition.Runtime);
        Assert.Null(composition.AdminApi);
        Assert.Equal(new[]
        {
            "online-assets", "online-foundation", "online-competition", "online-match",
            "online-social", "online-matchmaking", "online-operations",
        }, composition.Runtime.Report.EnabledIdsInOrder);
    }

    /// <summary>
    /// 管理面闸单开：组合产物只含 admin-only HTTP 服务（业务运行时不构造）。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_AdminGateOnly_ComposesAdminWithoutRuntime()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options => options.EnableAdminApi = true));

        Assert.Null(composition.Runtime);
        Assert.NotNull(composition.AdminApi);
    }

    /// <summary>
    /// 双闸齐开：一次回调同时产出运行时与管理面（admin 复用同一入口产物）。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_BothGates_ComposesBothFaces()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.EnableAdminApi = true;
        }));

        Assert.NotNull(composition.Runtime);
        Assert.NotNull(composition.AdminApi);
    }

    /// <summary>
    /// 空列表 = 全装配红线：Capabilities / Disable 为空数组时不过滤。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_EmptyFilterLists_AssembleAll()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Capabilities = Array.Empty<string>();
            options.Disable = Array.Empty<string>();
        }));

        Assert.Equal(7, composition.Runtime.Report.Entries.Count(entry => entry.State == CapabilityFilterState.Enabled));
    }

    /// <summary>
    /// 空白条目规整：全空白列表视为未过滤（等价 null）。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_BlankEntries_TreatedAsUnfiltered()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Capabilities = new[] { "  ", string.Empty };
            options.Disable = new[] { " " };
        }));

        Assert.Equal(7, composition.Runtime.Report.Entries.Count(entry => entry.State == CapabilityFilterState.Enabled));
    }
    /// <summary>
    /// 排除表生效：被排除能力（叶子能力 online-operations，无下游依赖）在运行时报告中为
    /// ExcludedByDisableList，其余保持装配。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_DisableList_ExcludesCapabilityFromReport()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Disable = new[] { "online-operations" };
        }));

        var entries = composition.Runtime.Report.Entries.ToDictionary(entry => entry.Manifest.Id);
        Assert.Equal(CapabilityFilterState.ExcludedByDisableList, entries["online-operations"].State);
        Assert.Equal(6, composition.Runtime.Report.EnabledIdsInOrder.Count);
        Assert.DoesNotContain("online-operations", composition.Runtime.Report.EnabledIdsInOrder);
    }

    /// <summary>
    /// 排除表在业务形态的对外行为：被排除能力的 admin action 按未安装（4003）裁决且载荷指向该能力。
    /// </summary>
    [Fact]
    public async Task AddOnlineCapabilities_DisabledCapabilityAction_AnswersNotInstalledAsync()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Disable = new[] { "online-operations" };
        }));

        var dispatcher = OnlineAdminApiComposition.CreateDispatcher(composition.Runtime);
        var response = await dispatcher.DispatchAsync("query_online_overview", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"RequestId\":\"r1\"}");
        using var outer = JsonDocument.Parse(response);
        var innerJson = outer.RootElement.GetProperty("data").GetString();
        using var inner = JsonDocument.Parse(innerJson);
        Assert.Equal((int)OnlineErrorCode.CapabilityNotInstalled, inner.RootElement.GetProperty("Code").GetInt32());
        using var payload = JsonDocument.Parse(inner.RootElement.GetProperty("Data").GetRawText());
        Assert.Equal("online-operations", payload.RootElement.GetProperty("NotInstalledCapability").GetString());
    }


    /// <summary>
    /// admin-only 形态接入同一配置段：排除/白名单外条目在目录报告中如实反映，其余仍为未装配；
    /// 被排除能力与未装配能力的 action 一律 4003。
    /// </summary>
    [Fact]
    public async Task AdminOnlyDispatcher_WithFilter_ReflectsExclusionInCatalogAsync()
    {
        var filter = new CapabilityAssemblyFilter(null, new[] { "online-assets" });
        var dispatcher = OnlineAdminApiComposition.CreateAdminOnlyDispatcher(new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1001 }, filter);

        var listing = await dispatcher.DispatchAsync("list_capabilities", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001}");
        using (var outer = JsonDocument.Parse(listing))
        {
            var innerJson = outer.RootElement.GetProperty("data").GetString();
            using var inner = JsonDocument.Parse(innerJson);
            var entries = inner.RootElement.GetProperty("Data").GetProperty("Capabilities").EnumerateArray()
                .ToDictionary(entry => entry.GetProperty("Id").GetString());
            Assert.Equal(7, entries.Count);
            Assert.Equal("ExcludedByDisableList", entries["online-assets"].GetProperty("State").GetString());
            Assert.Equal("NotAssembled", entries["online-foundation"].GetProperty("State").GetString());
        }

        var notInstalled = await dispatcher.DispatchAsync("grant_asset", "{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001,\"RequestId\":\"r2\"}");
        using var outerNotInstalled = JsonDocument.Parse(notInstalled);
        var innerNotInstalledJson = outerNotInstalled.RootElement.GetProperty("data").GetString();
        using var innerNotInstalled = JsonDocument.Parse(innerNotInstalledJson);
        Assert.Equal((int)OnlineErrorCode.CapabilityNotInstalled, innerNotInstalled.RootElement.GetProperty("Code").GetInt32());
    }

    /// <summary>
    /// 白名单闭包缺口：白名单含 online-matchmaking 而未含其依赖 online-social，组合期 fail-fast。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_EnableListClosureGap_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Capabilities = new[] { "online-matchmaking" };
        })));
    }

    /// <summary>
    /// 白名单闭包完整：含全部依赖链时按白名单装配。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_EnableListWithClosure_AssemblesWhitelist()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Capabilities = new[] { "online-foundation", "online-social", "online-matchmaking" };
        }));

        Assert.Equal(new[] { "online-foundation", "online-social", "online-matchmaking" },
            composition.Runtime.Report.EnabledIdsInOrder);
    }

    /// <summary>
    /// 未知能力 Id：过滤器引用未发现的能力，组合期 fail-fast。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_UnknownCapabilityId_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableRuntime = true;
            options.Capabilities = new[] { "online-ghost" };
        })));
    }

    /// <summary>
    /// 双闸均未启用：组合期 fail-fast，消息键为 Capability.Hosting.Composition.NoPlaneEnabled。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_NoPlaneEnabled_FailsFastWithKey()
    {
        var exception = Assert.Throws<InvalidOperationException>(
            () => OnlineCapabilityComposition.AddOnlineCapabilities(Compose()));

        Assert.Equal(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.Composition.NoPlaneEnabled), exception.Message);
    }

    /// <summary>
    /// 回调缺失：参数校验直接拒绝。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_NullConfigure_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => OnlineCapabilityComposition.AddOnlineCapabilities(null));
    }

    /// <summary>
    /// 作用域非法：透传宿主选项校验 fail-fast（TenantId=0）。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_InvalidScope_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => OnlineCapabilityComposition.AddOnlineCapabilities(options =>
        {
            options.TenantId = 0;
            options.AppId = 1;
            options.ServerId = 1001;
            options.EnableRuntime = true;
        }));
    }

    /// <summary>
    /// 管理面监听选项非法：透传管理面组合工厂校验 fail-fast（端口 0）。
    /// </summary>
    [Fact]
    public void AddOnlineCapabilities_InvalidAdminOptions_FailsFast()
    {
        Assert.Throws<InvalidOperationException>(() => OnlineCapabilityComposition.AddOnlineCapabilities(Compose(options =>
        {
            options.EnableAdminApi = true;
            options.AdminApi = new AdminApiOptions { AdminPort = 0, AdminApiPrefix = "online/admin" };
        })));
    }
}
