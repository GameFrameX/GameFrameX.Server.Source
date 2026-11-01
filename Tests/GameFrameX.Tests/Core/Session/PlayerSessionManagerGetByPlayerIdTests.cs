// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
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
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Session;
using GameFrameX.Network.Abstractions;
using Xunit;

using GameFrameX.SuperSocket.Server.Abstractions.Session;

namespace GameFrameX.Tests.Core.Session;

/// <summary>
/// GetByPlayerId 现状语义锁定单测（GFX-1178 / CR-L05）：
/// 删除死代码 HasActor 分支后，返回值语义必须是「按 playerId 查会话、不按 Actor 在内存过滤」——
/// UpdateSessionAsync 的同会话幂等重绑依赖该行为。
/// </summary>
public sealed class PlayerSessionManagerGetByPlayerIdTests
{
    private const long PlayerId = 931001;

    [Fact]
    public async Task GetByPlayerId_SessionExistsWithoutActor_ReturnsSession()
    {
        var manager = PlayerSessionManager.Instance;
        var session = new PlayerSession("c438-getbyplayerid", new FakeNetworkChannel());
        manager.Add(session);
        await manager.UpdateSessionAsync("c438-getbyplayerid", PlayerId, "sign-c438", _ => Task.CompletedTask);

        // 该玩家没有任何 Actor 在内存：仍必须取回同一会话（现状语义）
        var found = manager.GetByPlayerId(PlayerId);

        Assert.Same(session, found);
        manager.Remove("c438-getbyplayerid");
    }

    [Fact]
    public void GetByPlayerId_NoSession_ReturnsNull()
    {
        var manager = PlayerSessionManager.Instance;

        Assert.Null(manager.GetByPlayerId(long.MinValue + 438));
    }

    /// <summary>
    /// 最小通道替身（沿 PlayerSessionManagerConcurrentLoginTests 同款形态）。
    /// </summary>
    private sealed class FakeNetworkChannel : INetworkChannel
    {
        public void Dispose()
        {
        }

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
        }

        public T GetData<T>(string key)
        {
            return default;
        }

        public void ClearData()
        {
        }

        public void RemoveData(string key)
        {
        }

        public long GetLastMessageTimeSecond(in DateTime utcTime)
        {
            return 0;
        }

        public bool IsClosed()
        {
            return false;
        }

        public void SetData(string key, object value)
        {
        }

        public void UpdateReceivePacketBytesLength(ulong bufferLength)
        {
        }

        public void UpdateReceiveMessageTime(long offsetTicks = 0)
        {
        }
    }
}
