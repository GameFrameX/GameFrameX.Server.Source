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
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Foundation.Localization.Core;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 管理面组合机制测试（C294：<c>UseAdminApi</c> + <see cref="CapabilityComposition.Compose"/> 组装期——
/// action 名跨能力唯一、manifest 声明与实注册双向对账一次报全、admin-only 形态、监听选项非法与无面启用 fail-fast；
/// 适配器由 <see cref="MechanismTestFamily"/> 族基建预置；组装期用例不启动监听）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public class CapabilityAdminCompositionTests
{
    /// <summary>管理面路由前缀（机制测试专用）。</summary>
    private const string AdminApiPrefix = "mech/admin";

    /// <summary>
    /// 探测 28290-28299 段内的空闲端口（本类用例不启动监听，端口只需合法且不与本机已占用端口冲突）。
    /// </summary>
    /// <returns>空闲端口；整段被占用时返回段首（不监听时仍为合法端口）。</returns>
    private static int FindFreeAdminPort()
    {
        for (var port = 28290; port <= 28299; port++)
        {
            TcpListener listener = null;
            try
            {
                listener = new TcpListener(IPAddress.Loopback, port);
                listener.Start();
                return port;
            }
            catch (SocketException)
            {
                // 端口被占用：试下一个。
            }
            finally
            {
                listener?.Stop();
            }
        }

        return 28290;
    }

    /// <summary>
    /// 构造启用管理面的组合选项（合法作用域 + 白名单 + UseAdminApi 合法监听参数）。
    /// </summary>
    /// <param name="enabledIds">参与组合的槽位能力 Id。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateAdminOptions(params string[] enabledIds)
    {
        var port = FindFreeAdminPort();
        var options = new CapabilityCompositionOptions(default, new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 },
            Filter = new CapabilityAssemblyFilter(enabledIds),
        };
        options.UseAdminApi(adminApiOptions =>
        {
            adminApiOptions.AdminPort = port;
            adminApiOptions.AdminApiPrefix = AdminApiPrefix;
        });
        return options;
    }

    /// <summary>
    /// 组合机制测试族。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <returns>组合产物。</returns>
    private static CapabilityRuntime Compose(CapabilityCompositionOptions options)
    {
        return CapabilityComposition.Compose(new MechanismTestFamily(), options);
    }

    /// <summary>
    /// 测试用 admin action 处理器（恒返回固定载荷；组装期用例不会真正调用）。
    /// </summary>
    /// <param name="request">请求包装。</param>
    /// <param name="scope">已通过守卫的作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>固定载荷。</returns>
    private static Task<object> OkHandler(AdminApiRequest request, AdminApiClaimedScope scope, CancellationToken cancellationToken)
    {
        return Task.FromResult<object>("ok");
    }

    /// <summary>
    /// 停止组合产物（组合成功才有产物；管理面未启动时停止为空操作）。
    /// </summary>
    /// <param name="runtime">组合产物（可为 null）。</param>
    /// <returns>异步任务。</returns>
    private static async Task StopIfComposedAsync(CapabilityRuntime runtime)
    {
        if (runtime != null)
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// action 名跨能力唯一：两个能力注册同名 action 即组装失败（不再后者静默覆盖前者），
    /// 错误信息带 action 名与先注册者的能力 Id。
    /// </summary>
    [Fact]
    public async Task Compose_DuplicateActionAcrossCapabilities_FailsFastWithFirstOwner()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Actions = new[] { "mech_duplicate" };
            MechanismScenario.First.RegisterAdminActions = registry => registry.Register("mech_duplicate", OkHandler, false);
            MechanismScenario.Second.Actions = new[] { "mech_duplicate" };
            MechanismScenario.Second.RegisterAdminActions = registry => registry.Register("mech_duplicate", OkHandler, false);

            var exception = Assert.Throws<InvalidOperationException>(() => runtime = Compose(CreateAdminOptions("mech-first", "mech-second")));

            Assert.Contains("mech_duplicate", exception.Message);
            Assert.Contains("mech-first", exception.Message);
            Assert.DoesNotContain("mech-second", exception.Message);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 双向对账一次报全：manifest 声明但未注册的 action（附声明能力 Id）与注册但未被任何启用能力声明的 action
    /// 同时出现在同一个异常里（问题数 = 2）。
    /// </summary>
    [Fact]
    public async Task Compose_ActionReconciliation_ReportsBothDirectionsInOneThrow()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Actions = new[] { "mech_declared_only" };
            MechanismScenario.Second.RegisterAdminActions = registry => registry.Register("mech_registered_only", OkHandler, false);

            var exception = Assert.Throws<InvalidOperationException>(() => runtime = Compose(CreateAdminOptions("mech-first", "mech-second")));

            var expectedHeader = string.Format(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.ReconcileFailed), 2);
            Assert.StartsWith(expectedHeader, exception.Message);
            Assert.Contains(string.Format(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.ActionNotRegistered), "mech_declared_only", "mech-first"), exception.Message);
            Assert.Contains(string.Format(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.ActionNotDeclared), "mech_registered_only", "mech-second"), exception.Message);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 按能力分组对账（跨能力冒名）：A 声明却未注册、B 注册却未声明同名 action，全局集合会互相抵消，
    /// 分组对账必须装配失败并指出声明者与实际注册者。
    /// </summary>
    [Fact]
    public async Task Compose_ActionDeclaredByOneCapabilityRegisteredByAnother_FailsFast()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Actions = new[] { "mech_impersonated" };
            MechanismScenario.Second.RegisterAdminActions = registry => registry.Register("mech_impersonated", OkHandler, false);

            var exception = Assert.Throws<InvalidOperationException>(() => runtime = Compose(CreateAdminOptions("mech-first", "mech-second")));

            var expectedHeader = string.Format(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.ReconcileFailed), 1);
            Assert.StartsWith(expectedHeader, exception.Message);
            Assert.Contains(string.Format(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.ActionOwnerMismatch), "mech_impersonated", "mech-first", "mech-second"), exception.Message);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 合法的声明 + 注册配对：组合成功，业务宿主与管理面同时在场（未启动监听）。
    /// </summary>
    [Fact]
    public async Task Compose_ValidDeclaredAndRegisteredAction_ComposesAdminPlane()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Actions = new[] { "mech_ping" };
            MechanismScenario.First.RegisterAdminActions = registry => registry.Register("mech_ping", OkHandler, false);

            runtime = Compose(CreateAdminOptions("mech-first"));

            Assert.NotNull(runtime.AdminApi);
            Assert.NotNull(runtime.Host);
            Assert.Equal(new[] { "mech-first" }, runtime.Report.EnabledIdsInOrder);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// admin-only 形态（EnableRuntime=false）：业务宿主不交付（Host 为 null），管理面在场且报告照常产出。
    /// </summary>
    [Fact]
    public async Task Compose_AdminOnly_HostNullAndAdminPlanePresent()
    {
        CapabilityRuntime runtime = null;
        try
        {
            MechanismScenario.First.Actions = new[] { "mech_ping" };
            MechanismScenario.First.RegisterAdminActions = registry => registry.Register("mech_ping", OkHandler, false);
            var options = CreateAdminOptions("mech-first");
            options.EnableRuntime = false;

            runtime = Compose(options);

            Assert.Null(runtime.Host);
            Assert.NotNull(runtime.AdminApi);
            Assert.NotNull(runtime.Report);
            Assert.Equal(new[] { "mech-first" }, runtime.Report.EnabledIdsInOrder);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 管理面监听选项非法（端口 0）：组装期 fail-fast（本地化「管理面监听选项非法」）。
    /// </summary>
    [Fact]
    public async Task Compose_InvalidAdminApiOptions_FailsFast()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var options = new CapabilityCompositionOptions(default, new CollectingCapabilityAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 },
                Filter = new CapabilityAssemblyFilter(new[] { "mech-first" }),
            };
            options.UseAdminApi(adminApiOptions =>
            {
                adminApiOptions.AdminPort = 0;
                adminApiOptions.AdminApiPrefix = AdminApiPrefix;
            });

            var exception = Assert.Throws<InvalidOperationException>(() => runtime = Compose(options));

            Assert.Equal(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.AdminApi.AdminApiOptionsInvalid), exception.Message);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }

    /// <summary>
    /// 业务面与管理面都未启用（EnableRuntime=false 且未 UseAdminApi）：组合入口即 fail-fast（NoPlaneEnabled）。
    /// </summary>
    [Fact]
    public async Task Compose_NoPlaneEnabled_FailsFast()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var options = new CapabilityCompositionOptions(default, new CollectingCapabilityAssemblyLog())
            {
                HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 },
                Filter = new CapabilityAssemblyFilter(new[] { "mech-first" }),
                EnableRuntime = false,
            };

            var exception = Assert.Throws<InvalidOperationException>(() => runtime = Compose(options));

            Assert.Equal(LocalizationService.GetString(GameFrameX.Localization.Keys.Capability.Hosting.Composition.NoPlaneEnabled), exception.Message);
        }
        finally
        {
            await StopIfComposedAsync(runtime);
            MechanismScenario.Reset();
        }
    }
}
