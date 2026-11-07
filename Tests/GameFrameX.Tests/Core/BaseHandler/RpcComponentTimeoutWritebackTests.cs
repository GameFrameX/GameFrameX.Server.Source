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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using System.Reflection;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.BaseHandler.RPC;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Tests.Core.BaseHandler;

/// <summary>
/// 组件 RPC 处理器超时回写回归：业务核心超时时向客户端回写 TimeOut 响应。
/// </summary>
/// <remarks>
/// 旧实现的组件路径无超时包装：业务挂起时响应永不写回，客户端只能等自身超时。
/// 契约（对齐非组件路径）：TimeoutException 分支记错误日志并回写独立池化实例的
/// OperationErrorCode.TimeOut 响应（含 requestId）；孤儿业务任务持有的原响应经终态延续归还。
/// </remarks>
[Collection(GameAppRuntimeCollection.Name)]
public sealed class RpcComponentTimeoutWritebackTests
{
    private const int RequestUniqueId = 4343;

    [Fact]
    public async Task ComponentRpc_SlowAction_WritesTimeoutResponse()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        var channel = new CapturingNetworkChannel();
        var handler = new SlowActionRpcComponentHandler();

        // 绕过真实 ActorManager 装配：直接注入缓存组件（Tell 同步执行工作单元）与请求/通道
        var neverCompletes = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        handler.Behavior = (request, response, token) => neverCompletes.Task;
        InjectHandlerState(handler, channel);

        try
        {
            await handler.InnerActionAsync(80);

            // 工作单元在组件代理上异步执行（Tell 即返），超时响应在超时到达后异步写回：有限时间内轮询等待
            var waitDeadline = TimeSpan.FromSeconds(5);
            var waitStart = DateTime.UtcNow;
            while (channel.WrittenResponseSnapshots.Count == 0 && DateTime.UtcNow - waitStart < waitDeadline)
            {
                await Task.Delay(10);
            }

            // 旧代码无超时包装：响应永不写回，WrittenResponseSnapshots 为空（A/B 判别点）
            Assert.NotEmpty(channel.WrittenResponseSnapshots);
            var snapshot = Assert.Single(channel.WrittenResponseSnapshots);
            Assert.Equal(OperationErrorCode.TimeOut, snapshot.ErrorCode);
            Assert.Equal(RequestUniqueId, snapshot.UniqueId);
        }
        finally
        {
            neverCompletes.TrySetResult(true);
        }
    }

    /// <summary>
    /// 反射注入处理器执行态（NetworkChannel / RequestMessage / CacheComponent），避免真实 Actor 装配。
    /// </summary>
    private static void InjectHandlerState(SlowActionRpcComponentHandler handler, CapturingNetworkChannel channel)
    {
        var handlerType = typeof(BaseRpcMessageHandler<TimeoutWritebackProbeRequest, TimeoutWritebackProbeResponse>);
        var channelProperty = handlerType.GetProperty("NetworkChannel", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(channelProperty);
        channelProperty.SetValue(handler, channel);

        var requestProperty = handlerType.GetProperty("RequestMessage", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(requestProperty);
        var request = new TimeoutWritebackProbeRequest();
        request.SetUniqueId(RequestUniqueId);
        requestProperty.SetValue(handler, request);

        var cacheComponentProperty = typeof(BaseRpcComponentHandler<TimeoutWritebackProbeRequest, TimeoutWritebackProbeResponse>)
            .GetProperty("CacheComponent", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
        Assert.NotNull(cacheComponentProperty);
        cacheComponentProperty.SetValue(handler, new InlineTellComponentAgent());
    }

    /// <summary>
    /// 受控组件 RPC 处理器：业务行为可注入。
    /// </summary>
    private sealed class SlowActionRpcComponentHandler : BaseRpcComponentHandler<TimeoutWritebackProbeRequest, TimeoutWritebackProbeResponse>
    {
        /// <summary>业务行为（默认立即成功）。</summary>
        public Func<TimeoutWritebackProbeRequest, TimeoutWritebackProbeResponse, CancellationToken, Task> Behavior { get; set; }

        protected override Task<bool> InitActorAsync()
        {
            return Task.FromResult(true);
        }

        protected override Type ComponentAgentType => typeof(object);

        protected override Task ActionAsync(TimeoutWritebackProbeRequest request, TimeoutWritebackProbeResponse response, CancellationToken cancellationToken)
        {
            return Behavior != null ? Behavior(request, response, cancellationToken) : Task.CompletedTask;
        }
    }

    /// <summary>
    /// 同步执行投递工作的组件代理替身（Tell 立即触发工作单元任务，不等待其完成）。
    /// </summary>
    private sealed class InlineTellComponentAgent : IComponentAgent
    {
        public long ActorId => 0;

        public IComponent Owner => null;

        public ushort OwnerType => 0;

        public void SetOwner(IComponent owner)
        {
        }

        public Task<bool> BeforeActivationAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> ActiveAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> AfterActivationAsync()
        {
            return Task.FromResult(true);
        }

        public Task<bool> BeforeInActivationAsync()
        {
            return Task.FromResult(true);
        }

        public Task InactiveAsync()
        {
            return Task.CompletedTask;
        }

        public Task<bool> AfterInActivationAsync()
        {
            return Task.FromResult(true);
        }

        public void Tell(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            work();
        }

        public void Tell(Func<Task> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            _ = work().ContinueWith(completed => { _ = completed.Exception; }, TaskScheduler.Default);
        }

        public Task SendAsync(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            work();
            return Task.CompletedTask;
        }

        public Task<T> SendAsync<T>(Func<T> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(work());
        }

        public Task SendAsync(Func<Task> work, int timeOut = -1, bool checkLock = true, CancellationToken cancellationToken = default)
        {
            return work();
        }

        public Task<T> SendAsync<T>(Func<Task<T>> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            return work();
        }

        public Task<IComponentAgent> GetComponentAgentAsync(Type agentType, bool isNew = true)
        {
            return Task.FromResult<IComponentAgent>(this);
        }

        public Task<T> GetComponentAgentAsync<T>(bool isNew = true) where T : IComponentAgent
        {
            return Task.FromResult<T>(default);
        }
    }

    /// <summary>捕获写通道消息的假网络通道。</summary>
    private sealed class CapturingNetworkChannel : INetworkChannel
    {
        private bool closed;

        public void Dispose()
        {
            closed = true;
        }

        public List<(int UniqueId, int ErrorCode)> WrittenResponseSnapshots { get; } = new();

        public ulong SendBytesLength => 0;

        public ulong SendPacketLength => 0;

        public ulong ReceiveBytesLength => 0;

        public ulong ReceivePacketLength => 0;

        public IGameAppSession GameAppSession => null;

        public Task WriteAsync(INetworkMessage message, int errorCode = 0)
        {
            if (message is IResponseMessage responseMessage)
            {
                // 写通道时刻快照字段值：响应对象随后可能被归还对象池并重置
                WrittenResponseSnapshots.Add((responseMessage.UniqueId, responseMessage.ErrorCode));
            }

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

    /// <summary>测试专用请求消息。</summary>
    private sealed class TimeoutWritebackProbeRequest : IRequestMessage
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

    /// <summary>测试专用响应消息。</summary>
    private sealed class TimeoutWritebackProbeResponse : IResponseMessage
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
}
