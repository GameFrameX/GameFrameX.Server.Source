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
/// 自装配本域——两 store 以懒默认注册后取用（族 / 宿主 Override 自动胜出，模块不做探测回退）；跨域硬依赖
///（幂等 / 事件出口 / 审计读面）经上下文解析（审计读面为族基建预置的 <see cref="IOnlineAuditReader"/>）；
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
        new[] { "grant_asset", "revoke_asset", "query_wallet_ledger", "query_asset_audit", "query_transaction_detail" });

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
    /// 统一审计读面（Register 期解析；族基建预置 OnlineAuditService 的读面）。
    /// </summary>
    private IOnlineAuditReader _audit;

    /// <inheritdoc />
    public CapabilityManifest Manifest
    {
        get { return ManifestInstance; }
    }

    /// <inheritdoc />
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
    }

    /// <inheritdoc />
    public Task ActivateAsync(CapabilityActivationContext context, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task DeactivateAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminAssetHandlers(_grantService, _assetQuery, _audit, _assetTransactionStore).Register(registry);
    }
}
