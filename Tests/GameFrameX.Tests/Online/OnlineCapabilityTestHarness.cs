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
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 测试用装配日志收集器（C294 起 <see cref="ICapabilityAssemblyLog"/> 是组合必填出口；
/// 本类在进程内收集 Info / Error 两条流，供用例断言装配日志内容）。
/// </summary>
public sealed class CollectingCapabilityAssemblyLog : ICapabilityAssemblyLog
{
    /// <summary>Info 级日志流。</summary>
    public readonly List<string> Infos = new();

    /// <summary>Error 级日志流。</summary>
    public readonly List<string> Errors = new();

    /// <inheritdoc />
    public void Info(string message)
    {
        Infos.Add(message);
    }

    /// <inheritdoc />
    public void Error(string message)
    {
        Errors.Add(message);
    }
}

/// <summary>
/// Online 能力族测试组合挂具（C294：测试与宿主共用同一装配面
/// <see cref="CapabilityComposition.Compose"/>——族配置段 JSON、宿主 Override、过滤器与
/// 管理面适配器全部经 <see cref="CapabilityCompositionOptions"/> 注入；admin 调度器按
/// <c>AdminApiComposition.CreateAdminApiPlane</c> 同口径组装但直接暴露
/// <see cref="AdminApiDispatcher"/>，供行为用例内网调用）。
/// </summary>
public static class OnlineCapabilityTestHarness
{
    /// <summary>测试用租户标识（与族配置段一致）。</summary>
    public const long TenantId = 1;

    /// <summary>测试用应用标识（与族配置段一致）。</summary>
    public const long AppId = 1;

    /// <summary>测试用区服标识（与族配置段一致）。</summary>
    public const long ServerId = 1001;

    /// <summary>
    /// 构造族配置段 JSON（作用域三元组 + 可选 Settings 子对象；缺失键由族 fail-fast，因此恒写入）。
    /// </summary>
    /// <param name="settingsJson">Settings 子对象原文（null = 不写 Settings 键）。</param>
    /// <returns>族配置段节点。</returns>
    public static JsonElement FamilySection(string settingsJson = null)
    {
        var settings = string.IsNullOrEmpty(settingsJson) ? string.Empty : ",\"Settings\":" + settingsJson;
        return JsonDocument.Parse("{\"TenantId\":1,\"AppId\":1,\"ServerId\":1001" + settings + "}").RootElement.Clone();
    }

    /// <summary>
    /// 构造组合选项（收集器日志 + 合法宿主选项 + 两个宿主扩展点 Override）。
    /// <para>
    /// 管理面适配器（错误码表 / 作用域守卫 / 幂等包裹）由族基建 <see cref="OnlineCapabilityFamily"/> 注册，
    /// 测试不得 Override 绕行——<c>UseAdminApi</c> 形态必须走真实族基建解析路径。
    /// </para>
    /// </summary>
    /// <param name="settingsJson">Settings 子对象原文（null = 不写 Settings 键）。</param>
    /// <returns>组合选项。</returns>
    public static CapabilityCompositionOptions CreateOptions(string settingsJson = null)
    {
        var options = new CapabilityCompositionOptions(FamilySection(settingsJson), new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId, },
        };

        // 宿主扩展点：Social 的玩家目录与 Operations 的热更回滚执行器（两者均 RequireHostService，缺失即装配 fail-fast）。
        options.Override<IOnlinePlayerDirectory>(new EmptyPlayerDirectory());
        options.Override<IOnlineHotfixRollbackExecutor>(new NoopHotfixRollbackExecutor());
        return options;
    }

    /// <summary>
    /// 组合 Online 能力族（全装配；过滤器 / Settings / Configure 由用例经 <paramref name="configure"/> 追加）。
    /// </summary>
    /// <param name="settingsJson">Settings 子对象原文（null = 不写 Settings 键）。</param>
    /// <param name="configure">用例追加的编排动作。</param>
    /// <returns>组合产物。</returns>
    public static CapabilityRuntime Compose(string settingsJson = null, Action<CapabilityCompositionOptions> configure = null)
    {
        var options = CreateOptions(settingsJson);
        configure?.Invoke(options);
        return CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
    }

    /// <summary>
    /// 按机制同口径组装 admin 调度器（遍历模块贡献 action；错误码表 / 作用域守卫 / 幂等包裹一律从
    /// 族基建注册的宿主共享服务面解析——与 <c>AdminApiComposition.CreateAdminApiPlane</c> 同源，测试不另造适配器）。
    /// </summary>
    /// <param name="host">已装配能力宿主。</param>
    /// <returns>admin 调度器。</returns>
    public static AdminApiDispatcher CreateDispatcher(CapabilityHost host)
    {
        var registry = new AdminActionRegistry();
        foreach (var module in host.Modules)
        {
            if (module is IAdminActionContributor contributor)
            {
                contributor.RegisterAdminActions(registry);
            }
        }

        return new AdminApiDispatcher(
            registry,
            host.Services.GetService<AdminApiErrorCodes>(),
            host.Services.GetService<IAdminApiScopeAdapter>(),
            new AdminApiClaimedScope(host.TenantId, host.AppId, host.ServerId),
            host.Services.GetOptionalService<IAdminApiIdempotencyAdapter>(),
            host.Report,
            host.Services);
    }

    /// <summary>
    /// 组合并启动业务面（Activate 桥发布完成后返回；用例须在结束时 <c>StopAsync</c> 以撤桥）。
    /// </summary>
    /// <param name="settingsJson">Settings 子对象原文。</param>
    /// <param name="configure">用例追加的编排动作。</param>
    /// <returns>已启动的组合产物。</returns>
    public static async Task<CapabilityRuntime> ComposeStartedAsync(string settingsJson = null, Action<CapabilityCompositionOptions> configure = null)
    {
        var runtime = Compose(settingsJson, configure);
        await runtime.Host.StartAsync();
        return runtime;
    }

    /// <summary>
    /// 组合并在 <paramref name="body"/> 结束后恒停止（finally StopAsync：撤桥 + 释放钩子声明——
    /// 同进程下一次组合才能重新声明 <c>IOnlineChatModerationHook</c>）。
    /// </summary>
    /// <param name="body">用例主体（入参为已组合、未启动的产物）。</param>
    /// <param name="settingsJson">Settings 子对象原文。</param>
    /// <param name="configure">用例追加的编排动作。</param>
    /// <returns>异步任务。</returns>
    public static async Task WithRuntimeAsync(Func<CapabilityRuntime, Task> body, string settingsJson = null, Action<CapabilityCompositionOptions> configure = null)
    {
        var runtime = Compose(settingsJson, configure);
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
    /// 空玩家目录桩（Social 的 <c>RequireHostService&lt;IOnlinePlayerDirectory&gt;</c> 最小满足；
    /// 搜索 / 解析均返回空集合 = 部署未接目录的披露态，不是伪数据）。
    /// </summary>
    public sealed class EmptyPlayerDirectory : IOnlinePlayerDirectory
    {
        /// <inheritdoc />
        public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> SearchByNameAsync(long tenantId, long appId, string keyword, int limit, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(Array.Empty<OnlinePlayerDirectoryEntry>());
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<OnlinePlayerDirectoryEntry>> FindAsync(long tenantId, long appId, IReadOnlyList<long> playerIds, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<OnlinePlayerDirectoryEntry>>(Array.Empty<OnlinePlayerDirectoryEntry>());
        }
    }

    /// <summary>
    /// 空操作热更回滚执行器桩（Operations 的 <c>RequireHostService&lt;IOnlineHotfixRollbackExecutor&gt;</c> 最小满足）。
    /// </summary>
    public sealed class NoopHotfixRollbackExecutor : IOnlineHotfixRollbackExecutor
    {
        /// <inheritdoc />
        public Task RollbackAsync(string targetVersion, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}

/// <summary>
/// 好友域桥验证种子（C294 §4.6：生产配置键 <c>FriendBridgeSeedPlayerIds</c> 删除后，种子语义挪进测试夹具；
/// 语句级迁移自旧 <c>OnlineFriendDomainBridgeWiring.SeedFriendDomainBridgeVerificationAsync</c>——
/// 首元素为请求方、其余为好友，经真实 <see cref="OnlineFriendService"/> 写路径两两建立，幂等收敛）。
/// </summary>
public static class FriendBridgeVerificationSeed
{
    /// <summary>
    /// 经真实写路径两两建立种子好友关系（空 / 单元素列表为空操作；重复种子幂等命中既有关系）。
    /// </summary>
    /// <param name="host">已装配能力宿主。</param>
    /// <param name="friendPlayerIds">种子标识列表（首元素 = 请求方，其余 = 好友）。</param>
    /// <returns>成功建立的关系数。</returns>
    public static async Task<int> SeedAsync(CapabilityHost host, IReadOnlyList<long> friendPlayerIds)
    {
        Assert.NotNull(host);
        if (friendPlayerIds == null || friendPlayerIds.Count < 2)
        {
            return 0;
        }

        var friends = host.Services.GetOptionalService<OnlineFriendService>();
        if (friends == null)
        {
            return 0;
        }

        var requesterId = friendPlayerIds[0];
        var established = 0;
        for (var index = 1; index < friendPlayerIds.Count; index++)
        {
            var friendPlayerId = friendPlayerIds[index];
            if (friendPlayerId <= 0 || friendPlayerId == requesterId)
            {
                continue;
            }

            var requested = await friends.RequestAsync(new OnlineScope(host.TenantId, host.AppId, host.ServerId, requesterId), friendPlayerId);
            if (!requested.IsSuccess)
            {
                continue;
            }

            var accepted = await friends.AcceptAsync(new OnlineScope(host.TenantId, host.AppId, host.ServerId, friendPlayerId), requested.Data.FriendshipId);
            if (!accepted.IsSuccess && accepted.Code != OnlineErrorCode.StateOperationForbidden)
            {
                continue;
            }

            established++;
        }

        return established;
    }
}

/// <summary>
/// 能力运行时串行集合（C294：桥槽位与钩子声明槽位均为进程级静态——同契约重复发布 / 重复声明 fail-fast，
/// 钩子声明在 Compose 的 Register 期即落位、StopAsync 才释放；机制场景槽位亦为静态。
/// 因此本测试程序集内凡调用 <c>CapabilityComposition.Compose</c>、发布或消费桥契约的用例，
/// 一律落本集合且禁并行——禁并行集合在 xUnit 中独占执行，不与其它任何集合交叠）。
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class CapabilityRuntimeCollection
{
    /// <summary>集合名。</summary>
    public const string Name = "CapabilityRuntimeCollection";
}
