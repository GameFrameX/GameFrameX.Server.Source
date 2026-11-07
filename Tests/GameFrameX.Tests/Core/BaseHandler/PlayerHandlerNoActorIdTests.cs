// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

using GameFrameX.Core.BaseHandler;
using GameFrameX.Core.BaseHandler.RPC;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Tests.Utility;
using Xunit;

namespace GameFrameX.Tests.Core.BaseHandler;

/// <summary>
/// 未登录玩家消息拒绝回归：通道数据与 ActorId 均缺时不再随机生成 ActorId 回退。
/// </summary>
/// <remarks>
/// 旧实现为每个未绑定玩家身份的请求随机制造一个新 Actor 且默认状态落库成垃圾玩家记录；
/// 契约：与 BaseComponentHandler 既有分支口径统一——记 Fatal + 关闭通道 + 返回 false。
/// 依赖匿名试玩的业务需显式改游客登录流程（当前仓内无此依赖方）。
/// </remarks>
[Collection(GameAppRuntimeCollection.Name)]
public sealed class PlayerHandlerNoActorIdTests
{
    [Fact]
    public async Task PlayerComponentHandler_NoActorId_ClosesChannelAndReturnsFalse()
    {
        var channel = new NoActorIdNetworkChannel();
        var handler = new NoActorIdPlayerComponentHandler();

        var initialized = await handler.InitAsync(new NoActorIdProbeRequest(), channel);

        // 旧实现随机 ActorId 回退：返回 true 且不关通道（A/B 判别点）
        Assert.False(initialized);
        Assert.True(channel.IsClosed());
    }

    [Fact]
    public async Task PlayerRpcComponentHandler_NoActorId_ClosesChannelAndReturnsFalse()
    {
        var channel = new NoActorIdNetworkChannel();
        var handler = new NoActorIdPlayerRpcComponentHandler();

        var initialized = await handler.InitAsync(new NoActorIdProbeRequest(), channel);

        Assert.False(initialized);
        Assert.True(channel.IsClosed());
    }

    /// <summary>
    /// 无玩家身份数据的假网络通道（不携带 ACTOR_ID，模拟未登录连接）。
    /// </summary>
    private sealed class NoActorIdNetworkChannel : INetworkChannel
    {
        private bool closed;

        public void Dispose()
        {
            closed = true;
        }

        public ulong SendBytesLength => 0;

        public ulong SendPacketLength => 0;

        public ulong ReceiveBytesLength => 0;

        public ulong ReceivePacketLength => 0;

        public IGameAppSession GameAppSession => null;

        public Task WriteAsync(INetworkMessage message, int errorCode = 0)
        {
            return Task.CompletedTask;
        }

        public void Close()
        {
            closed = true;
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

        public void SetData(string key, object value)
        {
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
            return closed;
        }
    }

    /// <summary>探针请求消息。</summary>
    private sealed class NoActorIdProbeRequest : IRequestMessage
    {
        public int UniqueId { get; private set; }

        public void SetMessageId(int messageId)
        {
            UniqueId = messageId;
        }

        public void UpdateUniqueId()
        {
            UniqueId = GetHashCode();
        }

        public void SetUniqueId(int uniqueId)
        {
            UniqueId = uniqueId;
        }

        public string ToJsonString()
        {
            return string.Empty;
        }

        public string ToFormatMessageString(long actorId = default)
        {
            return string.Empty;
        }

        public void Clear()
        {
            UniqueId = 0;
        }
    }

    /// <summary>探针响应消息。</summary>
    private sealed class NoActorIdProbeResponse : IResponseMessage
    {
        public int UniqueId { get; private set; }

        public int ErrorCode { get; set; }

        public void SetMessageId(int messageId)
        {
            UniqueId = messageId;
        }

        public void UpdateUniqueId()
        {
            UniqueId = GetHashCode();
        }

        public void SetUniqueId(int uniqueId)
        {
            UniqueId = uniqueId;
        }

        public string ToJsonString()
        {
            return string.Empty;
        }

        public string ToFormatMessageString(long actorId = default)
        {
            return string.Empty;
        }

        public void Clear()
        {
            UniqueId = 0;
            ErrorCode = 0;
        }
    }

    /// <summary>
    /// 玩家组件处理器探针（ComponentAgentType 在失败路径不被读取，占位即可）。
    /// </summary>
    private sealed class NoActorIdPlayerComponentHandler : PlayerComponentHandler<NoActorIdProbeRequest>
    {
        protected override Type ComponentAgentType => typeof(object);

        protected override Task ActionAsync(NoActorIdProbeRequest message)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>玩家 RPC 组件处理器探针。</summary>
    private sealed class NoActorIdPlayerRpcComponentHandler : PlayerRpcComponentHandler<NoActorIdProbeRequest, NoActorIdProbeResponse>
    {
        protected override Type ComponentAgentType => typeof(object);

        protected override Task ActionAsync(NoActorIdProbeRequest request, NoActorIdProbeResponse response, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }
    }
}
