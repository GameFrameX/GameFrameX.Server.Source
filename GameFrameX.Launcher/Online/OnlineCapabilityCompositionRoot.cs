// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool
//   CNB Repository: https://cnb.cool
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Assets;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.Capability.Online.Foundation.Storage;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Persistence.Assets;
using GameFrameX.Capability.Online.Persistence.Identity;
using GameFrameX.Capability.Online.Persistence.Social;
using GameFrameX.Capability.Online.Persistence.Storage;
using GameFrameX.Capability.Online.Match;
using GameFrameX.Capability.Online.Persistence.Match;
using GameFrameX.Capability.Online.Social;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Launcher.Online;

/// <summary>
/// Online 能力族组合根（C294，替代退役的旧 Launcher 单入口装配）：
/// 模板序列的固定步骤经本类型组装 <c>online</c> 族——构造 <see cref="CapabilityCompositionOptions"/>
///（族配置段 JSON + LogHelper 装配日志 + 双闸 + 作用域三元组 + 能力过滤器 + 管理面监听），
/// 经 <see cref="CapabilityComposition.Compose"/> 产出 <see cref="CapabilityRuntime"/> 并启动。
/// <para>
/// 维护约束（红线）：
/// ① 同族进程内单例——<c>--AllInOne</c> 多 Role 进程中首个 <c>EnableRoles</c> 命中的 Role 完成组合，
/// 其余命中 Role 直接复用（<see cref="ComposeIfEnabled"/> 的互斥闸），停止由组合 Role 的停机步承担；
/// ② 宿主扩展点全部走 <c>Override&lt;T&gt;</c>——Hotfix 版本切换执行器、玩家目录、
/// GameDb 一级 store 覆盖（<c>Online.UseGameDbStores</c>，五 store 适配器来自 Persistence 包）；
/// ③ 双闸全关（EnableRuntime 与 EnableAdminApi 均 false）时零装配零日志（现状等价）；
/// ④ 族配置段以原始 JSON 重建（族级键 + 机制使用的 Settings 节点），能力级参数由机制 Options
/// 绑定器逐字段合并、未知键 fail-fast。
/// </para>
/// </summary>
/// <remarks>
/// The Online capability-family composition root (C294, replacing the retired Launcher single-entry
/// assembly): the fixed template-sequence step assembles the <c>online</c> family through this
/// type — building the <see cref="CapabilityCompositionOptions"/> (the family configuration-section
/// JSON, the LogHelper assembly log, the two gates, the scope triple, the capability filter and the
/// admin listening options), producing a <see cref="CapabilityRuntime"/> via
/// <see cref="CapabilityComposition.Compose"/> and starting it. Red lines: one composition per
/// process for the family — under <c>--AllInOne</c> the first role named by <c>EnableRoles</c>
/// composes and the rest reuse it (the mutual-exclusion gate in <see cref="ComposeIfEnabled"/>),
/// with the stop owned by the composing role's shutdown step; every host extension point rides
/// <c>Override&lt;T&gt;</c> — the hotfix rollback executor, the player directory and the GameDb
/// level-one store overrides (<c>Online.UseGameDbStores</c>, the five adapters from the Persistence
/// package); with both gates off nothing is assembled and no log fires; the family configuration
/// section is rebuilt as raw JSON (family keys plus the Settings node consumed by the mechanism),
/// capability-level parameters merged field-by-field by the mechanism options binder with unknown
/// keys failing fast.
/// </remarks>
public static class OnlineCapabilityCompositionRoot
{
    /// <summary>
    /// 进程内组合产物（同族单例；未组合为 null）。
    /// </summary>
    /// <remarks>
    /// The in-process composition product (the per-family singleton; null before composition).
    /// </remarks>
    private static CapabilityRuntime _runtime;

    /// <summary>
    /// 组合互斥闸（0 = 未组合 / 组合失败已复位，1 = 有 Role 正在组合或已完成组合）。
    /// <para>维护约束：只在双闸判定通过、真正要组装时才抢闸；组装或启动失败必须复位为 0，
    /// 否则后续命中 <c>EnableRoles</c> 的 Role 会被静默跳过。</para>
    /// </summary>
    /// <remarks>
    /// The composition mutual-exclusion gate (0 = not composed or reset after a failed composition,
    /// 1 = a role is composing or has composed). Claimed only once the two gates pass and assembly
    /// really happens; reset to 0 on any composition or start failure.
    /// </remarks>
    private static int _composed;

    /// <summary>
    /// 组合者 Role 名（ServerType；未组合为 null）。族运行时的停止只归该 Role 的停机步（设计 §4.6）。
    /// </summary>
    /// <remarks>
    /// The composing role name (ServerType; null before composition). Only that role's shutdown
    /// step stops the family runtime.
    /// </remarks>
    private static string _composerServerType;

    /// <summary>
    /// 停止单次闸（0 = 未停止，1 = 已进入停止；停机幂等护栏的族级对应物）。
    /// </summary>
    /// <remarks>
    /// The stop-once gate (0 = running, 1 = stopping/stopped; the family-level twin of the role
    /// stop guard).
    /// </remarks>
    private static int _stopped;

    /// <summary>
    /// 按配置段组合并启动 Online 能力族（模板序列固定步骤的承载入口，热更装载之后、网络监听之前调用）。
    /// <para>判定链："Online" 配置段缺席 → 不组合；<c>EnableRoles</c>（缺省 ["Game"]）不含本 Role 名 →
    /// 不组合；双闸全关 → 不组合（零装配零日志，且不占用互斥闸）；进程内已有组合 → 复用（本 Role 不再装配）。
    /// 其余路径完成组装并启动（启动序 = 业务宿主 → 管理面），并记录本 Role 为组合者。</para>
    /// <para>失败语义：组装或启动抛异常时，已创建的运行时先尽力停止（停止失败只记日志、不覆盖原异常），
    /// 再清空产物并复位互斥闸，原异常继续上抛。</para>
    /// </summary>
    /// <param name="setting">当前 Role 的应用配置。</param>
    /// <returns>表示组合与启动操作的异步任务。</returns>
    public static async Task ComposeIfEnabledAsync(AppSetting setting)
    {
        var online = setting.Online;
        if (online == null)
        {
            return;
        }

        if (!IsRoleEnabled(online, setting.ServerType))
        {
            return;
        }

        if (!online.EnableRuntime && !online.EnableAdminApi)
        {
            return;
        }

        // 同族进程内单例：首个命中 Role 完成组合，后续 Role 复用（D6）。
        if (System.Threading.Interlocked.CompareExchange(ref _composed, 1, 0) != 0)
        {
            return;
        }

        try
        {
            var options = new CapabilityCompositionOptions(BuildFamilyConfigurationSection(setting), new LogHelperCapabilityAssemblyLog())
            {
                EnableRuntime = online.EnableRuntime,
                HostOptions = new CapabilityHostOptions
                {
                    TenantId = setting.OnlineTenantId,
                    AppId = setting.OnlineAppId,
                    ServerId = setting.ServerId,
                },
                Filter = new CapabilityAssemblyFilter(online.Capabilities, online.Disable),
            };

            if (online.EnableAdminApi)
            {
                options.UseAdminApi(adminOptions =>
                {
                    adminOptions.AdminPort = setting.OnlineAdminPort;
                    adminOptions.AdminApiPrefix = setting.OnlineAdminApiPrefix;
                });
            }

            // 宿主扩展点（Override 注入，来源 = 宿主组合根）：Hotfix 版本切换执行器、玩家目录与回滚状态日志。
            // 类型实参必须显式写成契约接口：描述符表按类型键索引，省略时推断为具型，模块的 RequireHostService<契约> 找不到覆盖。
            options.Override<IOnlineHotfixRollbackExecutor>(new HotfixManagerRollbackExecutor(setting));
            options.Override<IOnlinePlayerDirectory>(new HostPlayerDirectory());
            options.Override<IOnlineHotfixRollbackLog>(new LogHelperHotfixRollbackLog());

            // GameDb 一级 store 覆盖（Online.UseGameDbStores 三态，C302）：true 预置五个持久化适配器（业务库已就绪由模板顺序保证）；
            // 未声明（null）= 无声降级防护位——启用族但未声明存储模式时告警（不 fail-fast，保护既有无键部署），随后保持 InMemory 默认。
            if (online.UseGameDbStores == true)
            {
                options.Override<IOnlineIdentityStore>(new GameDbOnlineIdentityStore());
                options.Override<IOnlinePlayerStorageStore>(new GameDbOnlinePlayerStorageStore());
                options.Override<IOnlineAssetStore>(new GameDbOnlineAssetStore());
                options.Override<IOnlineAssetTransactionStore>(new GameDbOnlineAssetTransactionStore());
                options.Override<IOnlineFriendStore>(new GameDbOnlineFriendStore());
                var matchSettlementStore = new GameDbOnlineMatchSettlementStore();
                options.Override<IOnlineMatchResultStore>(matchSettlementStore);
                options.Override<IOnlineMatchSettlementStore>(matchSettlementStore);
            }
            else if (ShouldWarnStoreModeUndeclared(online))
            {
                LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Capability.OnlineModule.UseGameDbStoresUndeclared, setting.ServerType));
            }

            _runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
            _composerServerType = setting.ServerType;
            await _runtime.StartAsync();
        }
        catch
        {
            await StopFailedRuntimeAsync();
            _runtime = null;
            _composerServerType = null;
            System.Threading.Volatile.Write(ref _composed, 0);
            throw;
        }
    }

    /// <summary>
    /// 判定是否需要发出「存储模式未声明」启动期告警（C302 无声降级防护）：
    /// 族被启用（双闸任一开）但 <c>Online.UseGameDbStores</c> 未在配置中显式声明（null）时为 true。
    /// <para>为什么不直接在组合点内联判定：三态口径（未声明 / 显式 true / 显式 false）是配置契约本身，
    /// 收敛为纯谓词供单测直接断言，避免只能靠日志输出验证信号。</para>
    /// <para>显式 false（有意选择 InMemory，如本地无库开发）与双闸全关（族未启用，零装配零日志）均不告警。</para>
    /// </summary>
    /// <remarks>
    /// Determines whether the "storage mode undeclared" startup warning must fire (the C302
    /// silent-degradation guard): true when the family is enabled (either gate on) yet
    /// <c>Online.UseGameDbStores</c> was never explicitly declared in configuration (null). The
    /// tri-state contract (undeclared / explicit true / explicit false) is collapsed into this pure
    /// predicate so unit tests can assert it directly instead of scraping log output; an explicit
    /// false (a deliberate in-memory choice, e.g. database-less local development) and both gates off
    /// (family disabled: nothing assembled, nothing logged) never warn.
    /// </remarks>
    /// <param name="online">当前 Role 的 Online 配置段。</param>
    /// <returns>未声明存储模式且族已启用时为 true；否则为 false。</returns>
    public static bool ShouldWarnStoreModeUndeclared(OnlineCapabilityOptions online)
    {
        return online != null
            && (online.EnableRuntime || online.EnableAdminApi)
            && online.UseGameDbStores == null;
    }

    /// <summary>
    /// 组合失败路径的尽力停止：撤回已启动部分（桥 / 调度 / 管理面），停止自身的异常只记日志，
    /// 不得覆盖触发回滚的原异常。
    /// </summary>
    /// <returns>表示停止操作的异步任务。</returns>
    private static async Task StopFailedRuntimeAsync()
    {
        if (_runtime == null)
        {
            return;
        }

        try
        {
            await _runtime.StopAsync();
        }
        catch (System.Exception stopException)
        {
            LogHelper.Error(stopException.ToString());
        }
    }

    /// <summary>
    /// 停止进程内组合产物（停 admin → 停调度 → 逆序 Deactivate → 自动撤桥）。
    /// 只有组合者 Role 的停机步真正执行族停机；非组合 Role、未组合或已停止时为空操作。
    /// </summary>
    /// <param name="serverType">调用方 Role 名（ServerType）。</param>
    /// <returns>表示停止操作的异步任务。</returns>
    public static async Task StopIfComposedAsync(string serverType)
    {
        if (_runtime == null || !string.Equals(_composerServerType, serverType, System.StringComparison.Ordinal))
        {
            return;
        }

        if (System.Threading.Interlocked.Exchange(ref _stopped, 1) != 0)
        {
            return;
        }

        try
        {
            await _runtime.StopAsync();
        }
        finally
        {
            _runtime = null;
        }
    }

    /// <summary>
    /// 判定 <c>EnableRoles</c>（缺省 ["Game"]）是否命中本 Role。
    /// </summary>
    /// <param name="online">Online 配置段。</param>
    /// <param name="serverType">本 Role 名（ServerType）。</param>
    /// <returns>命中返回 true。</returns>
    private static bool IsRoleEnabled(OnlineCapabilityOptions online, string serverType)
    {
        var enableRoles = online.GetEffectiveEnableRoles();
        for (var index = 0; index < enableRoles.Count; index++)
        {
            if (string.Equals(enableRoles[index], serverType, System.StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 重建族配置段原始 JSON（族级键 + Settings 节点）：族脊柱包读取 TenantId / AppId / ServerId 落授权作用域，
    /// 机制 Options 绑定器读取 Settings.&lt;能力Id&gt; 做能力级参数覆盖；JsonElement 底层 = System.Text.Json
    ///（GameFrameX.Foundation.Json 5.0.0 实证引用 System.Text.Json，配置装载同为 System.Text.Json 反序列化）。
    /// </summary>
    /// <param name="setting">当前 Role 的应用配置。</param>
    /// <returns>族配置段 JSON 节点。</returns>
    private static JsonElement BuildFamilyConfigurationSection(AppSetting setting)
    {
        var online = setting.Online;
        var section = new Dictionary<string, object>
        {
            ["TenantId"] = setting.OnlineTenantId,
            ["AppId"] = setting.OnlineAppId,
            ["ServerId"] = setting.ServerId,
            ["EnableRuntime"] = online.EnableRuntime,
            ["EnableAdminApi"] = online.EnableAdminApi,
            ["UseGameDbStores"] = online.UseGameDbStores,
            ["Settings"] = online.Settings.ValueKind == JsonValueKind.Undefined || online.Settings.ValueKind == JsonValueKind.Null ? new Dictionary<string, object>() : online.Settings,
        };
        return JsonSerializer.SerializeToElement(section);
    }
}
