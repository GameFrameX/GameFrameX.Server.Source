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

using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Abstractions;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Components;
using GameFrameX.Core.Events;
using GameFrameX.Core.Hotfix.Agent;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;

namespace GameFrameX.Hotfix.Tests.Probes;

/// <summary>
/// 热更注册契约探针集中地：独立于测试主程序集，任何测试不再对测试主程序集或本程序集做整集扫描构造，
/// 注册入口经反射直驱 + 探针类型逐个喂入，消除类型元数据顺序带来的不确定性。
/// </summary>
public static class HotfixRegistrationProbes
{
    /// <summary>
    /// 契约违规探针：直接实现 IComponentAgent（基类 object，非泛型基类），反推组件类型必须抛契约异常。
    /// </summary>
    public sealed class DirectInterfaceComponentAgent : IComponentAgent
    {
        /// <summary>代理占位成员。</summary>
        public long ActorId
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>代理占位成员。</summary>
        public IComponent Owner
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>代理占位成员。</summary>
        public ushort OwnerType
        {
            get { throw new NotImplementedException(); }
        }

        /// <summary>代理占位成员。</summary>
        public void SetOwner(IComponent owner)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task<bool> BeforeActivationAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>代理占位成员。</summary>
        public Task<bool> ActiveAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>代理占位成员。</summary>
        public Task<bool> AfterActivationAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>代理占位成员。</summary>
        public Task<bool> BeforeInActivationAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>代理占位成员。</summary>
        public Task InactiveAsync()
        {
            return Task.CompletedTask;
        }

        /// <summary>代理占位成员。</summary>
        public Task<bool> AfterInActivationAsync()
        {
            return Task.FromResult(true);
        }

        /// <summary>代理占位成员。</summary>
        public void Tell(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public void Tell(Func<Task> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task SendAsync(Action work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task<T> SendAsync<T>(Func<T> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task SendAsync(Func<Task> work, int timeOut = -1, bool checkLock = true, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task<T> SendAsync<T>(Func<Task<T>> work, int timeOut = -1, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task<IComponentAgent> GetComponentAgentAsync(Type agentType, bool isNew = true)
        {
            throw new NotImplementedException();
        }

        /// <summary>代理占位成员。</summary>
        public Task<T> GetComponentAgentAsync<T>(bool isNew = true) where T : IComponentAgent
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// 注册期构造校验探针：组件代理仅提供带参构造，故意不提供公共无参构造函数。
    /// </summary>
    public sealed class MissingParameterlessConstructorComponentAgent : BaseComponentAgent<PlayerComponent>
    {
        /// <summary>
        /// 唯一带参构造：使本类型在注册期构造校验中失败（无公共无参构造函数）。
        /// </summary>
        /// <param name="constructorValue">占位构造参数（无实际用途）。</param>
        public MissingParameterlessConstructorComponentAgent(int constructorValue)
        {
        }
    }

    /// <summary>
    /// 未注册组件探针：不携带 ComponentType 特性且不进入任何 ComponentRegister.InitAsync 扫描面。
    /// </summary>
    public sealed class UnregisteredProbeComponent : BaseComponent
    {
    }

    /// <summary>
    /// 指向未注册组件的代理探针。
    /// </summary>
    public sealed class UnregisteredProbeComponentAgent : BaseComponentAgent<UnregisteredProbeComponent>
    {
    }

    /// <summary>
    /// 探针事件参数。
    /// </summary>
    public sealed class ProbeEventArgs : GameEventArgs
    {
    }

    /// <summary>
    /// AddEvent 反查探针：监听未注册组件的代理，组件→Actor 反查必须抛带组件全名的契约异常。
    /// </summary>
    [Event(typeof(ProbeEventArgs))]
    public sealed class UnregisteredComponentEventListener : EventListener<UnregisteredProbeComponentAgent>
    {
        /// <summary>探针空实现（反查在监听器实例化之前失败，本方法不会被调用）。</summary>
        protected override Task HandleEventAsync(UnregisteredProbeComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 探针消息类型一：携带消息号特性供映射注册读取。
    /// </summary>
    [MessageTypeHandler(ProbeFirstMessageId)]
    public sealed class FirstProbeMessage
    {
    }

    /// <summary>
    /// 探针消息类型二。
    /// </summary>
    [MessageTypeHandler(ProbeSecondMessageId)]
    public sealed class SecondProbeMessage
    {
    }

    /// <summary>
    /// 同消息号探针消息一（与 <see cref="DuplicateMessageIdProbeMessage"/> 共享消息号）。
    /// </summary>
    [MessageTypeHandler(ProbeDuplicateMessageId)]
    public sealed class DuplicateMessageIdFirstProbeMessage
    {
    }

    /// <summary>
    /// 同消息号探针消息二。
    /// </summary>
    [MessageTypeHandler(ProbeDuplicateMessageId)]
    public sealed class DuplicateMessageIdSecondProbeMessage
    {
    }

    /// <summary>
    /// 注册期校验探针一：带 [MessageMapping] 但不实现 IMessageHandler。
    /// </summary>
    [MessageMapping(typeof(FirstProbeMessage))]
    public sealed class NotMessageHandlerProbeHandler
    {
    }

    /// <summary>
    /// 注册期校验探针二：实现 IMessageHandler 但仅提供带参构造，故意不提供公共无参构造函数。
    /// </summary>
    [MessageMapping(typeof(SecondProbeMessage))]
    public sealed class MissingParameterlessConstructorMessageHandler : IMessageHandler
    {
        /// <summary>
        /// 唯一带参构造：使本类型在注册期构造校验中失败（无公共无参构造函数）。
        /// </summary>
        /// <param name="constructorValue">占位构造参数（无实际用途）。</param>
        public MissingParameterlessConstructorMessageHandler(int constructorValue)
        {
        }

        /// <summary>探针空实现。</summary>
        public Task<bool> InitAsync(INetworkMessage message, INetworkChannel networkChannel)
        {
            return Task.FromResult(true);
        }

        /// <summary>探针空实现。</summary>
        public Task InnerActionAsync(int timeout = 30000, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>探针消息类型。</summary>
        public Type GetMessageType()
        {
            return typeof(SecondProbeMessage);
        }

        /// <summary>探针响应类型。</summary>
        public Type GetResponseType()
        {
            return typeof(SecondProbeMessage);
        }
    }

    /// <summary>
    /// 同消息号冲突探针处理器一（合法可实例化）。
    /// </summary>
    [MessageMapping(typeof(DuplicateMessageIdFirstProbeMessage))]
    public sealed class DuplicateMessageIdFirstComponentHandler : ValidProbeMessageHandler
    {
    }

    /// <summary>
    /// 同消息号冲突探针处理器二（合法可实例化，消息号与处理器一相同）。
    /// </summary>
    [MessageMapping(typeof(DuplicateMessageIdSecondProbeMessage))]
    public sealed class DuplicateMessageIdSecondComponentHandler : ValidProbeMessageHandler
    {
    }

    /// <summary>
    /// 合法消息处理器探针基类（满足密封/后缀/无参构造契约）。
    /// </summary>
    public abstract class ValidProbeMessageHandler : IMessageHandler
    {
        /// <summary>探针空实现。</summary>
        public Task<bool> InitAsync(INetworkMessage message, INetworkChannel networkChannel)
        {
            return Task.FromResult(true);
        }

        /// <summary>探针空实现。</summary>
        public Task InnerActionAsync(int timeout = 30000, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>探针消息类型。</summary>
        public Type GetMessageType()
        {
            return typeof(FirstProbeMessage);
        }

        /// <summary>探针响应类型。</summary>
        public Type GetResponseType()
        {
            return typeof(FirstProbeMessage);
        }
    }

    /// <summary>
    /// RPC 探针请求消息类型一。
    /// </summary>
    public sealed class FirstRpcProbeRequestMessage
    {
    }

    /// <summary>
    /// RPC 探针响应消息类型一。
    /// </summary>
    public sealed class FirstRpcProbeResponseMessage
    {
    }

    /// <summary>
    /// 合法 RPC 处理器探针（无参构造可实例化）。
    /// </summary>
    [MessageRpcMapping(typeof(FirstRpcProbeRequestMessage), typeof(FirstRpcProbeResponseMessage))]
    public sealed class ValidRpcProbeHandler : ValidProbeMessageHandler
    {
    }

    /// <summary>
    /// RPC 重复注册探针：与 <see cref="ValidRpcProbeHandler"/> 映射同一请求消息类型。
    /// </summary>
    [MessageRpcMapping(typeof(FirstRpcProbeRequestMessage), typeof(FirstRpcProbeResponseMessage))]
    public sealed class DuplicateRpcProbeHandler : ValidProbeMessageHandler
    {
    }
    /// <summary>
    /// 全小写命令 HTTP 探针：类名去掉 HttpHandler 后缀的 OriginalCmd 与 StandardCmd 完全相等，
    /// 单命令只应注册一次（旧实现第二次 TryAdd 同键失败抛"重复注册"）。
    /// </summary>
    [HttpMessageMapping(typeof(statusHttpHandler))]
    public sealed class statusHttpHandler : BaseHttpHandler
    {
    }

    /// <summary>
    /// 大小写变体 HTTP 探针：OriginalCmd 含大写字母（PlayerInfo / player_info 双命令），
    /// 大小写变体查询必须命中同一处理器（旧字典区分大小写返回 null）。
    /// </summary>
    [HttpMessageMapping(typeof(PlayerInfoHttpHandler))]
    public sealed class PlayerInfoHttpHandler : BaseHttpHandler
    {
    }

    /// <summary>
    /// RPC 注册期构造校验探针：仅提供带参构造，注册期 CreateFactory 必须抛出。
    /// </summary>
    [MessageRpcMapping(typeof(FirstRpcProbeRequestMessage), typeof(FirstRpcProbeResponseMessage))]
    public sealed class MissingParameterlessConstructorRpcProbeHandler : IMessageHandler
    {
        /// <summary>
        /// 唯一带参构造：使本类型在注册期构造校验中失败（无公共无参构造函数）。
        /// </summary>
        /// <param name="constructorValue">占位构造参数（无实际用途）。</param>
        public MissingParameterlessConstructorRpcProbeHandler(int constructorValue)
        {
        }

        /// <summary>探针空实现。</summary>
        public Task<bool> InitAsync(INetworkMessage message, INetworkChannel networkChannel)
        {
            return Task.FromResult(true);
        }

        /// <summary>探针空实现。</summary>
        public Task InnerActionAsync(int timeout = 30000, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>探针消息类型。</summary>
        public Type GetMessageType()
        {
            return typeof(FirstRpcProbeRequestMessage);
        }

        /// <summary>探针响应类型。</summary>
        public Type GetResponseType()
        {
            return typeof(FirstRpcProbeResponseMessage);
        }
    }


    /// <summary>探针消息号一（不参与任何真实协议派发）。</summary>
    public const int ProbeFirstMessageId = 990_101;

    /// <summary>探针消息号二。</summary>
    public const int ProbeSecondMessageId = 990_102;

    /// <summary>同消息号探针共享的消息号。</summary>
    public const int ProbeDuplicateMessageId = 990_103;
}
