// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes and liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/cnblanblank
//   CNB 仓库：https://cnb.cool
//   CNB Repository: https://cnb.cool
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Text.Json;

namespace GameFrameX.Utility.Setting;

/// <summary>
/// Online 能力配置段（角色配置内嵌套 <c>"Online"</c> 节点的强类型绑定，C294 键集重定）
/// <para>族级键：双闸（<see cref="EnableRuntime"/> / <see cref="EnableAdminApi"/>）与
/// <see cref="EnableRoles"/>（缺省 <c>["Game"]</c>，D6 Role↔Capability 声明式映射）、能力过滤器
/// （<see cref="Capabilities"/> 白名单 / <see cref="Disable"/> 排除表，null 或空 = 全装配 / 无排除）、
/// <see cref="UseGameDbStores"/>（store 覆盖开关）；能力级参数经 <see cref="Settings"/> 原始 JSON 节点承载
/// （键 = 能力 Id，值 = 该能力 Options 根类型的字段覆盖源，装配期由机制 Options 绑定器逐字段合并）</para>
/// <para>本类型为配置只读数据：装载后不得变更（多 Role 回落档可能共享同一实例引用）</para>
/// </summary>
/// <remarks>
/// Strong-typed binding of the nested <c>"Online"</c> node inside a role configuration section (the C294
/// key-set redefinition). Family-level keys: the two enable gates
/// (<see cref="EnableRuntime"/> / <see cref="EnableAdminApi"/>) plus <see cref="EnableRoles"/>
/// (default <c>["Game"]</c>, the D6 declarative role-to-capability mapping), the capability filter
/// (<see cref="Capabilities"/> whitelist / <see cref="Disable"/> exclusion list; null or empty assembles
/// everything / excludes nothing) and the <see cref="UseGameDbStores"/> store-override switch;
/// capability-level parameters ride the raw JSON node <see cref="Settings"/> (key = capability id, value =
/// the field-override source for that capability's options root, merged field-by-field at composition time
/// by the mechanism options binder). Treat instances as read-only after loading — multiple roles on the
/// launcher fallback path may share one instance reference.
/// </remarks>
public sealed class OnlineCapabilityOptions
{
    /// <summary>
    /// 缺省启用角色表（<see cref="EnableRoles"/> 为 null 时等效本值）。
    /// </summary>
    /// <remarks>
    /// The default enabled-role list (equivalent to this value when <see cref="EnableRoles"/> is null).
    /// </remarks>
    public static readonly IReadOnlyList<string> DefaultEnableRoles = new[] { "Game" };

    /// <summary>
    /// 是否装配业务运行时（并入配置段的旧双闸之一；缺省 false = 不装配）
    /// </summary>
    /// <remarks>
    /// Whether to assemble the business runtime (the in-section successor of the former flat
    /// runtime gate; default false = not assembled).
    /// </remarks>
    public bool EnableRuntime { get; set; }

    /// <summary>
    /// 是否装配管理面（并入配置段的旧双闸之一；缺省 false = 不装配；
    /// 业务运行时在则全能力形态，不在则 admin-only 形态）
    /// </summary>
    /// <remarks>
    /// Whether to assemble the admin plane (the in-section successor of the former flat
    /// admin gate; default false = not assembled; full-capability shape when the
    /// runtime is assembled, admin-only shape otherwise).
    /// </remarks>
    public bool EnableAdminApi { get; set; }

    /// <summary>
    /// 启用本族的 Role 名单（D6 声明式映射；null 或空 = 缺省 <c>["Game"]</c>）——
    /// <c>--AllInOne</c> 多 Role 进程中首个名单命中的 Role 负责组合，后续 Role 复用同一进程内组合
    /// </summary>
    /// <remarks>
    /// The role names that enable this family (the D6 declarative mapping; null or empty = the default
    /// <c>["Game"]</c>) — under <c>--AllInOne</c> the first role named by the list composes and the
    /// remaining roles reuse the same in-process composition.
    /// </remarks>
    public IReadOnlyList<string> EnableRoles { get; set; }

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
    /// 是否预置 Online 一级 store 的 GameDb 持久化实现（C294 起语义为宿主组合根 Override 注入开关；C302 起为三态）
    /// <para><see langword="true"/> = 宿主经 <c>Override&lt;T&gt;</c> 预置身份 / 玩家云存储 / 资产账本与交易 / 好友关系 store
    /// （重启数据不丢）；<see langword="false"/> = 显式选择包内 InMemory 默认；<see langword="null"/>（缺省）= 配置未声明——
    /// 组合根对此形态发出启动期告警（无声降级防护，见 C302）后按 InMemory 处理</para>
    /// <para>前置约束：开启前数据库必须已完成初始化（GameDb 门面 Init）；Session / Presence 保持易失（Redis 演进位）</para>
    /// </summary>
    /// <remarks>
    /// The tri-state host composition-root switch for the GameDb persistence adapters of the Online
    /// level-one stores (C294 semantics; tri-state since C302): <see langword="true"/> overrides the
    /// identity / player-storage / asset-ledger and transaction / friend stores via
    /// <c>Override&lt;T&gt;</c> (data survives restarts); <see langword="false"/> explicitly keeps the
    /// in-package in-memory defaults; <see langword="null"/> (default) means the configuration never
    /// declared the storage mode — the composition root warns at startup (the silent-degradation guard,
    /// C302) and then falls back to the in-memory defaults. The database must be initialized (GameDb
    /// facade Init) before enabling; sessions and presence stay volatile.
    /// </remarks>
    public bool? UseGameDbStores { get; set; }

    /// <summary>
    /// 能力级参数原始 JSON 节点（键 = 能力 Id（如 online-social），值 = 该能力 Options 根类型的字段覆盖源；
    /// 未知能力 Id / 未知字段 / 类型不符在装配期 fail-fast）。未配置时为 <see cref="JsonElement"/> 缺省值
    ///（<see cref="JsonValueKind.Undefined"/>，等效空对象）。
    /// </summary>
    /// <remarks>
    /// The raw JSON node of capability-level parameters (key = capability id such as online-social, value =
    /// the field-override source for that capability's options root; unknown capability ids / unknown
    /// fields / type mismatches fail fast at composition time). Unconfigured leaves the
    /// <see cref="JsonElement"/> default (<see cref="JsonValueKind.Undefined"/>, equivalent to an empty object).
    /// </remarks>
    public JsonElement Settings { get; set; }

    /// <summary>
    /// 读取生效的启用角色表（<see cref="EnableRoles"/> 为 null 或空时返回缺省 <c>["Game"]</c>）。
    /// </summary>
    /// <remarks>
    /// Reads the effective enabled-role list (returns the default <c>["Game"]</c> when
    /// <see cref="EnableRoles"/> is null or empty).
    /// </remarks>
    /// <returns>生效的启用角色名单。</returns>
    public IReadOnlyList<string> GetEffectiveEnableRoles()
    {
        return EnableRoles == null || EnableRoles.Count == 0 ? DefaultEnableRoles : EnableRoles;
    }
}
