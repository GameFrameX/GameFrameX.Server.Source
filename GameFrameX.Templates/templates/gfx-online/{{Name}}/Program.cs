// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//
//  使用本项目须严格遵守相应法律法规与开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file at the root of the source code for the full license text.
//
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯其他合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations.
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation;
//#if (Assets)
using GameFrameX.Capability.Online.Assets;
//#endif
//#if (Social)
using GameFrameX.Capability.Online.Social;
//#endif
//#if (Matchmaking)
using GameFrameX.Capability.Online.Matchmaking;
//#endif
//#if (Match)
using GameFrameX.Capability.Online.Match;
//#endif
//#if (Competition)
using GameFrameX.Capability.Online.Competition;
//#endif
//#if (Operations)
using GameFrameX.Capability.Online.Operations;
//#endif
using GameFrameX.Foundation.Idempotency;

// {{Name}} —— Online 能力最小组合根宿主（gfx-online 模板生成，C230）。
// 引啥装配啥：只装配 csproj 实际引用的能力包（编译期保证）；未装配能力的 admin action 不可执行
// ——未部署（未引用）→ 4002 UnknownAction；已引用被 SetFilter/Disable 排除 → 4003 CapabilityNotInstalled。
// 增减能力 = csproj ProjectReference 与 AddOnlineXxxRuntime 成对增减。

// 授权作用域三元组（演示值）。接入配置管道时改走嵌套强类型段 "Online": { ... }（禁平铺键，
// 能力族全局约定 2026-10-12）。
const long tenantId = 1;
const long appId = 1;
const long serverId = 1;

// 组合根基建预置（与生产组合根同形的最小四件：作用域 / 事件总线 / 事件出口 / 幂等）。
var scope = new OnlineScope(tenantId, appId, serverId);
var eventBus = new InMemoryEventPublisher();
var idempotency = new OnlineIdempotencyService(new IdempotencyCoordinator(
    new InMemoryIdempotencyStore(), SystemClock.Instance,
    new IdempotencyOptions { FailedReplayPolicy = FailedReplayPolicy.ReExecute }));

var builder = new CapabilityHostBuilder();
builder.UseService(scope);
builder.UseService(eventBus);
builder.UseService<IOnlineEventPublisher>(new OnlineEventPublisher(eventBus));
builder.UseService(idempotency);

// 能力装配（回调式，AddXxx(options => ...) 惯例；闭包缺口的引用组合会在 Build 期 fail-fast）。
builder.AddOnlineFoundationRuntime(options => { /* 地基包级选项（缺省） */ });
//#if (Assets)
builder.AddOnlineAssetsRuntime(options => { /* 资产包级选项（缺省） */ });
//#endif
//#if (Social)
builder.AddOnlineSocialRuntime(options => { /* 社交包级选项（缺省） */ });
//#endif
//#if (Matchmaking)
builder.AddOnlineMatchmakingRuntime(options => { /* 撮合包级选项（缺省） */ });
//#endif
//#if (Match)
builder.AddOnlineMatchRuntime(options => { /* 对局包级选项（缺省） */ });
//#endif
//#if (Competition)
builder.AddOnlineCompetitionRuntime(options => { /* 竞技包级选项（缺省） */ });
//#endif
//#if (Operations)
builder.AddOnlineOperationsRuntime(options => { /* 运营聚合包级选项（缺省） */ });
//#endif

var host = builder.Build(new CapabilityHostOptions
{
    TenantId = tenantId,
    AppId = appId,
    ServerId = serverId,
});

// 管理面：模块贡献 action → 注册表 → 调度器 → HTTP 服务（机制开放，组合根零域硬编码）。
var registry = new AdminActionRegistry();
foreach (var module in host.Modules)
{
    if (module is IAdminActionContributor contributor)
    {
        contributor.RegisterAdminActions(registry);
    }
}

var dispatcher = new AdminApiDispatcher(
    registry,
    OnlineAdminApiAdapters.ErrorCodes,
    OnlineAdminApiAdapters.CreateScopeAdapter(host.Services.GetService<OnlineScope>()),
    new AdminApiClaimedScope(host.TenantId, host.AppId, host.ServerId),
    OnlineAdminApiAdapters.CreateIdempotencyAdapter(host.Services.GetService<OnlineIdempotencyService>()),
    host.Report);

// 管理面监听（端口避开生产 Launcher 缺省 28090，便于同机并存）。
var admin = new AdminApiServer(new AdminApiOptions { AdminPort = 28091, AdminApiPrefix = "online/admin" }, dispatcher);

await host.StartAsync();
await admin.StartAsync();

Console.WriteLine("[{{Name}}] Online 最小组合根已启动。已装配能力: {0}", string.Join(", ", host.Modules.Select(module => module.Manifest.Id)));
Console.WriteLine("[{{Name}}] 管理面: http://127.0.0.1:28091/online/admin/{{action}}（未装配 action 不可执行：未部署 → 4002 / 已引被排除 → 4003）");
Console.WriteLine("[{{Name}}] 按 Ctrl+C 退出。");

var shutdown = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Set();
};
shutdown.Wait();

await admin.StopAsync();
await host.StopAsync();
Console.WriteLine("[{{Name}}] 已停止。");
