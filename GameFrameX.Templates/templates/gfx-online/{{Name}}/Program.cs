// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws and regulations of the People's Republic of China and relevant international regulations.
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
//
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes or liabilities arising from secondary development of this project
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

using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
//#if (Operations)
using GameFrameX.Capability.Online.Operations.HotfixRollback;
//#endif
using {{Name}};

// {{Name}} —— Online 能力最小组合根宿主（gfx-online 模板生成，C230 组合根骨架 + C294 机制化组合）。
// 引啥装配啥：CapabilityComposition.Compose 按 Online 族部署前缀扫描本进程加载的能力包，
// csproj 引用了哪些能力包（DLL 随构建落盘）就装配哪些能力（编译期保证）；
// 未装配能力的 admin action 不可执行——未部署（未引用）→ 4002 UnknownAction；
// 已引用但被过滤排除 → 4003 CapabilityNotInstalled（载荷带已装清单）。
// 增减能力 = 只增删 csproj ProjectReference（引用集即装配集，无按包装配代码）。

// 授权作用域三元组（演示值）。接入配置管道时改走嵌套强类型段 "Online": { ... }（禁平铺键，
// 能力族全局约定 2026-10-12）。
const long tenantId = 1;
const long appId = 1;
const long serverId = 1;

// 族配置段（演示值）：族脊柱包从段内读取授权作用域三元组（TenantId / AppId / ServerId，缺失或非正数
// = 装配期 fail-fast）；机制只把其中的 Settings 子对象按字段绑定到各能力 Options（空 = 全部走包内默认参数，
// 未知能力 Id / 未知字段 / 类型不符 = fail-fast）。接入配置管道后把 "Online" 段整段读成 JsonElement 传入；
// 族级键（EnableRoles / Capabilities / Disable / UseGameDbStores 等）由宿主自行解析后落到 options（如 options.Filter）。
var familyConfigurationSection = JsonSerializer.SerializeToElement(new
{
    TenantId = tenantId,
    AppId = appId,
    ServerId = serverId,
    Settings = new { },
});

var options = new CapabilityCompositionOptions(familyConfigurationSection, new ConsoleCapabilityAssemblyLog());
options.HostOptions.TenantId = tenantId;
options.HostOptions.AppId = appId;
options.HostOptions.ServerId = serverId;

// 管理面：UseAdminApi 挂 admin 组装（action 注册 + 双向对账 + 元端点 list_capability_services /
// list_capability_settings）。端口避开生产 Launcher 缺省 28090，便于同机并存。
options.UseAdminApi(adminOptions =>
{
    adminOptions.AdminPort = 28091;
    adminOptions.AdminApiPrefix = "online/admin";
});

// 宿主扩展点：能力以 RequireHostService 声明、包内无默认实现，宿主必须 Override 提供（缺失 = 装配期 fail-fast）。
// 玩家目录供 online-social 好友搜索 / 展示名使用；本最小宿主不接玩家库，提供空目录（未装配 online-social 时无人解析，覆盖无副作用）。
options.Override<IOnlinePlayerDirectory>(new EmptyPlayerDirectory());
//#if (Operations)
// Hotfix 版本切换执行器供 online-operations 回滚 action 使用；本最小宿主不承载热更运行时，执行即显式失败。
options.Override<IOnlineHotfixRollbackExecutor>(new UnsupportedHotfixRollbackExecutor());
//#endif

// 代码层参数覆盖（可选示例；只改需要改的字段，合并优先级：包内默认 < 配置段 Settings < 代码
// Configure）。替换带默认实现的扩展点同样走 options.Override<T>(...)（如宿主 GameDb store）。
// options.Configure<OnlineSocialOptions>(configured => configured.Friend.FriendLimit = 500);

// 唯一组合入口：族骨架 + 机制装配（依赖检查、拓扑定序、Options 绑定、装配日志一次报全）。
// 启停序由 CapabilityRuntime 内含：启动 Host → Admin；停止 Admin → Host（桥撤销随宿主停止自动完成）。
var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
await runtime.StartAsync();

Console.WriteLine("[{{Name}}] Online 最小组合根已启动。已装配能力（拓扑序）: {0}", string.Join(", ", runtime.Report.EnabledIdsInOrder));
Console.WriteLine("[{{Name}}] 管理面: http://127.0.0.1:28091/online/admin/{{action}}（未装配 action 不可执行：未部署 → 4002 / 已引被排除 → 4003）");
Console.WriteLine("[{{Name}}] 按 Ctrl+C 退出。");

var shutdown = new ManualResetEventSlim(false);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    shutdown.Set();
};
shutdown.Wait();

await runtime.StopAsync();
Console.WriteLine("[{{Name}}] 已停止。");
