//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 身份域内存默认存储（vault:C3 S2.2：单进程/测试环境默认实现；生产装配以持久化实现替换）。
/// <para>
/// 维护约束（天花板）：全量数据驻留内存，仅适用单进程开发/测试与阶段一联调；
/// 全局锁保护——身份写入为低频操作，锁竞争天花板可接受，持久化实现按索引并发替代；
/// 标识序列为进程内自增，跨进程部署必须由持久化实现（如数据库序列/雪花）接管。
/// </para>
/// </summary>
public sealed class InMemoryOnlineIdentityStore : IOnlineIdentityStore
{
    /// <summary>全局读写锁（身份域低频写入，粗粒度锁足够）。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>身份表：键 = (TenantId, AppId, Kind, Identifier)。</summary>
    private readonly Dictionary<string, OnlineIdentity> _identities = new Dictionary<string, OnlineIdentity>();

    /// <summary>账号表：键 = GameAccountId。</summary>
    private readonly Dictionary<long, OnlineGameAccount> _accounts = new Dictionary<long, OnlineGameAccount>();

    /// <summary>玩家表：键 = PlayerId。</summary>
    private readonly Dictionary<long, OnlinePlayerProfile> _players = new Dictionary<long, OnlinePlayerProfile>();

    /// <summary>设备表：键 = (GameAccountId, DeviceIdentifier)。</summary>
    private readonly Dictionary<string, OnlinePlayerDevice> _devices = new Dictionary<string, OnlinePlayerDevice>();

    /// <summary>账号标识序列。</summary>
    private long _accountIdSequence;

    /// <summary>玩家标识序列。</summary>
    private long _playerIdSequence;

    /// <summary>按唯一键查找身份。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份实体；不存在返回 null。</returns>
    public Task<OnlineIdentity> FindIdentityAsync(long tenantId, long appId, OnlineIdentityKind kind, string identifier, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _identities.TryGetValue(BuildIdentityKey(tenantId, appId, kind, identifier), out var identity);
            return Task.FromResult(identity);
        }
    }

    /// <summary>写入或覆盖身份（按 Id 全量写）。</summary>
    /// <param name="identity">身份实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AddOrUpdateIdentityAsync(OnlineIdentity identity, CancellationToken cancellationToken = default)
    {
        if (identity == null)
        {
            throw new ArgumentNullException(nameof(identity));
        }

        lock (_syncRoot)
        {
            _identities[BuildIdentityKey(identity.TenantId, identity.AppId, identity.Kind, identity.Identifier)] = identity;
        }

        return Task.CompletedTask;
    }

    /// <summary>列出账号下全部身份（含已解绑）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份实体列表。</returns>
    public Task<IReadOnlyList<OnlineIdentity>> ListIdentitiesAsync(long gameAccountId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlineIdentity>();
            foreach (var identity in _identities.Values)
            {
                if (identity.GameAccountId == gameAccountId)
                {
                    result.Add(identity);
                }
            }

            return Task.FromResult<IReadOnlyList<OnlineIdentity>>(result);
        }
    }

    /// <summary>按主键查找账号。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号实体；不存在返回 null。</returns>
    public Task<OnlineGameAccount> FindAccountAsync(long gameAccountId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _accounts.TryGetValue(gameAccountId, out var account);
            return Task.FromResult(account);
        }
    }

    /// <summary>写入或覆盖账号（按 Id 全量写）。</summary>
    /// <param name="account">账号实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AddOrUpdateAccountAsync(OnlineGameAccount account, CancellationToken cancellationToken = default)
    {
        if (account == null)
        {
            throw new ArgumentNullException(nameof(account));
        }

        lock (_syncRoot)
        {
            _accounts[account.Id] = account;
        }

        return Task.CompletedTask;
    }

    /// <summary>按主键查找玩家档案。</summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案；不存在返回 null。</returns>
    public Task<OnlinePlayerProfile> FindPlayerAsync(long playerId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _players.TryGetValue(playerId, out var player);
            return Task.FromResult(player);
        }
    }

    /// <summary>写入或覆盖玩家档案（按 Id 全量写）。</summary>
    /// <param name="player">玩家档案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AddOrUpdatePlayerAsync(OnlinePlayerProfile player, CancellationToken cancellationToken = default)
    {
        if (player == null)
        {
            throw new ArgumentNullException(nameof(player));
        }

        lock (_syncRoot)
        {
            _players[player.Id] = player;
        }

        return Task.CompletedTask;
    }

    /// <summary>列出账号在指定 App/Server 归属下的玩家档案（按创建时间升序）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案列表。</returns>
    public Task<IReadOnlyList<OnlinePlayerProfile>> ListPlayersAsync(long gameAccountId, long appId, long serverId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlinePlayerProfile>();
            foreach (var player in _players.Values)
            {
                if (player.GameAccountId == gameAccountId && player.AppId == appId && player.ServerId == serverId)
                {
                    result.Add(player);
                }
            }

            result.Sort((left, right) => left.CreatedAtTime.CompareTo(right.CreatedAtTime));
            return Task.FromResult<IReadOnlyList<OnlinePlayerProfile>>(result);
        }
    }

    /// <summary>列出账号下全部玩家档案（不限 App/Server 归属；合并迁移用，按创建时间升序）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案列表。</returns>
    public Task<IReadOnlyList<OnlinePlayerProfile>> ListAllPlayersAsync(long gameAccountId, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            var result = new List<OnlinePlayerProfile>();
            foreach (var player in _players.Values)
            {
                if (player.GameAccountId == gameAccountId)
                {
                    result.Add(player);
                }
            }

            result.Sort((left, right) => left.CreatedAtTime.CompareTo(right.CreatedAtTime));
            return Task.FromResult<IReadOnlyList<OnlinePlayerProfile>>(result);
        }
    }

    /// <summary>按唯一键 (GameAccountId, DeviceIdentifier) 查找设备。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="deviceIdentifier">设备唯一标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备实体；不存在返回 null。</returns>
    public Task<OnlinePlayerDevice> FindDeviceAsync(long gameAccountId, string deviceIdentifier, CancellationToken cancellationToken = default)
    {
        lock (_syncRoot)
        {
            _devices.TryGetValue(BuildDeviceKey(gameAccountId, deviceIdentifier), out var device);
            return Task.FromResult(device);
        }
    }

    /// <summary>写入或覆盖设备（按 Id 全量写）。</summary>
    /// <param name="device">设备实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task AddOrUpdateDeviceAsync(OnlinePlayerDevice device, CancellationToken cancellationToken = default)
    {
        if (device == null)
        {
            throw new ArgumentNullException(nameof(device));
        }

        lock (_syncRoot)
        {
            _devices[BuildDeviceKey(device.GameAccountId, device.DeviceIdentifier)] = device;
        }

        return Task.CompletedTask;
    }

    /// <summary>生成新的游戏账号标识（进程内自增）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号标识。</returns>
    public Task<long> NextAccountIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _accountIdSequence));
    }

    /// <summary>生成新的玩家标识（进程内自增）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家标识。</returns>
    public Task<long> NextPlayerIdAsync(CancellationToken cancellationToken = default)
    {
        return Task.FromResult(Interlocked.Increment(ref _playerIdSequence));
    }

    /// <summary>构造身份唯一键。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildIdentityKey(long tenantId, long appId, OnlineIdentityKind kind, string identifier)
    {
        return tenantId + ":" + appId + ":" + (int)kind + ":" + identifier;
    }

    /// <summary>构造设备唯一键。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="deviceIdentifier">设备唯一标识串。</param>
    /// <returns>复合键字符串。</returns>
    private static string BuildDeviceKey(long gameAccountId, string deviceIdentifier)
    {
        return gameAccountId + ":" + deviceIdentifier;
    }
}
