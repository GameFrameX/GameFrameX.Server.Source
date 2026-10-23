// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Identity;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Foundation.Identity;
using GameFrameX.DataBase;

/// <summary>
/// 身份域 GameDb 持久化适配（一级 store，分级混合持久化的身份侧落地）。
/// <para>
/// 数据生命周期语义：行物理长存——适配层零删除调用（账号注销/合并等状态迁移为字段覆盖写，
/// 与 InMemory 全量驻留语义同构）；标识序列（账号/玩家）由 <c>NextSequenceAsync</c> 数据库序列接管
/// （原子单调、重启不复用——替代 InMemory 进程内自增）；身份/设备唯一性由物化复合键单列唯一索引
/// + <c>TryAddUniqueAsync</c> 承接。
/// </para>
/// <para>
/// 单写者假设（继承 store 契约）：唯一插入冲突兜底 = 读回既有行按行主键覆盖写——并发窗口内恰一胜者、
/// 落后者转为全量覆盖，净效果与单写者串行等价；不引入分布式协调（多进程部署由装配层按归属路由）。
/// 消费方零改动：仅实现 <see cref="IOnlineIdentityStore"/> 全方法，接口面不变。
/// </para>
/// </summary>
public sealed class GameDbOnlineIdentityStore : IOnlineIdentityStore
{
    /// <summary>
    /// 身份行的行 Id 分配序列名（无自然主键实体；键控 = 状态类型名 + 序列名）。
    /// </summary>
    private const string IdentityRowIdSequence = "row-id";

    /// <summary>
    /// 设备行的行 Id 分配序列名。
    /// </summary>
    private const string DeviceRowIdSequence = "row-id";

    /// <summary>
    /// 游戏账号标识序列名（<see cref="NextAccountIdAsync"/> 的数据库序列键）。
    /// </summary>
    private const string AccountIdSequence = "account-id";

    /// <summary>
    /// 玩家标识序列名（<see cref="NextPlayerIdAsync"/> 的数据库序列键）。
    /// </summary>
    private const string PlayerIdSequence = "player-id";

    /// <summary>按唯一键 (TenantId, AppId, Kind, Identifier) 查找身份。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份实体；不存在返回 null。</returns>
    public async Task<OnlineIdentity> FindIdentityAsync(long tenantId, long appId, OnlineIdentityKind kind, string identifier, CancellationToken cancellationToken = default)
    {
        var state = await FindIdentityStateAsync(BuildIdentityKey(tenantId, appId, kind, identifier), cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>写入或覆盖身份（按唯一键全量写；既有行保行主键覆盖，新行序列分配 + 唯一插入）。</summary>
    /// <param name="identity">身份实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddOrUpdateIdentityAsync(OnlineIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var uniqueKey = BuildIdentityKey(identity.TenantId, identity.AppId, identity.Kind, identity.Identifier);
        var existing = await FindIdentityStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            await GameDb.AddOrUpdateAsync(ToState(identity, uniqueKey, existing.Id), cancellationToken).ConfigureAwait(false);
            return;
        }

        await AddUniqueRowAsync(
            uniqueKey,
            rowId => ToState(identity, uniqueKey, rowId),
            static key => state => state.UniqueKey == key,
            IdentityRowIdSequence,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>列出账号下全部身份（含已解绑，调用方按 UnboundAtTime 过滤）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份实体列表。</returns>
    public async Task<IReadOnlyList<OnlineIdentity>> ListIdentitiesAsync(long gameAccountId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlineIdentityState>(state => state.GameAccountId == gameAccountId, cancellationToken).ConfigureAwait(false);
        return states.Select(ToEntity).ToList();
    }

    /// <summary>按主键查找账号。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号实体；不存在返回 null。</returns>
    public async Task<OnlineGameAccount> FindAccountAsync(long gameAccountId, CancellationToken cancellationToken = default)
    {
        var state = await GameDb.FindAsync<OnlineGameAccountState>(gameAccountId, null, false, cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>写入或覆盖账号（行 Id = 实体自然主键，按主键全量覆盖）。</summary>
    /// <param name="account">账号实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddOrUpdateAccountAsync(OnlineGameAccount account, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(account);

        await GameDb.AddOrUpdateAsync(ToState(account), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按主键查找玩家档案。</summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案；不存在返回 null。</returns>
    public async Task<OnlinePlayerProfile> FindPlayerAsync(long playerId, CancellationToken cancellationToken = default)
    {
        var state = await GameDb.FindAsync<OnlinePlayerProfileState>(playerId, null, false, cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>写入或覆盖玩家档案（行 Id = 实体自然主键，按主键全量覆盖）。</summary>
    /// <param name="player">玩家档案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddOrUpdatePlayerAsync(OnlinePlayerProfile player, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(player);

        await GameDb.AddOrUpdateAsync(ToState(player), cancellationToken).ConfigureAwait(false);
    }

    /// <summary>列出账号在指定 App/Server 归属下的玩家档案（按创建时间升序；并列按 Id 定序）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案列表。</returns>
    public async Task<IReadOnlyList<OnlinePlayerProfile>> ListPlayersAsync(long gameAccountId, long appId, long serverId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlinePlayerProfileState>(state => state.GameAccountId == gameAccountId && state.AppId == appId && state.ServerId == serverId, cancellationToken).ConfigureAwait(false);
        return states.OrderBy(state => state.CreatedAtTime).ThenBy(state => state.Id).Select(ToEntity).ToList();
    }

    /// <summary>列出账号下全部玩家档案（不限 App/Server 归属；按创建时间升序，并列按 Id 定序）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案列表。</returns>
    public async Task<IReadOnlyList<OnlinePlayerProfile>> ListAllPlayersAsync(long gameAccountId, CancellationToken cancellationToken = default)
    {
        var states = await GameDb.FindListAsync<OnlinePlayerProfileState>(state => state.GameAccountId == gameAccountId, cancellationToken).ConfigureAwait(false);
        return states.OrderBy(state => state.CreatedAtTime).ThenBy(state => state.Id).Select(ToEntity).ToList();
    }

    /// <summary>按唯一键 (GameAccountId, DeviceIdentifier) 查找设备。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="deviceIdentifier">设备唯一标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备实体；不存在返回 null。</returns>
    public async Task<OnlinePlayerDevice> FindDeviceAsync(long gameAccountId, string deviceIdentifier, CancellationToken cancellationToken = default)
    {
        var state = await FindDeviceStateAsync(BuildDeviceKey(gameAccountId, deviceIdentifier), cancellationToken).ConfigureAwait(false);
        return state == null ? null : ToEntity(state);
    }

    /// <summary>写入或覆盖设备（按唯一键全量写；既有行保行主键覆盖，新行序列分配 + 唯一插入）。</summary>
    /// <param name="device">设备实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task AddOrUpdateDeviceAsync(OnlinePlayerDevice device, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(device);

        var uniqueKey = BuildDeviceKey(device.GameAccountId, device.DeviceIdentifier);
        var existing = await FindDeviceStateAsync(uniqueKey, cancellationToken).ConfigureAwait(false);
        if (existing != null)
        {
            await GameDb.AddOrUpdateAsync(ToState(device, uniqueKey, existing.Id), cancellationToken).ConfigureAwait(false);
            return;
        }

        await AddUniqueRowAsync(
            uniqueKey,
            rowId => ToState(device, uniqueKey, rowId),
            static key => state => state.UniqueKey == key,
            DeviceRowIdSequence,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>生成新的游戏账号标识（数据库序列接管：原子单调、重启不复用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号标识。</returns>
    public Task<long> NextAccountIdAsync(CancellationToken cancellationToken = default)
    {
        return GameDb.NextSequenceAsync<OnlineGameAccountState>(AccountIdSequence, cancellationToken);
    }

    /// <summary>生成新的玩家标识（数据库序列接管：原子单调、重启不复用）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家标识。</returns>
    public Task<long> NextPlayerIdAsync(CancellationToken cancellationToken = default)
    {
        return GameDb.NextSequenceAsync<OnlinePlayerProfileState>(PlayerIdSequence, cancellationToken);
    }

    /// <summary>
    /// 唯一行写入（唯一插入 + 冲突读回覆盖兜底；单写者假设下的防御路径）。
    /// </summary>
    /// <typeparam name="TState">行类型。</typeparam>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="buildState">以分配的行 Id 构造行。</param>
    /// <param name="buildUniqueFilter">以复合键构造唯一性判定表达式。</param>
    /// <param name="rowIdSequence">行 Id 分配序列名。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private static async Task AddUniqueRowAsync<TState>(string uniqueKey, Func<long, TState> buildState, Func<string, Expression<Func<TState, bool>>> buildUniqueFilter, string rowIdSequence, CancellationToken cancellationToken) where TState : BaseCacheState, new()
    {
        var rowId = await GameDb.NextSequenceAsync<TState>(rowIdSequence, cancellationToken).ConfigureAwait(false);
        var state = buildState(rowId);
        if (await GameDb.TryAddUniqueAsync(state, buildUniqueFilter(uniqueKey), cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        // 唯一插入冲突（并发窗口的落者）：读回胜者行，按行主键全量覆盖——AddOrUpdate 幂等语义不丢失。
        var winner = await GameDb.FindAsync<TState>(buildUniqueFilter(uniqueKey), isCreateIfNotExists: false, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (winner == null)
        {
            // 唯一插入失败但读不到既有行 = 数据库状态自相矛盾（唯一索引可见性异常），fail fast 而非静默丢写。
            throw new InvalidOperationException("唯一插入冲突后未能读回既有行：" + uniqueKey);
        }

        state.Id = winner.Id;
        await GameDb.AddOrUpdateAsync(state, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>按物化复合键查找身份行。</summary>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份行；不存在返回 null。</returns>
    private static Task<OnlineIdentityState> FindIdentityStateAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlineIdentityState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>按物化复合键查找设备行。</summary>
    /// <param name="uniqueKey">物化复合键。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备行；不存在返回 null。</returns>
    private static Task<OnlinePlayerDeviceState> FindDeviceStateAsync(string uniqueKey, CancellationToken cancellationToken)
    {
        return GameDb.FindAsync<OnlinePlayerDeviceState>(state => state.UniqueKey == uniqueKey, isCreateIfNotExists: false, cancellationToken: cancellationToken);
    }

    /// <summary>构造身份唯一键（与 InMemory BuildIdentityKey 同构）。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildIdentityKey(long tenantId, long appId, OnlineIdentityKind kind, string identifier)
    {
        return tenantId + ":" + appId + ":" + (int)kind + ":" + identifier;
    }

    /// <summary>构造设备唯一键（与 InMemory BuildDeviceKey 同构）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="deviceIdentifier">设备唯一标识串。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildDeviceKey(long gameAccountId, string deviceIdentifier)
    {
        return gameAccountId + ":" + deviceIdentifier;
    }

    /// <summary>身份实体 → 行（指定行 Id）。</summary>
    private static OnlineIdentityState ToState(OnlineIdentity identity, string uniqueKey, long rowId)
    {
        return new OnlineIdentityState
        {
            Id = rowId,
            UniqueKey = uniqueKey,
            EntityId = identity.Id,
            TenantId = identity.TenantId,
            AppId = identity.AppId,
            Kind = (int)identity.Kind,
            Identifier = identity.Identifier,
            GameAccountId = identity.GameAccountId,
            CreatedAtTime = identity.CreatedAtTime,
            UpdatedAtTime = identity.UpdatedAtTime,
            UnboundAtTime = identity.UnboundAtTime,
        };
    }

    /// <summary>身份行 → 实体。</summary>
    private static OnlineIdentity ToEntity(OnlineIdentityState state)
    {
        return new OnlineIdentity
        {
            Id = state.EntityId,
            TenantId = state.TenantId,
            AppId = state.AppId,
            Kind = (OnlineIdentityKind)state.Kind,
            Identifier = state.Identifier,
            GameAccountId = state.GameAccountId,
            CreatedAtTime = state.CreatedAtTime,
            UpdatedAtTime = state.UpdatedAtTime,
            UnboundAtTime = state.UnboundAtTime,
        };
    }

    /// <summary>账号实体 → 行（行 Id = 实体主键）。</summary>
    private static OnlineGameAccountState ToState(OnlineGameAccount account)
    {
        return new OnlineGameAccountState
        {
            Id = account.Id,
            TenantId = account.TenantId,
            AppId = account.AppId,
            Status = (int)account.Status,
            CreatedAtTime = account.CreatedAtTime,
            UpdatedAtTime = account.UpdatedAtTime,
            DeactivatedAtTime = account.DeactivatedAtTime,
            DataRetentionUntilTime = account.DataRetentionUntilTime,
            MergedIntoAccountId = account.MergedIntoAccountId,
        };
    }

    /// <summary>账号行 → 实体。</summary>
    private static OnlineGameAccount ToEntity(OnlineGameAccountState state)
    {
        return new OnlineGameAccount
        {
            Id = state.Id,
            TenantId = state.TenantId,
            AppId = state.AppId,
            Status = (OnlineGameAccountStatus)state.Status,
            CreatedAtTime = state.CreatedAtTime,
            UpdatedAtTime = state.UpdatedAtTime,
            DeactivatedAtTime = state.DeactivatedAtTime,
            DataRetentionUntilTime = state.DataRetentionUntilTime,
            MergedIntoAccountId = state.MergedIntoAccountId,
        };
    }

    /// <summary>玩家档案实体 → 行（行 Id = 实体主键）。</summary>
    private static OnlinePlayerProfileState ToState(OnlinePlayerProfile player)
    {
        return new OnlinePlayerProfileState
        {
            Id = player.Id,
            GameAccountId = player.GameAccountId,
            TenantId = player.TenantId,
            AppId = player.AppId,
            ServerId = player.ServerId,
            Name = player.Name,
            CreatedAtTime = player.CreatedAtTime,
            UpdatedAtTime = player.UpdatedAtTime,
        };
    }

    /// <summary>玩家档案行 → 实体。</summary>
    private static OnlinePlayerProfile ToEntity(OnlinePlayerProfileState state)
    {
        return new OnlinePlayerProfile
        {
            Id = state.Id,
            GameAccountId = state.GameAccountId,
            TenantId = state.TenantId,
            AppId = state.AppId,
            ServerId = state.ServerId,
            Name = state.Name,
            CreatedAtTime = state.CreatedAtTime,
            UpdatedAtTime = state.UpdatedAtTime,
        };
    }

    /// <summary>设备实体 → 行（指定行 Id）。</summary>
    private static OnlinePlayerDeviceState ToState(OnlinePlayerDevice device, string uniqueKey, long rowId)
    {
        return new OnlinePlayerDeviceState
        {
            Id = rowId,
            UniqueKey = uniqueKey,
            EntityId = device.Id,
            GameAccountId = device.GameAccountId,
            DeviceIdentifier = device.DeviceIdentifier,
            Platform = device.Platform,
            BoundAtTime = device.BoundAtTime,
            LastActiveAtTime = device.LastActiveAtTime,
            RevokedAtTime = device.RevokedAtTime,
        };
    }

    /// <summary>设备行 → 实体。</summary>
    private static OnlinePlayerDevice ToEntity(OnlinePlayerDeviceState state)
    {
        return new OnlinePlayerDevice
        {
            Id = state.EntityId,
            GameAccountId = state.GameAccountId,
            DeviceIdentifier = state.DeviceIdentifier,
            Platform = state.Platform,
            BoundAtTime = state.BoundAtTime,
            LastActiveAtTime = state.LastActiveAtTime,
            RevokedAtTime = state.RevokedAtTime,
        };
    }
}
