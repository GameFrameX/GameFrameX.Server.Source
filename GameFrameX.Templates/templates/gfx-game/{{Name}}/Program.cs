// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//  ==========================================================================================

using System;
using System.Text.Json;
using System.Threading;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;

using {{Name}};

// {{Name}} —— 用 GameFrameX 做一个游戏的起点（gfx-game 模板生成）。
// 独立工程：以 GameFrameX 2.0 NuGet 包消费框架能力，引啥装配啥——
// CapabilityComposition.Compose 按 Online 族部署前缀扫描本进程加载的能力包，
// csproj 引用了哪些能力包就装配哪些能力（编译期保证）。缺省勾选黄金路径最小集：
// 开局 → 对局裁决 → 结算 → 发奖（统一资产入口）→ 默认榜上榜，零 JSON 配置开箱即通。
// 参考：官方游戏仓 https://github.com/GameFrameX/GameFrameX.Server （四子棋完整活演示）。

// 授权作用域区服键（单键化：作用域只剩 ServerId；接入配置管道时改走嵌套强类型段 "Online": { ... }）。
const long serverId = 1;

// 族配置段（零 Settings = 全部走包内默认参数）。
var familyConfigurationSection = JsonSerializer.SerializeToElement(new
{
    ServerId = serverId,
    Settings = new { },
});

var options = new CapabilityCompositionOptions(familyConfigurationSection, new ConsoleCapabilityAssemblyLog());
options.HostOptions.ServerId = serverId;

// 管理面：admin action 注册 + 元端点（list_capability_services / list_capability_settings / query_leaderboard 等）。
options.UseAdminApi(adminOptions =>
{
    adminOptions.AdminPort = 28092;
    adminOptions.AdminApiPrefix = "online/admin";
});

// 宿主扩展点：能力以 RequireHostService 声明、包内无默认实现，宿主必须 Override 提供（缺失 = 装配期 fail-fast）。
// 玩家目录供 online-social 好友搜索使用；本起点工程不接玩家库，提供空目录。
options.Override<IOnlinePlayerDirectory>(new EmptyPlayerDirectory());
// 唯一组合入口：族骨架 + 机制装配（依赖检查、拓扑定序、Options 绑定、装配日志一次报全）。
var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
await runtime.StartAsync();

Console.WriteLine("[{{Name}}] 游戏宿主已启动。已装配能力（拓扑序）: {0}", string.Join(", ", runtime.Report.EnabledIdsInOrder));
// 管理面如实打印：无鉴权且监听所有网卡（非仅回环）——本机地址仅为示例入口，实际可经任意网卡可达。
Console.WriteLine("[{{Name}}] 管理面: http://127.0.0.1:28092/online/admin/{{action}}（无鉴权，监听所有网卡，请用防火墙/安全组限制访问）");
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
