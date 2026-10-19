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
//   Gite Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Utility.Setting;

/// <summary>
/// Online 能力配置段（角色配置内嵌套 <c>"Online"</c> 节点的强类型绑定，C229）
/// <para>白名单 <c>Capabilities</c> 与排除表 <c>Disable</c> 承载能力 Id 列表；null 或空 = 全装配 / 无排除（缺省全装配红线）</para>
/// <para>本类型为配置只读数据：装载后不得变更（多 Role 回落档可能共享同一实例引用）</para>
/// </summary>
/// <remarks>
/// Strong-typed binding of the nested <c>"Online"</c> node inside a role configuration section (C229).
/// <c>Capabilities</c> is the enable whitelist and <c>Disable</c> the exclusion list, both carrying
/// capability ids; null or empty means "assemble everything" / "exclude nothing" (the
/// empty-filter-assembles-all guarantee). Treat instances as read-only after loading — multiple
/// roles on the launcher fallback path may share one instance reference.
/// </remarks>
public sealed class OnlineCapabilityOptions
{
    /// <summary>
    /// 能力白名单（能力 Id 列表，如 online-assets）；null 或空 = 全装配
    /// </summary>
    /// <remarks>
    /// Capability enable whitelist (capability ids, e.g. online-assets); null or empty assembles everything.
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
    /// 是否预置 Online 一级 store 的 GameDb 持久化实现（C249，D3 = 3A 分级混合）
    /// <para>true = Identity / 玩家云存储 / 资产账本与交易四 store 预置进共享服务面（重启数据不丢）；false（缺省）= InMemory 默认不变</para>
    /// <para>前置约束：开启前数据库必须已完成初始化（GameDb 门面 Init）；Session / Presence 保持易失（Redis 演进位）</para>
    /// </summary>
    /// <remarks>
    /// Whether to pre-seed the GameDb persistence adapters for the Online level-one stores (C249, D3 = 3A):
    /// true pre-seeds identity / player storage / asset ledger and transaction stores into the shared
    /// service face (data survives restarts); false (default) keeps the in-memory defaults unchanged.
    /// The database must be initialized (GameDb facade Init) before enabling; sessions and presence stay volatile.
    /// </remarks>
    public bool UseGameDbStores { get; set; }

    /// <summary>
    /// 是否启用好友查询桥接验证路径（C243）
    /// <para>true = Game 进程的 ReqFriendList 协议入口改经 Online 好友契约面（进程内桥接）读取；
    /// false（缺省）= 旧链行为逐字节保持（Social GameDb 读写归属不变）</para>
    /// <para>前置约束：开启时进程必须同时开启 <c>IsEnableOnline</c> 且装配 online-social 能力（组合根启动期 fail-fast）；
    /// 桥失败（宿主未启 / 已停 / 服务错误）按显式错误返回，绝不静默回落旧链或空列表；本开关只应配置在 Game 角色段</para>
    /// </summary>
    /// <remarks>
    /// Whether the friend-query bridge verification path is enabled (C243): true routes the Game
    /// process's ReqFriendList protocol entry through the Online friend contract face (an in-process
    /// bridge read); false (default) keeps the legacy chain byte-identical (the Social GameDb
    /// read/write ownership unchanged). Enabling requires <c>IsEnableOnline</c> and the
    /// online-social capability (composition-root fail-fast at startup); bridge failures (host not
    /// started / stopped / service errors) answer with explicit error codes and never fall back to
    /// the legacy chain or an empty list; configure this switch on the Game role section only.
    /// </remarks>
    public bool EnableFriendQueryBridge { get; set; }

    /// <summary>
    /// 好友查询桥验证种子玩家标识列表（C243；元素 &lt; 2 = 不种子）
    /// <para>首元素为请求方、其余为好友：组合根在业务运行时启动后，经真实 <c>OnlineFriendService</c> 写路径
    /// 两两建立好友（幂等收敛），验证入口（HTTP <c>mode=online-bridge-friend&amp;playerId=首元素</c>）据此读到
    /// 预置非空好友状态——仅供部署验证，生产常开须显式知情（Warning 日志披露）</para>
    /// <para>请求方取配置 id 而非进程 actor 派生 id：玩家型组件 actor id 为多实例逐次唯一生成，
    /// 不存在可预置的确定性请求主体</para>
    /// </summary>
    /// <remarks>
    /// The friend-query bridge verification seed player ids (C243; fewer than 2 entries = no seeding):
    /// the first entry is the requester and the rest the friends — after the business runtime starts
    /// the composition root establishes the friendships pairwise through the real
    /// <c>OnlineFriendService</c> write path (idempotent convergence), so the verification entry
    /// (HTTP <c>mode=online-bridge-friend&amp;playerId=first-id</c>) can read preset non-empty friend
    /// state. Deployment verification only; keeping it on in production requires informed consent (a
    /// Warning log discloses every seed). The requester comes from configuration rather than a
    /// process-derived actor id: player-type component actor ids are per-call unique multi ids with no
    /// deterministic request subject to preset.
    /// </remarks>
    public IReadOnlyList<long> FriendQueryBridgeSeedPlayerIds { get; set; }
}
