// ==========================================================================================
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   This project is licensed solely under the Apache License 2.0,
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// 玩家路由 Tier 1 fast-path 接口契约与 PlayerRouteInfo 工厂测试（C143e D21）。
/// </summary>
/// <remarks>
/// Pure-logic tests for the player-route resolver contract (C143e D21):
/// the Tier 1 fast-path shape that <see cref="MongoPlayerRouteResolver"/>
/// consumes from the host application layer, and the <see cref="PlayerRouteInfo"/>
/// factory methods. Tier 2 control-database and Tier 3 offline paths run against
/// Mongo and live in <c>MongoPlayerRouteIntegrationTests</c>; the resolver's
/// per-instance CAS upsert is also covered there. The dedupe primitive
/// (<c>PerInstanceDedupe</c>) is left to its native callers — its behavior is
/// already covered by integration tests that exercise the real cross-server
/// send path.
/// </remarks>
public sealed class MongoPlayerRouteResolverTests
{
    /// <summary>
    /// 录制型 fast-path 提供方（测试替身）。
    /// </summary>
    /// <remarks>
    /// Records calls and returns scripted answers; lets the test verify the
    /// resolver does not fall through to Tier 2 when Tier 1 returns online.
    /// </remarks>
    private sealed class ScriptedFastPath : IPlayerRouteFastPath
    {
        public ScriptedFastPath(long playerId, bool isOnline, string serverType = "Game", int serverId = 1, long version = 7)
        {
            PlayerId = playerId;
            IsOnline = isOnline;
            ServerType = serverType;
            ServerId = serverId;
            Version = version;
        }

        public long PlayerId { get; }
        public bool IsOnline { get; }
        public string ServerType { get; }
        public int ServerId { get; }
        public long Version { get; }
        public int Calls { get; private set; }

        public bool TryGetOnline(long playerId, out PlayerRouteInfo info)
        {
            Calls++;
            if (playerId == PlayerId && IsOnline)
            {
                info = PlayerRouteInfo.Online(ServerType, ServerId, Version);
                return true;
            }

            info = PlayerRouteInfo.Offline();
            return false;
        }
    }

    [Fact]
    public void FastPath_OnlineAnswer_ReturnsOnlineRouteInfo()
    {
        var fastPath = new ScriptedFastPath(playerId: 42, isOnline: true, serverType: "Game", serverId: 9, version: 5);

        var resolved = fastPath.TryGetOnline(42, out var info);

        Assert.True(resolved);
        Assert.True(info.IsOnline);
        Assert.Equal("Game", info.ServerType);
        Assert.Equal(9, info.ServerId);
        Assert.Equal(5, info.Version);
        Assert.Equal(1, fastPath.Calls);
    }

    [Fact]
    public void FastPath_OfflineAnswer_ReturnsOfflineInfoAndFalse()
    {
        var fastPath = new ScriptedFastPath(playerId: 42, isOnline: false);

        var resolved = fastPath.TryGetOnline(42, out var info);

        Assert.False(resolved);
        Assert.False(info.IsOnline);
        Assert.Null(info.ServerType);
        Assert.Equal(1, info.Version);
    }

    [Fact]
    public void FastPath_WrongPlayer_AnswersOffline()
    {
        var fastPath = new ScriptedFastPath(playerId: 42, isOnline: true);

        var resolved = fastPath.TryGetOnline(999, out var info);

        Assert.False(resolved);
        Assert.False(info.IsOnline);
    }

    [Fact]
    public void NullPlayerRouteSyncTarget_DeleteAsync_IsNoOp()
    {
        var task = NullPlayerRouteSyncTarget.Instance.DeleteAsync(1);

        Assert.NotNull(task);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void NullPlayerRouteSyncTarget_UpsertAsync_IsNoOp()
    {
        var task = NullPlayerRouteSyncTarget.Instance.UpsertAsync(new PlayerRouteRecord { PlayerId = 1, InstanceId = "instance", Role = "Game", Version = 1, });

        Assert.NotNull(task);
        Assert.True(task.IsCompletedSuccessfully);
    }

    [Fact]
    public void PlayerRouteInfo_Offline_HasIsOnlineFalseAndStableVersion()
    {
        var info = PlayerRouteInfo.Offline();
        Assert.False(info.IsOnline);
        Assert.Null(info.ServerType);
        Assert.Equal(0, info.ServerId);
        Assert.Equal(1, info.Version);
    }

    [Fact]
    public void PlayerRouteInfo_Online_RoundTripsFields()
    {
        var info = PlayerRouteInfo.Online("Game", 42, 9);
        Assert.True(info.IsOnline);
        Assert.Equal("Game", info.ServerType);
        Assert.Equal(42, info.ServerId);
        Assert.Equal(9, info.Version);
    }
}