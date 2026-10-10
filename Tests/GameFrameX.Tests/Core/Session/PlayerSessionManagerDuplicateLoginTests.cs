// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Session;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using Xunit;

namespace GameFrameX.Tests.Core.Session;

/// <summary>
/// 顶号通知路径单测（C192）：重复登录时通知器收到旧会话对象本身，且通知先于 Close；
/// 无重复登录时通知器不被调用。断言按对象身份核对。
/// </summary>
public sealed class PlayerSessionManagerDuplicateLoginTests
{
    [Fact]
    public async Task UpdateSession_DuplicateLogin_ShouldNotifyOldSessionBeforeClose()
    {
        var manager = PlayerSessionManager.Instance;
        var oldChannel = new FakeNetworkChannel();
        var oldSession = new PlayerSession("c192-dup-old", oldChannel);
        oldSession.SetPlayerId(910001);
        manager.Add(oldSession);

        var newChannel = new FakeNetworkChannel();
        manager.Add(new PlayerSession("c192-dup-new", newChannel));

        IPlayerSession notified = null;
        var channelOpenAtNotify = false;

        await manager.UpdateSession("c192-dup-new", 910001, "sign-1", old =>
        {
            notified = old;
            channelOpenAtNotify = !oldChannel.IsClosed();
            return Task.CompletedTask;
        });

        // 身份核对：通知器收到的是旧会话对象本身，而非新会话或包装副本
        Assert.Same(oldSession, notified);
        // 通知先于 Close：通知器执行时旧连接尚未关闭
        Assert.True(channelOpenAtNotify);
        // 通知完成后旧连接被关闭、会话被移除
        Assert.True(oldChannel.IsClosed());
        Assert.Null(manager.Get("c192-dup-old"));
        // 新会话接管玩家与签名
        var current = manager.Get("c192-dup-new");
        Assert.NotNull(current);
        Assert.Equal(910001, current.PlayerId);
        Assert.Equal("sign-1", current.Sign);
    }

    [Fact]
    public async Task UpdateSession_NoDuplicateLogin_ShouldNotCallNotifier()
    {
        var manager = PlayerSessionManager.Instance;
        manager.Add(new PlayerSession("c192-solo", new FakeNetworkChannel()));

        var called = false;
        await manager.UpdateSession("c192-solo", 910002, "sign-2", _ =>
        {
            called = true;
            return Task.CompletedTask;
        });

        Assert.False(called);
        var current = manager.Get("c192-solo");
        Assert.NotNull(current);
        Assert.Equal(910002, current.PlayerId);
        Assert.Equal("sign-2", current.Sign);
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
