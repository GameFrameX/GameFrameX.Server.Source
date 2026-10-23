// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.IO;
using System.Reflection;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Competition.Leaderboard;
using GameFrameX.Capability.Online.Operations;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Operations.Audit;
using GameFrameX.Capability.Online.Persistence;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// Online 能力组合编排选项（<see cref="OnlineCapabilityComposition.AddOnlineCapabilities"/> 回调承载面）。
/// <para>作用域三元组、调度间隔、双闸（EnableRuntime / EnableAdminApi）、管理面监听与能力过滤器
/// （Capabilities 白名单 / Disable 排除表）统一经回调写入（.NET <c>AddXxx(options =&gt; ...)</c> 惯例）。</para>
/// </summary>
/// <remarks>
/// Orchestration options carried by the <see cref="OnlineCapabilityComposition.AddOnlineCapabilities"/>
/// callback): the scope triple, scheduler interval, the two enable gates
/// (<see cref="EnableRuntime"/> / <see cref="EnableAdminApi"/>), the admin listening options and the
/// capability filter (<see cref="Capabilities"/> whitelist / <see cref="Disable"/> exclusion list) all
/// arrive through the callback, following the .NET <c>AddXxx(options =&gt; ...)</c> convention.
/// </remarks>
public sealed class OnlineCapabilityCompositionOptions
{
    /// <summary>
    /// 授权租户标识（作用域三元组成员；跨租户请求按 3002 拒绝）
    /// </summary>
    /// <remarks>
    /// Authorized tenant id (part of the scope triple; cross-tenant requests are rejected with 3002).
    /// </remarks>
    public long TenantId { get; set; }

    /// <summary>
    /// 授权应用标识（作用域三元组成员；跨 App 请求按 3003 拒绝）
    /// </summary>
    /// <remarks>
    /// Authorized app id (part of the scope triple; cross-app requests are rejected with 3003).
    /// </remarks>
    public long AppId { get; set; }

    /// <summary>
    /// 宿主服务器标识（作用域三元组成员；跨服请求按 3004 拒绝）
    /// </summary>
    /// <remarks>
    /// Host server id (part of the scope triple; cross-server requests are rejected with 3004).
    /// </remarks>
    public long ServerId { get; set; }

    /// <summary>
    /// 调度滴答间隔（毫秒，默认 1000；小于 100 由调度壳钳制为 100）
    /// </summary>
    /// <remarks>
    /// Scheduler tick interval in milliseconds (default 1000; values below 100 are clamped to 100 by the scheduler).
    /// </remarks>
    public int SchedulerIntervalMilliseconds { get; set; } = 1000;

    /// <summary>
    /// 是否装配业务运行时（对应 <c>AppSetting.IsEnableOnline</c>；false 时不构造业务能力宿主）
    /// </summary>
    /// <remarks>
    /// Whether to assemble the business runtime (mirrors <c>AppSetting.IsEnableOnline</c>);
    /// when false no business capability host is constructed at all.
    /// </remarks>
    public bool EnableRuntime { get; set; }

    /// <summary>
    /// 是否装配管理面（对应 <c>AppSetting.IsEnableOnlineAdmin</c>；业务运行时在则全能力形态，不在则 admin-only 形态）
    /// </summary>
    /// <remarks>
    /// Whether to assemble the admin plane (mirrors <c>AppSetting.IsEnableOnlineAdmin</c>);
    /// full-capability shape when the runtime is assembled, admin-only shape otherwise.
    /// </remarks>
    public bool EnableAdminApi { get; set; }

    /// <summary>
    /// 管理面监听选项（null = 缺省 28090 / online-admin）
    /// </summary>
    /// <remarks>
    /// Admin listening options (null = defaults 28090 / online-admin).
    /// </remarks>
    public AdminApiOptions AdminApi { get; set; }

    /// <summary>
    /// 能力白名单（能力 Id 列表，如 online-assets）；null 或空 = 全装配；白名单未含依赖时组合期闭包校验 fail-fast
    /// </summary>
    /// <remarks>
    /// Capability enable whitelist (capability ids, e.g. online-assets); null or empty assembles
    /// everything, and a whitelist that omits a required dependency fails fast at composition time.
    /// </remarks>
    public IReadOnlyList<string> Capabilities { get; set; }

    /// <summary>
    /// 能力排除表（能力 Id 列表）；null 或空 = 无排除
    /// </summary>
    /// <remarks>
    /// Capability exclusion list (capability ids); null or empty excludes nothing.
    /// </remarks>
    public IReadOnlyList<string> Disable { get; set; }

    /// <summary>
    /// 是否预置 Online 一级 store 的 GameDb 持久化实现
    /// <para>true = Identity / 玩家云存储 / 资产账本与交易四 store 经 <c>UseOnlineGameDbStores</c> 预置进共享服务面（模块 Register 期探测命中）；false（缺省）= InMemory 默认不变（零依赖起跑红线）</para>
    /// <para>前置约束：数据库须先于能力装配完成初始化（GameDb 门面 Init）；Session / Presence 二级易失 store 不受本开关影响</para>
    /// </summary>
    /// <remarks>
    /// Whether to pre-seed the GameDb persistence adapters for the Online level-one stores: true
    /// pre-seeds the identity / player-storage / asset-ledger and asset-transaction
    /// stores through <c>UseOnlineGameDbStores</c> into the shared service face (probed at module
    /// Register time); false (default) keeps the in-memory defaults unchanged (the zero-dependency
    /// boot guarantee). The database must be initialized before capability assembly; the volatile
    /// level-two stores (sessions / presence) are unaffected by this switch.
    /// </remarks>
    public bool UseGameDbStores { get; set; }

    /// <summary>
    /// 对局结算写榜榜单标识：非空 = 预置 <c>OnlineLeaderboardProjectionOptions</c> 共享服务，
    /// online-competition 模块据此装配 Match.Settled 可信结果投影（要求 online-match / online-competition 能力在装配面）；
    /// 空（缺省）= 不预置，投影不装配。
    /// </summary>
    /// <remarks>
    /// The leaderboard id that settled match results project onto: non-empty pre-seeds the
    /// <c>OnlineLeaderboardProjectionOptions</c> shared service from which the online-competition
    /// module assembles the Match.Settled trusted-result projection (requires the online-match /
    /// online-competition capabilities on the assembly face); empty (default) pre-seeds nothing and
    /// the projection stays unassembled.
    /// </remarks>
    public string MatchBridgeLeaderboardId { get; set; }
}
