// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相关法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or any other activities prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本项目组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Session;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using Xunit;

namespace GameFrameX.Tests.Core.Session;

/// <summary>
/// 断线路由守卫单测：旧连接的迟到 OnDisconnected 回调 Remove(旧会话) 不得把
/// 仍持有新会话（B）的玩家路由打成离线；仅当玩家已无剩余会话时才允许路由下线。
/// </summary>
public sealed class PlayerSessionManagerStaleDisconnectTests
{
    [Fact]
    public void Remove_StaleDisconnect_WithLiveNewSession_ShouldKeepRouteOnline()
    {
        var manager = PlayerSessionManager.Instance;
        const long playerId = 920001;

        // 旧会话 A 先绑（顶号/重连前），新会话 B 后绑，两者同 playerId
        var sessionA = new PlayerSession("c204-stale-old", new FakeNetworkChannel());
        sessionA.SetPlayerId(playerId);
        manager.Add(sessionA);

        var sessionB = new PlayerSession("c204-stale-new", new FakeNetworkChannel());
        manager.Add(sessionB);
        sessionB.SetPlayerId(playerId);
        manager.SetPlayerRouteOnline(playerId);

        // 旧连接迟到的 OnDisconnected → Remove(A)
        var removed = manager.Remove("c204-stale-old");

        Assert.NotNull(removed);
        Assert.Null(manager.Get("c204-stale-old"));
        // 新会话 B 仍在场且仍是该玩家的当前会话
        var current = manager.GetByPlayerId(playerId);
        Assert.NotNull(current);
        Assert.Same(sessionB, current);
        // 路由不得被旧回调打成离线
        Assert.True(manager.TryGetPlayerRoute(playerId, out var snapshot));
        Assert.True(snapshot.IsOnline);
    }

    [Fact]
    public void Remove_LastSession_ShouldSetRouteOffline()
    {
        var manager = PlayerSessionManager.Instance;
        const long playerId = 920002;

        var session = new PlayerSession("c204-solo", new FakeNetworkChannel());
        session.SetPlayerId(playerId);
        manager.Add(session);

        var removed = manager.Remove("c204-solo");

        Assert.NotNull(removed);
        Assert.Null(manager.Get("c204-solo"));
        Assert.True(manager.TryGetPlayerRoute(playerId, out var snapshot));
        Assert.False(snapshot.IsOnline);
    }

    /// <summary>
    /// 最小网络通道假件：仅实现 SetData / ClearData / Close / IsClosed 的状态语义，其余成员为空实现。
    /// </summary>
    private sealed class FakeNetworkChannel : INetworkChannel
    {
        private readonly Dictionary<string, object> _data = new();
        private bool _closed;

        public ulong SendBytesLength => 0;

        public ulong SendPacketLength => 0;

        public ulong ReceiveBytesLength => 0;

        public ulong ReceivePacketLength => 0;

        public IGameAppSession GameAppSession => null;

        public Task WriteAsync(INetworkMessage msg, int errorCode = 0)
        {
            return Task.CompletedTask;
        }

        public void Close()
        {
            _closed = true;
        }

        /// <summary>
        /// 释放假件资源：委托 <see cref="Close"/>，对齐 BaseNetworkChannel.Dispose 的真实契约（Dispose 即幂等 Close）。
        /// </summary>
        public void Dispose()
        {
            Close();
        }

        public T GetData<T>(string key)
        {
            return _data.TryGetValue(key, out var value) ? (T)value : default;
        }

        public void ClearData()
        {
            _data.Clear();
        }

        public void RemoveData(string key)
        {
            _data.Remove(key);
        }

        public void SetData(string key, object value)
        {
            _data[key] = value;
        }

        public void UpdateReceivePacketBytesLength(ulong bufferLength)
        {
        }

        public void UpdateReceiveMessageTime(long offsetTicks = 0)
        {
        }

        public long GetLastMessageTimeSecond(in DateTime utcTime)
        {
            return 0;
        }

        public bool IsClosed()
        {
            return _closed;
        }
    }
}
