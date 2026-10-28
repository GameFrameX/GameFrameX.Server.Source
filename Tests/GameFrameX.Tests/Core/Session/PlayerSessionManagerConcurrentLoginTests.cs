// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Session;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using Xunit;

namespace GameFrameX.Tests.Core.Session;

/// <summary>
/// 顶号串行化单测（C306 / GFX-1018）：同 playerId 并发登录的关键段互斥——
/// TaskCompletionSource 把第二登录阻塞在通知器内（关键段中段）确定性放大读-判-改竞态窗口，
/// 第三登录并发进入必须等第二登录整体完成后才顶号，收敛为单一幸存绑定；
/// 同会话重登（同 sessionId）不触发顶号通知、不关闭自身通道，幂等重绑。
/// </summary>
public sealed class PlayerSessionManagerConcurrentLoginTests
{
    [Fact]
    public async Task UpdateSession_ConcurrentSamePlayerLogins_ShouldSerializeToSingleSurvivor()
    {
        var manager = PlayerSessionManager.Instance;
        const long playerId = 930001;

        // 底座：第一会话先完成登录绑定，作为后续并发段落的"旧会话"
        var firstChannel = new FakeNetworkChannel();
        var firstSession = new PlayerSession("c306-first", firstChannel);
        manager.Add(firstSession);
        await manager.UpdateSessionAsync("c306-first", playerId, "sign-0", _ => Task.CompletedTask);

        var secondChannel = new FakeNetworkChannel();
        var secondSession = new PlayerSession("c306-second", secondChannel);
        manager.Add(secondSession);

        var thirdChannel = new FakeNetworkChannel();
        manager.Add(new PlayerSession("c306-third", thirdChannel));

        // 第二登录进入关键段后在通知器内阻塞：notifierEntered 置位时它已持有该玩家的串行锁
        var notifierEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseGate = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondLogin = manager.UpdateSessionAsync("c306-second", playerId, "sign-2", async old =>
        {
            // 第二登录先进入，看到的旧会话必须是底座第一会话
            Assert.Same(firstSession, old);
            notifierEntered.SetResult(true);
            await releaseGate.Task;
        });
        var synchronization = await Task.WhenAny(notifierEntered.Task, secondLogin).WaitAsync(TimeSpan.FromSeconds(10));
        if (synchronization == secondLogin)
        {
            await secondLogin;
        }
        await notifierEntered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // 第三登录并发进入：串行化下必须阻塞在锁上（通知器不可能先于放行触发）；
        // 无锁旧代码会立即以第一会话为旧会话完成顶号（探测到即继续，最终断言会暴露双活绑定）
        IPlayerSession thirdNotifiedOld = null;
        var thirdNotifierEntered = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thirdLogin = manager.UpdateSessionAsync("c306-third", playerId, "sign-3", old =>
        {
            thirdNotifiedOld = old;
            thirdNotifierEntered.TrySetResult(true);
            return Task.CompletedTask;
        });
        await Task.WhenAny(thirdNotifierEntered.Task, Task.Delay(TimeSpan.FromSeconds(1)));

        // 放行第二登录，等待两段登录全部完成
        releaseGate.SetResult(true);
        await secondLogin;
        await thirdLogin;

        // 串行化收敛：同 playerId 恰一个绑定存活（无锁旧代码此处为 2——第二/第三双活绑定）
        var bound = manager.GetList(m => m.PlayerId == playerId);
        Assert.Single(bound);
        var survivor = manager.GetByPlayerId(playerId);
        Assert.NotNull(survivor);
        Assert.Equal("c306-third", survivor.SessionId);
        Assert.Equal(playerId, survivor.PlayerId);
        Assert.Equal("sign-3", survivor.Sign);
        Assert.False(thirdChannel.IsClosed());

        // 第三登录顶掉的旧会话 = 第二会话（而非底座第一会话），证明它等待了第二登录整体完成
        Assert.Same(secondSession, thirdNotifiedOld);
        Assert.True(secondChannel.IsClosed());
        Assert.Null(manager.Get("c306-second"));

        // 底座第一会话被第二登录顶掉：通知后关闭并移除
        Assert.True(firstChannel.IsClosed());
        Assert.Null(manager.Get("c306-first"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateSession_DisconnectedReplacement_ShouldPreserveExistingSession(bool disconnectDuringNotification)
    {
        var manager = PlayerSessionManager.Instance;
        var playerId = disconnectDuringNotification ? 930004L : 930003L;
        var existingIdentifier = "existing-" + playerId;
        var replacementIdentifier = "replacement-" + playerId;
        var existingChannel = new FakeNetworkChannel();
        var replacementChannel = new FakeNetworkChannel();
        manager.Add(new PlayerSession(existingIdentifier, existingChannel));
        await manager.UpdateSessionAsync(existingIdentifier, playerId, "original", session => Task.CompletedTask);
        manager.Add(new PlayerSession(replacementIdentifier, replacementChannel));
        if (!disconnectDuringNotification)
        {
            manager.Remove(replacementIdentifier);
        }
        try
        {
            await manager.UpdateSessionAsync(replacementIdentifier, playerId, "replacement", session =>
            {
                manager.Remove(replacementIdentifier);
                return Task.CompletedTask;
            }).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.False(existingChannel.IsClosed());
            Assert.Equal(existingIdentifier, manager.GetByPlayerId(playerId).SessionId);
            Assert.Equal("original", manager.Get(existingIdentifier).Sign);
        }
        finally
        {
            manager.Remove(existingIdentifier);
            manager.Remove(replacementIdentifier);
        }
    }

    [Fact]
    public async Task UpdateSession_SameSessionRebind_ShouldNotKickSelf()
    {
        var manager = PlayerSessionManager.Instance;
        const long playerId = 930002;

        var channel = new FakeNetworkChannel();
        var session = new PlayerSession("c306-rebind", channel);
        manager.Add(session);
        await manager.UpdateSessionAsync("c306-rebind", playerId, "sign-a", _ => Task.CompletedTask);

        var notified = false;
        await manager.UpdateSessionAsync("c306-rebind", playerId, "sign-b", _ =>
        {
            notified = true;
            return Task.CompletedTask;
        });

        // 同会话重登不触发顶号通知（无守卫旧代码会把自己判为旧会话：自通知 + 自关闭 + 自移除）
        Assert.False(notified);
        var current = manager.Get("c306-rebind");
        Assert.NotNull(current);
        Assert.Same(session, current);
        Assert.Equal(playerId, current.PlayerId);
        Assert.Equal("sign-b", current.Sign);
        Assert.False(channel.IsClosed());
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
