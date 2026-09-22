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
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
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


using System.Threading.Channels;
using GameFrameX.NetWork.RemoteMessaging.Routing;

namespace GameFrameX.Tests.Topology.Equivalence;

/// <summary>
/// 等价用例集的单 Role 邮箱（C143c D9）。
/// </summary>
/// <remarks>
/// Per-role mailbox for the topology equivalence suite (C143c D9).
/// Simulates one role's in-process message queue with the same guarantees the actor
/// pipeline provides in production: strict per-role FIFO processing, one message at a
/// time, and senders may either fire-and-forget or await the handler's completion
/// (the Actor.Tell/SendAsync semantics behind D3 case 1).
/// Known ceiling: this is a lightweight queue, not the real Actor; the real actor-backed
/// local dispatcher arrives with C143e and is exercised there.
/// The arrival log records the message type name of every processed message so the
/// equivalence tests can assert identical receive order across topologies.
/// </remarks>
public sealed class RoleMailbox
{
    /// <summary>
    /// 邮箱工作项：信封 + 完成源。
    /// </summary>
    private sealed class MailboxWorkItem
    {
        /// <summary>
        /// 初始化工作项。
        /// </summary>
        public MailboxWorkItem(MessageEnvelope envelope)
        {
            Envelope = envelope;
            Completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        /// <summary>路由信封 / The routing envelope</summary>
        public MessageEnvelope Envelope { get; }

        /// <summary>处理完成源 / The handler completion source</summary>
        public TaskCompletionSource<bool> Completion { get; }
    }

    /// <summary>
    /// Role 名。
    /// </summary>
    private readonly string _roleName;

    /// <summary>
    /// FIFO 工作项通道（单消费者）。
    /// </summary>
    private readonly Channel<MailboxWorkItem> _channel = Channel.CreateUnbounded<MailboxWorkItem>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    /// <summary>
    /// 单消费者处理任务。
    /// </summary>
    private readonly Task _processingTask;

    /// <summary>
    /// 到达日志锁。
    /// </summary>
    private readonly object _arrivalLogLock = new object();

    /// <summary>
    /// 按处理顺序记录的消息类型名日志。
    /// </summary>
    private readonly List<string> _arrivalLog = new List<string>();

    /// <summary>
    /// 当前消息处理器。
    /// </summary>
    private Func<MessageEnvelope, Task> _handler = _ => Task.CompletedTask;

    /// <summary>
    /// 初始化单 Role 邮箱并启动 FIFO 消费者。
    /// </summary>
    /// <param name="roleName">Role 名 / The role name</param>
    public RoleMailbox(string roleName)
    {
        _roleName = roleName;
        _processingTask = Task.Run(ProcessAsync);
    }

    /// <summary>
    /// 获取 Role 名。
    /// </summary>
    public string RoleName
    {
        get { return _roleName; }
    }

    /// <summary>
    /// 设置消息处理器（每个测试安装一次）。
    /// </summary>
    /// <param name="handler">消息处理器 / The message handler</param>
    public void SetHandler(Func<MessageEnvelope, Task> handler)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
    }

    /// <summary>
    /// 投递一封信封：入队后等待处理完成，等待期尊重取消令牌（超时语义）。
    /// </summary>
    /// <remarks>
    /// Enqueues the envelope and waits for its handler to complete.
    /// The enqueue itself is never cancelled by the hop timeout (the message has been
    /// committed to the queue once routing accepted it); only the wait observes the
    /// cancellation token, which is exactly the SendAsync-with-timeout semantics both
    /// topologies are compared on.
    /// </remarks>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消操作的令牌 / The cancellation token</param>
    public async Task DeliverAsync(MessageEnvelope envelope, CancellationToken cancellationToken)
    {
        var item = new MailboxWorkItem(envelope);
        await _channel.Writer.WriteAsync(item, CancellationToken.None);
        await item.Completion.Task.WaitAsync(cancellationToken);
    }

    /// <summary>
    /// 获取到达日志快照（按处理顺序的消息类型名）。
    /// </summary>
    /// <returns>消息类型名列表 / The ordered message type names</returns>
    public IReadOnlyList<string> GetArrivalLog()
    {
        lock (_arrivalLogLock)
        {
            return _arrivalLog.ToList();
        }
    }

    /// <summary>
    /// 关闭邮箱（停止接收新消息，测试收尾用）。
    /// </summary>
    public void Close()
    {
        _channel.Writer.TryComplete();
    }

    /// <summary>
    /// 单消费者处理循环：严格按 FIFO 逐条调用处理器并记录到达日志。
    /// </summary>
    private async Task ProcessAsync()
    {
        await foreach (var item in _channel.Reader.ReadAllAsync())
        {
            lock (_arrivalLogLock)
            {
                _arrivalLog.Add(item.Envelope.Message.GetType().Name);
            }

            try
            {
                await _handler(item.Envelope);
                item.Completion.SetResult(true);
            }
            catch (Exception exception)
            {
                item.Completion.SetException(exception);
            }
        }
    }
}
