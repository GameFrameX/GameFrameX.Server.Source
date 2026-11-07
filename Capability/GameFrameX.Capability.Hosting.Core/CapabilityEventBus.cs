// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 机制事件总线（机制层唯一事件语义：Register 期订阅声明、同步注册序分发、单订阅异常记日志继续、句柄幂等退订）。
/// <para>
/// 维护约束：订阅只在 Register 期经注册上下文声明；<see cref="Publish{TEvent}"/> 在宿主 Activate 开始前
/// 拒绝（Register 期发出的事件后注册订阅者收不到）；主题命名约定 <c>&lt;族&gt;.&lt;域&gt;.&lt;事件&gt;</c>
///（内容机制不解释）；载荷类型按订阅时的泛型参数精确匹配，同主题不同载荷类型互不投递；
/// 退订经 <see cref="Subscribe{TEvent}"/> 返回的句柄——从订阅表移除后不再进入后续发布快照，
/// 已在快照内的当轮回调允许完成（Dispose 不等待回调）。
/// </para>
public sealed class CapabilityEventBus : ICapabilityEventBus, ICapabilityEventPublisher
{
    /// <summary>
    /// 订阅表锁。
    /// </summary>
    private readonly object _sync = new();

    /// <summary>
    /// 订阅表（注册序）。
    /// </summary>
    private readonly List<Subscription> _subscriptions = new();

    /// <summary>
    /// 装配日志出口（订阅体异常记录用）。
    /// </summary>
    private readonly ICapabilityAssemblyLog _assemblyLog;

    /// <summary>
    /// 是否已进入 Activate 期（发布开关）。
    /// </summary>
    private bool _acceptingPublishes;

    /// <summary>
    /// 初始化 <see cref="CapabilityEventBus"/>。
    /// </summary>
    /// <param name="assemblyLog">装配日志出口（不可为 null）。</param>
    public CapabilityEventBus(ICapabilityAssemblyLog assemblyLog)
    {
        _assemblyLog = assemblyLog ?? throw new ArgumentNullException(nameof(assemblyLog));
    }

    /// <summary>
    /// 声明一个订阅（Register 期经注册上下文调用；注册序即分发序）。
    /// </summary>
    /// <typeparam name="TEvent">事件载荷类型。</typeparam>
    /// <param name="topic">主题（非空）。</param>
    /// <param name="handler">同步订阅体（不可为 null）。</param>
    /// <returns>退订句柄（幂等释放；从订阅表移除本订阅）。</returns>
    public IDisposable Subscribe<TEvent>(string topic, Action<TEvent> handler)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Event topic must be a non-empty string.", nameof(topic));
        }

        if (handler == null)
        {
            throw new ArgumentNullException(nameof(handler));
        }

        Action<object> typedHandler = payload => handler((TEvent)payload);
        var subscription = new Subscription(topic, typeof(TEvent), typedHandler);
        lock (_sync)
        {
            _subscriptions.Add(subscription);
        }

        return new SubscriptionRelease(this, subscription);
    }

    /// <summary>
    /// 移除一个订阅（仅经退订句柄进入；不在订阅表中的条目为无操作）。
    /// </summary>
    /// <param name="subscription">订阅条目。</param>
    private void RemoveSubscription(Subscription subscription)
    {
        lock (_sync)
        {
            _subscriptions.Remove(subscription);
        }
    }

    /// <summary>
    /// 发布一条事件（Activate 开始前拒绝；同步按订阅注册序分发，单个订阅体异常记日志后继续）。
    /// </summary>
    /// <typeparam name="TEvent">事件载荷类型。</typeparam>
    /// <param name="topic">主题（非空）。</param>
    /// <param name="eventPayload">事件载荷。</param>
    public void Publish<TEvent>(string topic, TEvent eventPayload)
    {
        if (string.IsNullOrWhiteSpace(topic))
        {
            throw new ArgumentException("Event topic must be a non-empty string.", nameof(topic));
        }

        Subscription[] snapshot;
        lock (_sync)
        {
            if (!_acceptingPublishes)
            {
                // Localization: Capability.Hosting.EventPublishRejected - Activate 开始前拒绝发布事件。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.EventPublishRejected), topic));
            }

            snapshot = BuildSnapshotLocked(topic, typeof(TEvent));
        }

        for (var i = 0; i < snapshot.Length; i++)
        {
            try
            {
                snapshot[i].Handler(eventPayload);
            }
            catch (Exception exception)
            {
                // Localization: Capability.Hosting.EventBus.SubscriberFailed - 能力事件订阅处理失败。
                _assemblyLog.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.EventBusSubscriberFailed), topic, exception.Message));
            }
        }
    }

    /// <summary>
    /// 标记进入 Activate 期（宿主启动时调用一次；开启发布）。
    /// </summary>
    internal void MarkActivationBegun()
    {
        lock (_sync)
        {
            _acceptingPublishes = true;
        }
    }

    /// <summary>
    /// 构建主题 + 载荷类型的订阅快照（须在锁内调用）。
    /// </summary>
    /// <param name="topic">主题。</param>
    /// <param name="payloadType">载荷类型。</param>
    /// <returns>订阅快照（注册序）。</returns>
    private Subscription[] BuildSnapshotLocked(string topic, Type payloadType)
    {
        var matched = new List<Subscription>();
        for (var i = 0; i < _subscriptions.Count; i++)
        {
            if (string.Equals(_subscriptions[i].Topic, topic, StringComparison.Ordinal) && _subscriptions[i].PayloadType == payloadType)
            {
                matched.Add(_subscriptions[i]);
            }
        }

        return matched.ToArray();
    }

    /// <summary>
    /// 订阅条目（主题 + 载荷类型 + 装箱处理器）。
    /// </summary>
    private sealed class Subscription
    {
        /// <summary>
        /// 主题。
        /// </summary>
        public readonly string Topic;

        /// <summary>
        /// 载荷类型。
        /// </summary>
        public readonly Type PayloadType;

        /// <summary>
        /// 装箱处理器。
        /// </summary>
        public readonly Action<object> Handler;

        /// <summary>
        /// 初始化 <see cref="Subscription"/>。
        /// </summary>
        /// <param name="topic">主题。</param>
        /// <param name="payloadType">载荷类型。</param>
        /// <param name="handler">装箱处理器。</param>
        public Subscription(string topic, Type payloadType, Action<object> handler)
        {
            Topic = topic;
            PayloadType = payloadType;
            Handler = handler;
        }
    }

    /// <summary>
    /// 订阅退订句柄（幂等释放：首个 <see cref="Dispose"/> 从总线订阅表移除订阅，此后释放为无操作；
    /// 不等待已在发布快照内进入分发的回调——其当轮完成后订阅不再投递）。
    /// </summary>
    private sealed class SubscriptionRelease : IDisposable
    {
        /// <summary>
        /// 所属总线。
        /// </summary>
        private readonly CapabilityEventBus _owner;

        /// <summary>
        /// 目标订阅条目。
        /// </summary>
        private readonly Subscription _subscription;

        /// <summary>
        /// 释放标记（0 = 未释放；幂等性的唯一事实源）。
        /// </summary>
        private int _disposed;

        /// <summary>
        /// 初始化 <see cref="SubscriptionRelease"/>。
        /// </summary>
        /// <param name="owner">所属总线。</param>
        /// <param name="subscription">目标订阅条目。</param>
        public SubscriptionRelease(CapabilityEventBus owner, Subscription subscription)
        {
            _owner = owner;
            _subscription = subscription;
        }

        /// <summary>
        /// 退订（幂等）：首个调用移除订阅；并发释放恰有一次生效。
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _owner.RemoveSubscription(_subscription);
            }
        }
    }
}
