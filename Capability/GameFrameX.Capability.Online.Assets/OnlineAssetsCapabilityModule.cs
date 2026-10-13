// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and other international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the full license text in the LICENSE file located at the root of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activity that endangers national security, disrupts social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of citizens as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本组织不承担任何责任，开发者应自行承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
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
/// 资产能力（钱包 / 库存 / 账本 / 统一发放）的自装配模块（C231 P3，自 Runtime 壳过渡模块无参化迁入）。
/// <para>
/// 维护约束（服务共享协议）：模块无参构造（<see cref="CapabilityAssembly"/> 扫描可发现）；<c>Register</c> 自装配本域——
/// store 经 <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> 探测组合根预置 / <c>UseOnlineAssetStore</c>
/// 替换缝，未命中构造 InMemory 默认；跨域硬依赖（幂等 / 事件出口 / 审计读面）经上下文解析；
/// 对外产物（store 接口 + 查询 / 发放服务）注册回共享面供后序能力与组合根取用。
/// </para>
/// </summary>
/// <remarks>
/// The self-assembling module of the assets capability (wallet / inventory / ledger / unified granting;
/// C231 P3, migrated from the Runtime-shell transitional module and made parameterless). The module has a
/// parameterless constructor (discoverable by <see cref="CapabilityAssembly"/> scanning); <c>Register</c>
/// self-assembles the domain — stores probe the composition-root pre-seed / the <c>UseOnlineAssetStore</c>
/// replacement seam via <see cref="CapabilityRegistrationContext.TryGetService{TService}"/> and fall back to
/// in-memory defaults; cross-domain hard dependencies (idempotency / the event outlet / the audit read face)
/// resolve through the context; outward products (store interfaces plus the query / grant services) are
/// registered back into the shared face for later capabilities and the composition root.
/// </remarks>
public sealed class OnlineAssetsCapabilityModule : ICapabilityModule, IAdminActionContributor
{
    /// <summary>
    /// 资产能力清单（Id 冻结 = 终态包名；action 集合与幂等包裹位逐字迁移自壳过渡模块）。
    /// </summary>
    public static readonly CapabilityManifest ManifestInstance = new(
        "online-assets",
        "1.0.0",
        new[] { "grant_asset", "revoke_asset", "query_wallet_ledger", "query_asset_audit", "query_transaction_detail" });

    /// <summary>
    /// 资产存储（钱包 / 库存 / 账本；Register 期解析或构造）。
    /// </summary>
    private IOnlineAssetStore _assetStore;

    /// <summary>
    /// 资产交易存储（Register 期解析或构造）。
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
    /// 统一审计读面（Register 期解析；组合根预置或聚合域缺席时的空读面）。
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
        if (!context.TryGetService(out _assetStore))
        {
            _assetStore = new InMemoryOnlineAssetStore();
            context.AddService(_assetStore);
        }

        if (!context.TryGetService(out _assetTransactionStore))
        {
            _assetTransactionStore = new InMemoryOnlineAssetTransactionStore();
            context.AddService(_assetTransactionStore);
        }

        _audit = context.TryGetService(out IOnlineAuditReader auditReader) ? auditReader : new EmptyAuditReader();
        var idempotency = context.GetService<OnlineIdempotencyService>();
        var eventPublisher = context.GetService<IOnlineEventPublisher>();
        _assetQuery = new OnlineAssetQueryService(_assetStore, _assetTransactionStore);
        _grantService = new OnlineGrantService(_assetStore, _assetTransactionStore, idempotency, eventPublisher);
        context.AddService(_assetQuery);
        context.AddService(_grantService);
    }

    /// <inheritdoc />
    public void RegisterAdminActions(AdminActionRegistry registry)
    {
        new OnlineAdminAssetHandlers(_grantService, _assetQuery, _audit, _assetTransactionStore).Register(registry);
    }

    /// <summary>
    /// 聚合审计域缺席时的空读面（最小组合根语义：审计能力未安装 = 无审计记录；全能力形态由组合根预置真实读面，
    /// 行为与壳直连逐语句等价）。
    /// </summary>
    private sealed class EmptyAuditReader : IOnlineAuditReader
    {
        /// <inheritdoc />
        public Task<OnlineResult<OnlineAuditPage>> QueryAsync(OnlineScope scope, OnlineAuditQuery query = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(OnlineResult<OnlineAuditPage>.Ok(new OnlineAuditPage(Array.Empty<OnlineAuditRecord>(), null)));
        }
    }
}
