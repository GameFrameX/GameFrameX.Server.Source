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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 身份域存储接口（vault:C3 S2.2：Identity/GameAccount/Player/Device 数据关系的持久化契约）。
/// <para>
/// 维护约束：唯一性索引——Identity(TenantId, AppId, Kind, Identifier)、Device(GameAccountId, DeviceIdentifier)；
/// 查询键——Player 按 (GameAccountId, AppId, ServerId) 归属检索；
/// 生产装配以 Mongo 等持久化实现本接口（交接点 X4 复用不 fork），本仓交付线程安全的内存默认实现；
/// 实现方必须保证 AddOrUpdate 系列为幂等全量写（按主键覆盖）。
/// </para>
/// </summary>
public interface IOnlineIdentityStore
{
    /// <summary>按唯一键 (TenantId, AppId, Kind, Identifier) 查找身份。</summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="kind">身份类型。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份实体；不存在返回 null。</returns>
    Task<OnlineIdentity> FindIdentityAsync(long tenantId, long appId, OnlineIdentityKind kind, string identifier, CancellationToken cancellationToken = default);

    /// <summary>写入或覆盖身份（按 Id 全量写）。</summary>
    /// <param name="identity">身份实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddOrUpdateIdentityAsync(OnlineIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>列出账号下全部身份（含已解绑，调用方按 UnboundAtTime 过滤）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>身份实体列表。</returns>
    Task<IReadOnlyList<OnlineIdentity>> ListIdentitiesAsync(long gameAccountId, CancellationToken cancellationToken = default);

    /// <summary>按主键查找账号。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号实体；不存在返回 null。</returns>
    Task<OnlineGameAccount> FindAccountAsync(long gameAccountId, CancellationToken cancellationToken = default);

    /// <summary>写入或覆盖账号（按 Id 全量写）。</summary>
    /// <param name="account">账号实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddOrUpdateAccountAsync(OnlineGameAccount account, CancellationToken cancellationToken = default);

    /// <summary>按主键查找玩家档案。</summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案；不存在返回 null。</returns>
    Task<OnlinePlayerProfile> FindPlayerAsync(long playerId, CancellationToken cancellationToken = default);

    /// <summary>写入或覆盖玩家档案（按 Id 全量写）。</summary>
    /// <param name="player">玩家档案。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddOrUpdatePlayerAsync(OnlinePlayerProfile player, CancellationToken cancellationToken = default);

    /// <summary>列出账号在指定 App/Server 归属下的玩家档案。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案列表（按创建时间升序）。</returns>
    Task<IReadOnlyList<OnlinePlayerProfile>> ListPlayersAsync(long gameAccountId, long appId, long serverId, CancellationToken cancellationToken = default);

    /// <summary>列出账号下全部玩家档案（不限 App/Server 归属；合并迁移用）。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家档案列表（按创建时间升序）。</returns>
    Task<IReadOnlyList<OnlinePlayerProfile>> ListAllPlayersAsync(long gameAccountId, CancellationToken cancellationToken = default);

    /// <summary>按唯一键 (GameAccountId, DeviceIdentifier) 查找设备。</summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="deviceIdentifier">设备唯一标识串。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>设备实体；不存在返回 null。</returns>
    Task<OnlinePlayerDevice> FindDeviceAsync(long gameAccountId, string deviceIdentifier, CancellationToken cancellationToken = default);

    /// <summary>写入或覆盖设备（按 Id 全量写）。</summary>
    /// <param name="device">设备实体。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    Task AddOrUpdateDeviceAsync(OnlinePlayerDevice device, CancellationToken cancellationToken = default);

    /// <summary>生成新的游戏账号标识（服务端唯一）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>账号标识。</returns>
    Task<long> NextAccountIdAsync(CancellationToken cancellationToken = default);

    /// <summary>生成新的玩家标识（服务端唯一）。</summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>玩家标识。</returns>
    Task<long> NextPlayerIdAsync(CancellationToken cancellationToken = default);
}
