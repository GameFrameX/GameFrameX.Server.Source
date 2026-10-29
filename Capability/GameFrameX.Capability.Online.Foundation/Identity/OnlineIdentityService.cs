//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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

namespace GameFrameX.Capability.Online.Foundation.Identity;

using System.Collections.Concurrent;
using System.Security.Cryptography;
using GameFrameX.Capability.Online.Abstractions.Audit;
using GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 身份域服务（登录解析、绑定/换绑/解绑、合并与注销的显式操作）。
/// <para>
/// 维护约束（红线）：登录成功后由服务端生成账号与玩家材料，客户端不得自行拼接身份关系；
/// 已解绑身份归原账号所有——跨账号改绑必须携带原账号签发的一次性授权票据
///（见 <see cref="IssueIdentityRebindAuthorizationAsync"/> 与 <see cref="BindIdentityAsync"/>）；
/// 身份写入走条件写面（唯一插入 / CAS），授权判定与写入针对同一行版本；
/// 设备标识只用于识别与换绑判定（旧设备换绑后按策略拒绝登录）；
/// 注销与数据保留为显式操作——保留期内账号不可登录、数据不可物理删除；
/// 多端登录策略不在本服务执行（会话域按签发时策略裁决，见 <c>OnlineSessionManager</c>）。
/// </para>
/// </summary>
public sealed class OnlineIdentityService
{
    /// <summary>改绑授权票据默认有效期（毫秒；5 分钟——短时效一次性凭证）。</summary>
    public const long DefaultRebindAuthorizationLifetimeMilliseconds = 300000;

    /// <summary>身份域存储。</summary>
    private readonly IOnlineIdentityStore _store;

    /// <summary>统一审计链路（跨账号改绑留痕的唯一写入面）。</summary>
    private readonly OnlineAuditService _audit;

    /// <summary>改绑授权票据有效期（毫秒）。</summary>
    private readonly long _rebindAuthorizationLifetimeMilliseconds;

    /// <summary>
    /// 改绑授权票据登记表（键 = 票据秘密串；易失凭证：进程内承载、消费式单次，重启即失效）。
    /// </summary>
    private readonly ConcurrentDictionary<string, OnlineIdentityRebindAuthorization> _rebindAuthorizations = new ConcurrentDictionary<string, OnlineIdentityRebindAuthorization>();

    /// <summary>
    /// 初始化 <see cref="OnlineIdentityService"/>。
    /// </summary>
    /// <param name="store">身份域存储。</param>
    /// <param name="auditService">统一审计链路（跨账号改绑留痕；族基建预置实例）。</param>
    /// <param name="rebindAuthorizationLifetimeMilliseconds">改绑授权票据有效期（毫秒；默认 <see cref="DefaultRebindAuthorizationLifetimeMilliseconds"/>，0 = 签发即过期，负数抛出）。</param>
    public OnlineIdentityService(IOnlineIdentityStore store, OnlineAuditService auditService, long rebindAuthorizationLifetimeMilliseconds = DefaultRebindAuthorizationLifetimeMilliseconds)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _audit = auditService ?? throw new ArgumentNullException(nameof(auditService));
        if (rebindAuthorizationLifetimeMilliseconds < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(rebindAuthorizationLifetimeMilliseconds), "改绑授权票据有效期不能为负数。");
        }

        _rebindAuthorizationLifetimeMilliseconds = rebindAuthorizationLifetimeMilliseconds;
    }

    /// <summary>
    /// 解析一次登录：命中身份或自动注册，选定 App/Server 归属玩家，校验设备状态。
    /// </summary>
    /// <param name="request">登录解析请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>登录解析结果（身份/账号/玩家三件套）；失败返回段位化错误码。</returns>
    public async Task<OnlineResult<OnlineLoginResolution>> ResolveLoginAsync(OnlineLoginRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var tenantId = request.TenantId;
        var appId = request.AppId;
        var serverId = request.ServerId;
        var kind = request.Kind;
        var identifier = request.Identifier;
        var playerName = request.PlayerName;
        var deviceIdentifier = request.DeviceIdentifier;
        var devicePlatform = request.DevicePlatform;
        if (!Enum.IsDefined(typeof(OnlineIdentityKind), kind) || string.IsNullOrEmpty(identifier))
        {
            return OnlineResult<OnlineLoginResolution>.Fail(OnlineErrorCode.ParameterInvalid, "身份类型未定义或标识为空。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var identity = await _store.FindIdentityAsync(tenantId, appId, kind, identifier, cancellationToken);
        OnlineGameAccount account;
        OnlinePlayerProfile player;

        if (identity == null)
        {
            // 新身份自动注册：账号 + 身份 + 首个玩家一次性建立（服务端生成，客户端不得拼接）。
            (identity, account, player) = await RegisterIdentityAsync(tenantId, appId, serverId, kind, identifier, playerName, now, cancellationToken);
        }
        else if (identity.UnboundAtTime > 0)
        {
            // 已解绑身份复活路径：不新建账号（否则原账号资产成孤儿），解析原账号后重绑恢复登录。
            var accountResult = await ResolveAccountAsync(identity, cancellationToken);
            if (!accountResult.IsSuccess)
            {
                return OnlineResult<OnlineLoginResolution>.Fail(accountResult.Code, accountResult.Message);
            }

            account = accountResult.Data;
            identity = await ReviveUnboundIdentityAsync(identity, account, now, cancellationToken);
            player = await ResolvePlayerAsync(account, tenantId, appId, serverId, playerName, now, cancellationToken);
        }
        else
        {
            var accountResult = await ResolveAccountAsync(identity, cancellationToken);
            if (!accountResult.IsSuccess)
            {
                return OnlineResult<OnlineLoginResolution>.Fail(accountResult.Code, accountResult.Message);
            }

            account = accountResult.Data;
            player = await ResolvePlayerAsync(account, tenantId, appId, serverId, playerName, now, cancellationToken);
        }

        // 设备识别与换绑策略校验：已失效设备（换绑后旧设备）按策略拒绝登录。
        if (!string.IsNullOrEmpty(deviceIdentifier))
        {
            var deviceResult = await TouchDeviceAsync(account, deviceIdentifier, devicePlatform, now, cancellationToken);
            if (!deviceResult.IsSuccess)
            {
                return OnlineResult<OnlineLoginResolution>.Fail(deviceResult.Code, deviceResult.Message);
            }
        }

        return OnlineResult<OnlineLoginResolution>.Ok(new OnlineLoginResolution(identity, account, player));
    }

    /// <summary>
    /// 新身份自动注册（红线）：账号、身份与首个玩家由服务端一次性生成并落库，客户端不得拼接身份关系。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="playerName">玩家显示名（可空，缺省按玩家标识生成）。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份/账号/玩家三元组（均已写入存储）。</returns>
    private async Task<(OnlineIdentity Identity, OnlineGameAccount Account, OnlinePlayerProfile Player)> RegisterIdentityAsync(long tenantId, long appId, long serverId, OnlineIdentityKind kind, string identifier, string playerName, long now, CancellationToken cancellationToken)
    {
        var accountId = await _store.NextAccountIdAsync(cancellationToken);
        var account = new OnlineGameAccount
        {
            Id = accountId,
            TenantId = tenantId,
            AppId = appId,
            Status = OnlineGameAccountStatus.Active,
            CreatedAtTime = now,
            UpdatedAtTime = now,
        };
        var playerId = await _store.NextPlayerIdAsync(cancellationToken);
        var player = new OnlinePlayerProfile
        {
            Id = playerId,
            GameAccountId = accountId,
            TenantId = tenantId,
            AppId = appId,
            ServerId = serverId,
            Name = string.IsNullOrEmpty(playerName) ? "Player-" + playerId : playerName,
            CreatedAtTime = now,
            UpdatedAtTime = now,
        };
        var identity = new OnlineIdentity
        {
            Id = "ident-" + Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            AppId = appId,
            Kind = kind,
            Identifier = identifier,
            GameAccountId = accountId,
            CreatedAtTime = now,
            UpdatedAtTime = now,
        };
        await _store.AddOrUpdateAccountAsync(account, cancellationToken);
        await _store.AddOrUpdatePlayerAsync(player, cancellationToken);
        await _store.AddOrUpdateIdentityAsync(identity, cancellationToken);
        return (identity, account, player);
    }

    /// <summary>
    /// 解析既有身份指向的可登录账号：合并账号单跳跟随（目标必须活跃），注销账号保留期内拒绝登录。
    /// </summary>
    /// <param name="identity">已命中的绑定身份。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>可登录的活跃账号；失败返回段位化错误码。</returns>
    private async Task<OnlineResult<OnlineGameAccount>> ResolveAccountAsync(OnlineIdentity identity, CancellationToken cancellationToken)
    {
        var account = await _store.FindAccountAsync(identity.GameAccountId, cancellationToken);
        if (account == null)
        {
            return OnlineResult<OnlineGameAccount>.Fail(OnlineErrorCode.StateNotReady, "身份指向的账号不存在（数据不一致）。");
        }

        // 合并账号跟随：身份与玩家归属以合并目标账号为准（单跳，目标必须活跃）。
        if (account.Status == OnlineGameAccountStatus.Merged)
        {
            if (account.MergedIntoAccountId <= 0)
            {
                return OnlineResult<OnlineGameAccount>.Fail(OnlineErrorCode.StateNotReady, "合并账号缺少目标账号指向。");
            }

            var targetAccount = await _store.FindAccountAsync(account.MergedIntoAccountId, cancellationToken);
            if (targetAccount == null || targetAccount.Status != OnlineGameAccountStatus.Active)
            {
                return OnlineResult<OnlineGameAccount>.Fail(OnlineErrorCode.StateNotReady, "合并目标账号不可用。");
            }

            account = targetAccount;
        }

        if (account.Status == OnlineGameAccountStatus.Deactivated)
        {
            return OnlineResult<OnlineGameAccount>.Fail(OnlineErrorCode.StateOperationForbidden, "账号已注销，保留期内不可登录。");
        }

        if (account.Status != OnlineGameAccountStatus.Active)
        {
            return OnlineResult<OnlineGameAccount>.Fail(OnlineErrorCode.StateNotReady, "账号状态不可登录。");
        }

        return OnlineResult<OnlineGameAccount>.Ok(account);
    }

    /// <summary>
    /// 已解绑身份复活（红线）：不新建账号，重绑回解析出的活跃原账号，防止原账号资产成孤儿。
    /// <para>归属证明 = 该身份凭据已通过本次登录链校验；原账号已合并时跟随单跳指向合并目标。</para>
    /// </summary>
    /// <param name="identity">已解绑的身份。</param>
    /// <param name="account">身份原指向且经状态校验的可登录账号。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>重绑后的身份（已写入存储）。</returns>
    private async Task<OnlineIdentity> ReviveUnboundIdentityAsync(OnlineIdentity identity, OnlineGameAccount account, long now, CancellationToken cancellationToken)
    {
        identity.GameAccountId = account.Id;
        identity.UnboundAtTime = 0;
        identity.UpdatedAtTime = now;
        await _store.AddOrUpdateIdentityAsync(identity, cancellationToken);
        return identity;
    }

    /// <summary>
    /// 解析账号在指定 App/Server 下的归属玩家：命中既有玩家则复用，否则新建并落库。
    /// </summary>
    /// <param name="account">已解析的可登录账号。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="playerName">新建玩家显示名（可空，缺省按玩家标识生成）。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>归属玩家实例。</returns>
    private async Task<OnlinePlayerProfile> ResolvePlayerAsync(OnlineGameAccount account, long tenantId, long appId, long serverId, string playerName, long now, CancellationToken cancellationToken)
    {
        var players = await _store.ListPlayersAsync(account.Id, appId, serverId, cancellationToken);
        if (players.Count > 0)
        {
            return players[0];
        }

        var playerId = await _store.NextPlayerIdAsync(cancellationToken);
        var player = new OnlinePlayerProfile
        {
            Id = playerId,
            GameAccountId = account.Id,
            TenantId = tenantId,
            AppId = appId,
            ServerId = serverId,
            Name = string.IsNullOrEmpty(playerName) ? "Player-" + playerId : playerName,
            CreatedAtTime = now,
            UpdatedAtTime = now,
        };
        await _store.AddOrUpdatePlayerAsync(player, cancellationToken);
        return player;
    }

    /// <summary>
    /// 设备识别与换绑策略校验：新设备绑定，既有设备刷新活跃时间，已失效设备（换绑后旧设备）按策略拒绝登录。
    /// </summary>
    /// <param name="account">当前登录账号。</param>
    /// <param name="deviceIdentifier">登录设备标识。</param>
    /// <param name="devicePlatform">登录设备平台描述（可空）。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true；设备已失效返回段位化错误码。</returns>
    private async Task<OnlineResult<bool>> TouchDeviceAsync(OnlineGameAccount account, string deviceIdentifier, string devicePlatform, long now, CancellationToken cancellationToken)
    {
        var device = await _store.FindDeviceAsync(account.Id, deviceIdentifier, cancellationToken);
        if (device == null)
        {
            device = new OnlinePlayerDevice
            {
                Id = "device-" + Guid.NewGuid().ToString("N"),
                GameAccountId = account.Id,
                DeviceIdentifier = deviceIdentifier,
                Platform = devicePlatform ?? string.Empty,
                BoundAtTime = now,
                LastActiveAtTime = now,
            };
        }
        else if (device.RevokedAtTime > 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "设备已失效（换绑后旧设备按策略拒绝登录）。");
        }
        else
        {
            device.LastActiveAtTime = now;
        }

        await _store.AddOrUpdateDeviceAsync(device, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 为账号绑定外源身份（显式操作）。三分支：全新身份唯一插入；绑定中身份同账号幂等放行、他账号拒绝；
    /// 已解绑身份归原账号——原账号重绑自己放行，跨账号改绑必须携带原账号签发的一次性授权票据。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="gameAccountId">目标游戏账号。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="rebindAuthorizationTicket">跨账号改绑授权票据秘密串（仅已解绑身份且原账号非本账号时必需）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true；失败返回段位化错误码（并发冲突 <see cref="OnlineErrorCode.VersionConflict"/>）。</returns>
    public async Task<OnlineResult<bool>> BindIdentityAsync(long tenantId, long appId, long gameAccountId, OnlineIdentityKind kind, string identifier, string rebindAuthorizationTicket = null, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(OnlineIdentityKind), kind) || string.IsNullOrEmpty(identifier))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "身份类型未定义或标识为空。");
        }

        var account = await _store.FindAccountAsync(gameAccountId, cancellationToken);
        if (account == null || account.Status != OnlineGameAccountStatus.Active)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "目标账号不存在或不可绑定。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var existing = await _store.FindIdentityAsync(tenantId, appId, kind, identifier, cancellationToken);
        if (existing == null)
        {
            // 全新身份：上游完成第三方凭据校验后直接绑定；唯一插入保证并发恰一胜者（败者零写入）。
            var identity = new OnlineIdentity
            {
                Id = "ident-" + Guid.NewGuid().ToString("N"),
                TenantId = tenantId,
                AppId = appId,
                Kind = kind,
                Identifier = identifier,
                GameAccountId = gameAccountId,
                CreatedAtTime = now,
                UpdatedAtTime = now,
            };
            if (!await _store.TryAddIdentityAsync(identity, cancellationToken))
            {
                return OnlineResult<bool>.Fail(OnlineErrorCode.VersionConflict, "身份已被并发绑定。");
            }

            return OnlineResult<bool>.Ok(true);
        }

        if (existing.UnboundAtTime == 0)
        {
            if (existing.GameAccountId == gameAccountId)
            {
                return OnlineResult<bool>.Ok(true);
            }

            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "身份已绑定其他账号。");
        }

        if (existing.GameAccountId == gameAccountId)
        {
            // 原账号重绑自己解绑过的身份：归属证明 = 调用主体即原账号；CAS 写入防并发双绑。
            if (!await TryRebindIdentityCoreAsync(existing, gameAccountId, now, cancellationToken))
            {
                return OnlineResult<bool>.Fail(OnlineErrorCode.VersionConflict, "身份已被并发变更，请重试。");
            }

            return OnlineResult<bool>.Ok(true);
        }

        return await BindUnboundIdentityCrossAccountAsync(existing, gameAccountId, rebindAuthorizationTicket, now, cancellationToken);
    }

    /// <summary>
    /// 已解绑身份的跨账号改绑尾段：原账号须仍为 Active，且携带其签发的一次性授权票据；CAS 成功后写改绑审计。
    /// </summary>
    private async Task<OnlineResult<bool>> BindUnboundIdentityCrossAccountAsync(OnlineIdentity existing, long gameAccountId, string rebindAuthorizationTicket, long now, CancellationToken cancellationToken)
    {
        // 跨账号改绑已解绑身份：原账号须仍为 Active（注销/封禁/合并等走客服或管理后台，不开放自助改绑）。
        var originalAccount = await _store.FindAccountAsync(existing.GameAccountId, cancellationToken);
        if (originalAccount == null || originalAccount.Status != OnlineGameAccountStatus.Active)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "原账号不可用，跨账号改绑须走客服或管理后台流程。");
        }

        var authorizationResult = TryConsumeRebindAuthorization(rebindAuthorizationTicket, existing.TenantId, existing.AppId, existing.Kind, existing.Identifier, existing.GameAccountId, DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        if (!authorizationResult.IsSuccess)
        {
            return OnlineResult<bool>.Fail(authorizationResult.Code, authorizationResult.Message);
        }

        // 票据已消费（消费式单次）：此后 CAS 失败票据同样作废，需原账号重新签发。
        if (!await TryRebindIdentityCoreAsync(existing, gameAccountId, now, cancellationToken))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.VersionConflict, "身份已被并发变更，请原账号重新签发授权后重试。");
        }

        await WriteRebindAuditAsync(existing, gameAccountId, authorizationResult.Data, now, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 签发已解绑身份跨账号改绑的一次性授权票据（由原账号已登录会话调用；短有效期、消费式单次）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="issuerGameAccountId">签发账号（原账号；身份须归属该账号，解绑前后均可）。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>授权票据（<see cref="OnlineIdentityRebindAuthorization.Code"/> 为秘密串，交由新账号持有方使用）；失败返回段位化错误码。</returns>
    public async Task<OnlineResult<OnlineIdentityRebindAuthorization>> IssueIdentityRebindAuthorizationAsync(long tenantId, long appId, long issuerGameAccountId, OnlineIdentityKind kind, string identifier, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(typeof(OnlineIdentityKind), kind) || string.IsNullOrEmpty(identifier))
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.ParameterInvalid, "身份类型未定义或标识为空。");
        }

        var account = await _store.FindAccountAsync(issuerGameAccountId, cancellationToken);
        if (account == null)
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.ResourceNotFound, "签发账号不存在。");
        }

        if (account.Status != OnlineGameAccountStatus.Active)
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.StateOperationForbidden, "仅活跃账号可签发身份改绑授权。");
        }

        var identity = await _store.FindIdentityAsync(tenantId, appId, kind, identifier, cancellationToken);
        if (identity == null)
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.ResourceNotFound, "身份不存在。");
        }

        if (identity.GameAccountId != issuerGameAccountId)
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.StateOperationForbidden, "仅身份归属账号可签发改绑授权。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        SweepExpiredRebindAuthorizations(now);
        var authorization = new OnlineIdentityRebindAuthorization
        {
            Code = "rebind-" + Convert.ToHexString(RandomNumberGenerator.GetBytes(32)),
            TicketId = "rebind-auth-" + Guid.NewGuid().ToString("N"),
            TenantId = tenantId,
            AppId = appId,
            Kind = kind,
            Identifier = identifier,
            IssuerGameAccountId = issuerGameAccountId,
            ExpiresAtTime = now + _rebindAuthorizationLifetimeMilliseconds,
        };
        _rebindAuthorizations[authorization.Code] = authorization;
        return OnlineResult<OnlineIdentityRebindAuthorization>.Ok(authorization);
    }

    /// <summary>
    /// 改绑核心写入（CAS）：沿用原身份记录 Id 与创建时间，指向新账号并清解绑时间戳，期望版本 = 读取时版本。
    /// </summary>
    /// <param name="existing">读取时的身份快照。</param>
    /// <param name="gameAccountId">新归属账号。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>CAS 成功返回 true；版本被并发推进返回 false（零写入）。</returns>
    private async Task<bool> TryRebindIdentityCoreAsync(OnlineIdentity existing, long gameAccountId, long now, CancellationToken cancellationToken)
    {
        var rebound = new OnlineIdentity
        {
            Id = existing.Id,
            TenantId = existing.TenantId,
            AppId = existing.AppId,
            Kind = existing.Kind,
            Identifier = existing.Identifier,
            GameAccountId = gameAccountId,
            CreatedAtTime = existing.CreatedAtTime,
            UpdatedAtTime = now,
            UnboundAtTime = 0,
        };
        return await _store.TryUpdateIdentityIfRowVersionAsync(rebound, existing.RowVersion, cancellationToken);
    }

    /// <summary>
    /// 消费式校验改绑授权票据（验证即移除，无论后续改绑是否成功）：作用域四元组与签发账号必须与被改绑身份一致。
    /// </summary>
    /// <param name="ticketCode">票据秘密串。</param>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="issuerGameAccountId">原账号标识。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <returns>校验通过返回票据实体；失败返回段位化错误码。</returns>
    private OnlineResult<OnlineIdentityRebindAuthorization> TryConsumeRebindAuthorization(string ticketCode, long tenantId, long appId, OnlineIdentityKind kind, string identifier, long issuerGameAccountId, long now)
    {
        if (string.IsNullOrEmpty(ticketCode))
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.StateOperationForbidden, "跨账号改绑已解绑身份须携带原账号签发的授权票据。");
        }

        if (!_rebindAuthorizations.TryRemove(ticketCode, out var authorization))
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.StateOperationForbidden, "授权票据无效或已使用。");
        }

        if (authorization.TenantId != tenantId || authorization.AppId != appId || authorization.Kind != kind || authorization.Identifier != identifier || authorization.IssuerGameAccountId != issuerGameAccountId)
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.StateOperationForbidden, "授权票据与被改绑身份不匹配。");
        }

        if (authorization.ExpiresAtTime <= now)
        {
            return OnlineResult<OnlineIdentityRebindAuthorization>.Fail(OnlineErrorCode.StateOperationForbidden, "授权票据已过期。");
        }

        return OnlineResult<OnlineIdentityRebindAuthorization>.Ok(authorization);
    }

    /// <summary>
    /// 清扫过期未消费的改绑授权票据（签发低频操作，顺带全量清扫即可；过期票据不长期占用登记表）。
    /// </summary>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    private void SweepExpiredRebindAuthorizations(long now)
    {
        foreach (var pair in _rebindAuthorizations)
        {
            if (pair.Value.ExpiresAtTime <= now)
            {
                _rebindAuthorizations.TryRemove(pair.Key, out _);
            }
        }
    }

    /// <summary>
    /// 跨账号改绑成功后写统一审计（域 Operation）：身份种类、原账号、新账号、授权票据标识（秘密串不落审计）。
    /// </summary>
    /// <param name="original">改绑前的身份快照（<see cref="OnlineIdentity.GameAccountId"/> 仍指向原账号）。</param>
    /// <param name="newGameAccountId">新归属账号。</param>
    /// <param name="authorization">已消费的授权票据。</param>
    /// <param name="now">当前 Unix 毫秒时间。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task WriteRebindAuditAsync(OnlineIdentity original, long newGameAccountId, OnlineIdentityRebindAuthorization authorization, long now, CancellationToken cancellationToken)
    {
        var entry = new OnlineAuditEntry
        {
            Domain = OnlineAuditDomain.Operation,
            EventId = "identity-rebind-" + Guid.NewGuid().ToString("N"),
            EventType = "online.identity.rebound",
            OccurredTime = now,
            TenantId = original.TenantId,
            AppId = original.AppId,
            ServerId = 0,
            PlayerId = 0,
            OperatorId = "game-account-" + original.GameAccountId,
            Reason = "已解绑身份跨账号改绑（原账号一次性授权票据核准）",
            Source = "online-foundation-identity",
            CorrelationId = authorization.TicketId,
            PayloadAuditFields = new Dictionary<string, string>
            {
                { "identityKind", original.Kind.ToString() },
                { "originalGameAccountId", original.GameAccountId.ToString() },
                { "newGameAccountId", newGameAccountId.ToString() },
                { "authorizationTicketId", authorization.TicketId },
            },
        };

        // 改绑已生效，审计为留痕面：接入字段确定性齐全（不触发宁拒毋缺拒绝），接入结果不回灌改绑返回值。
        await _audit.IngestAsync(entry, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// 解绑外源身份（显式操作；账号必须保留至少一个绑定身份，否则拒绝）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true；失败返回段位化错误码。</returns>
    public async Task<OnlineResult<bool>> UnbindIdentityAsync(long tenantId, long appId, OnlineIdentityKind kind, string identifier, CancellationToken cancellationToken = default)
    {
        var identity = await _store.FindIdentityAsync(tenantId, appId, kind, identifier, cancellationToken);
        if (identity == null || identity.UnboundAtTime > 0)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "身份不存在或已解绑。");
        }

        var identities = await _store.ListIdentitiesAsync(identity.GameAccountId, cancellationToken);
        var boundCount = 0;
        foreach (var item in identities)
        {
            if (item.UnboundAtTime == 0)
            {
                boundCount++;
            }
        }

        if (boundCount <= 1)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "账号必须保留至少一个绑定身份。");
        }

        identity.UnboundAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        identity.UpdatedAtTime = identity.UnboundAtTime;
        await _store.AddOrUpdateIdentityAsync(identity, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 换绑设备（显式操作）：旧设备立即失效，新设备绑定并可用；旧设备后续登录按策略拒绝。
    /// </summary>
    /// <param name="gameAccountId">目标游戏账号。</param>
    /// <param name="oldDeviceIdentifier">旧设备唯一标识串。</param>
    /// <param name="newDeviceIdentifier">新设备唯一标识串。</param>
    /// <param name="newDevicePlatform">新设备平台描述（可空）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true；失败返回段位化错误码。</returns>
    public async Task<OnlineResult<bool>> RebindDeviceAsync(long gameAccountId, string oldDeviceIdentifier, string newDeviceIdentifier, string newDevicePlatform = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(oldDeviceIdentifier) || string.IsNullOrEmpty(newDeviceIdentifier))
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "换绑设备标识不能为空。");
        }

        var oldDevice = await _store.FindDeviceAsync(gameAccountId, oldDeviceIdentifier, cancellationToken);
        if (oldDevice == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "旧设备不存在。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        oldDevice.RevokedAtTime = now;
        await _store.AddOrUpdateDeviceAsync(oldDevice, cancellationToken);

        var newDevice = await _store.FindDeviceAsync(gameAccountId, newDeviceIdentifier, cancellationToken);
        if (newDevice == null)
        {
            newDevice = new OnlinePlayerDevice
            {
                Id = "device-" + Guid.NewGuid().ToString("N"),
                GameAccountId = gameAccountId,
                DeviceIdentifier = newDeviceIdentifier,
                Platform = newDevicePlatform ?? string.Empty,
                BoundAtTime = now,
                LastActiveAtTime = now,
            };
        }
        else
        {
            newDevice.RevokedAtTime = 0;
            newDevice.LastActiveAtTime = now;
        }

        await _store.AddOrUpdateDeviceAsync(newDevice, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 注销账号（显式操作）：保留期内不可登录、数据不可物理删除。
    /// </summary>
    /// <param name="gameAccountId">目标游戏账号。</param>
    /// <param name="dataRetentionUntilTime">数据保留截止时间（Unix 毫秒；0 = 按默认保留策略）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true；失败返回段位化错误码。</returns>
    public async Task<OnlineResult<bool>> DeactivateAccountAsync(long gameAccountId, long dataRetentionUntilTime, CancellationToken cancellationToken = default)
    {
        var account = await _store.FindAccountAsync(gameAccountId, cancellationToken);
        if (account == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "账号不存在。");
        }

        if (account.Status != OnlineGameAccountStatus.Active)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "仅活跃账号可注销。");
        }

        account.Status = OnlineGameAccountStatus.Deactivated;
        account.DeactivatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        account.DataRetentionUntilTime = dataRetentionUntilTime;
        account.UpdatedAtTime = account.DeactivatedAtTime;
        await _store.AddOrUpdateAccountAsync(account, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }

    /// <summary>
    /// 合并账号（显式操作）：源账号身份与玩家迁至目标账号，源账号转 Merged 只读保留。
    /// </summary>
    /// <param name="sourceAccountId">源账号。</param>
    /// <param name="targetAccountId">目标账号。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>成功返回 true；失败返回段位化错误码。</returns>
    public async Task<OnlineResult<bool>> MergeAccountsAsync(long sourceAccountId, long targetAccountId, CancellationToken cancellationToken = default)
    {
        if (sourceAccountId == targetAccountId)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ParameterInvalid, "合并源与目标不能相同。");
        }

        var source = await _store.FindAccountAsync(sourceAccountId, cancellationToken);
        var target = await _store.FindAccountAsync(targetAccountId, cancellationToken);
        if (source == null || target == null)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.ResourceNotFound, "合并源或目标账号不存在。");
        }

        if (source.Status != OnlineGameAccountStatus.Active || target.Status != OnlineGameAccountStatus.Active)
        {
            return OnlineResult<bool>.Fail(OnlineErrorCode.StateOperationForbidden, "仅活跃账号可参与合并。");
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var identities = await _store.ListIdentitiesAsync(sourceAccountId, cancellationToken);
        foreach (var identity in identities)
        {
            if (identity.UnboundAtTime == 0)
            {
                identity.GameAccountId = targetAccountId;
                identity.UpdatedAtTime = now;
                await _store.AddOrUpdateIdentityAsync(identity, cancellationToken);
            }
        }

        var players = await _store.ListAllPlayersAsync(sourceAccountId, cancellationToken);
        foreach (var player in players)
        {
            player.GameAccountId = targetAccountId;
            player.UpdatedAtTime = now;
            await _store.AddOrUpdatePlayerAsync(player, cancellationToken);
        }

        source.Status = OnlineGameAccountStatus.Merged;
        source.MergedIntoAccountId = targetAccountId;
        source.UpdatedAtTime = now;
        await _store.AddOrUpdateAccountAsync(source, cancellationToken);
        return OnlineResult<bool>.Ok(true);
    }
}