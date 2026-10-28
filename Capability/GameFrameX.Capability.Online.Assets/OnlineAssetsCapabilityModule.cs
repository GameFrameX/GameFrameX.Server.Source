// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file located at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织与贡献者概不承担任何责任，开发者须自行承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://github.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://github.com/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Assets.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;

namespace GameFrameX.Capability.Online.Assets;

/// <summary>
/// 资产能力（钱包 / 库存 / 账本 / 统一发放）的自装配模块（C231 P3 迁入；C294 改写为三段生命周期机制模块）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（<see cref="CapabilityAssembly"/> 扫描可发现）；<c>Register</c>
/// 自装配本域——两 store 与跨服补偿队列以默认注册后取用（族 / 宿主 Override 自动胜出，模块不做探测回退；
/// 补偿队列默认为惰性形态——无传输即无自动续投，宿主装配真实传输后以 Override 注册同型队列）；
/// 跨域硬依赖（幂等 / 事件出口 / 审计读面）经上下文解析（审计读面为族基建预置的 <see cref="IOnlineAuditReader"/>）；
/// 对外产物（查询 / 发放服务）注册回共享面供后序能力取用。本能力无 Options 根类型、无依赖声明；
/// <c>ActivateAsync</c> 无桥与预建资源（空实现）、<c>DeactivateAsync</c> 无自持资源（空实现）。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the assets capability (wallet / inventory / ledger / unified
/// granting; C231 P3, rewritten by C294 for the three-stage lifecycle). The module has a
/// parameterless constructor (discoverable by <see cref="CapabilityAssembly"/> scanning);
/// <c>Register</c> self-assembles the domain — the two stores register lazy in-memory defaults and
/// resolve through the shared face (family / host overrides win automatically, no probing
/// fallback); cross-domain hard dependencies (idempotency / the event outlet / the audit read face)
/// resolve through the context (the audit read face is the family-preset
/// <see cref="IOnlineAuditReader"/>); outward products (the query / grant services) are registered
/// back into the shared face for later capabilities. The capability declares no options root and no
/// dependencies; <c>ActivateAsync</c> / <c>DeactivateAsync</c> hold nothing to do.
/// </remarks>
public sealed class OnlineAssetsCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 资产能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块；无依赖、无 Options）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-assets",
        "1.0.0",
        new[]
        {
            "grant_asset",
            "revoke_asset",
            "query_wallet_ledger",
            "query_asset_audit",
            "query_transaction_detail",
            "query_compensation_queue",
            "resolve_compensation_manual_review",
        });

    /// <summary>
    /// 资产存储（钱包 / 库存 / 账本；Register 期解析）。
    /// </summary>
    private IOnlineAssetStore _assetStore;

    /// <summary>
    /// 资产交易存储（Register 期解析）。
    /// </summary>
    private IOnlineAssetTransactionStore _assetTransactionStore;

    /// <summary>
    /// 资产查询服务（Register 期构造）。
    /// </summary>
    private OnlineAssetQueryService _assetQuery;

    /// <summary>
    /// 统一发放入口（Register 期构造）。
    /// </summary>
    private OnlineGrantService _grantService;

    /// <summary>
    /// 跨服补偿队列（Register 期懒默认注册后取用，宿主/族 Override 胜出；Admin 观测/裁决承载）。
    /// </summary>
    private OnlineGrantCompensationQueue _compensationQueue;

    /// <summary>
    /// 统一审计读面（Register 期解析；族基建预置 OnlineAuditService 的读面）。
    /// </summary>
    private IOnlineAuditReader _audit;

    /// <summary>
    /// 获取资产能力清单，固定返回静态实例 <see cref="ManifestInstance"/>。
    /// </summary>
    /// <remarks>
    /// Gets the assets capability manifest; always returns the static <see cref="ManifestInstance"/>.
    /// </remarks>
    /// <value>资产能力清单 / Assets capability manifest</value>
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <summary>
    /// 懒默认注册内存版资产存储与资产交易存储并解析实际生效实例，解析审计读面、幂等服务与事件发布器，构造资产查询服务与统一发放服务并注册回共享服务面。
    /// </summary>
    /// <remarks>
    /// Registers in-memory asset and asset-transaction stores as lazy defaults (overridable by the host), resolves the
    /// effective stores plus the audit reader, idempotency service and event publisher, then builds the
    /// <see cref="OnlineAssetQueryService"/> and <see cref="OnlineGrantService"/> and registers them back into the shared services.
    /// </remarks>
    /// <param name="context">能力注册上下文 / Capability registration context</param>
    public void Register(CapabilityRegistrationContext context)
    {
        // 两 store：懒默认注册后经共享面取用（族 / 宿主 GameDb Override 自动胜出）。
        context.AddService<IOnlineAssetStore>(static _ => new InMemoryOnlineAssetStore());
        context.AddService<IOnlineAssetTransactionStore>(static _ => new InMemoryOnlineAssetTransactionStore());
        _assetStore = context.GetService<IOnlineAssetStore>();
        _assetTransactionStore = context.GetService<IOnlineAssetTransactionStore>();

        // 跨域硬依赖：审计读面 / 幂等 / 事件出口均为族基建预置，直接解析。
        _audit = context.GetService<IOnlineAuditReader>();
        var idempotency = context.GetService<OnlineIdempotencyService>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        _assetQuery = new OnlineAssetQueryService(_assetStore, _assetTransactionStore);
        _grantService = new OnlineGrantService(_assetStore, _assetTransactionStore, idempotency, eventPublisher);
        context.AddService(_assetQuery);
        context.AddService(_grantService);

        // 跨服补偿队列：惰性默认（无传输 = 仅承载观测/裁决，续投显式报错）；宿主装配真实传输与告警出口后
        // 以 Override 注册同型队列胜出（Override 解析优先与注册先后无关），Admin 观测/裁决始终可用。
        context.AddService<OnlineGrantCompensationQueue>(new OnlineGrantCompensationQueue(null, null));
        _compensationQueue = context.GetService<OnlineGrantCompensationQueue>();
    }

    /// <summary>
    /// 激活阶段无需处理：所有服务已在注册阶段构造完毕，本能力不持有需启动的后台任务或桥接，直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// Nothing to do on activation: every service is built during registration and the capability owns no background work
    /// or bridges to start, so a completed task is returned.
    /// </remarks>
    /// <param name="context">能力激活上下文 / Capability activation context</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / Completed task</returns>
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 停用阶段无需处理：激活时未启动任何资源，因此没有需要释放或撤销的内容，直接返回已完成任务。
    /// </summary>
    /// <remarks>
    /// Nothing to do on deactivation: activation starts no resources, so there is nothing to release or revoke.
    /// </remarks>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>已完成的任务 / Completed task</returns>
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <summary>
    /// 使用注册阶段得到的发放服务、查询服务、审计读面、交易存储与可选跨服补偿队列构造资产管理处理器，
    /// 并登记发放、撤销、钱包账本、资产审计、交易详情与补偿队列观测/裁决等管理动作。
    /// </summary>
    /// <remarks>
    /// Builds <see cref="OnlineAdminAssetHandlers"/> from the grant service, query service, audit reader, transaction store
    /// and optional compensation queue resolved during registration, and registers the grant / revoke / wallet-ledger /
    /// asset-audit / transaction-detail / compensation-queue admin actions.
    /// </remarks>
    /// <param name="registry">管理动作注册表 / Admin action registry</param>
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminAssetHandlers(_grantService, _assetQuery, _audit, _assetTransactionStore, _compensationQueue).Register(registry);
    }
}
