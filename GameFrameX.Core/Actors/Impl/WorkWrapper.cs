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
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Core.Actors.Impl;

/// <summary>
/// 工作包装器
/// </summary>
public abstract class WorkWrapper
{
    /// <summary>
    /// 工作对象
    /// </summary>
    public WorkerActor Owner { get; set; }

    /// <summary>
    /// 超时时间（毫秒）
    /// </summary>
    public int TimeOut { get; set; }

    /// <summary>
    /// 调用链ID
    /// </summary>
    public long CallChainId { get; set; }

    /// <summary>
    /// 调用方传入的取消令牌。
    /// 入队阶段用于取消排队；执行阶段与超时合并为执行期取消源（见 <see cref="BeginExecution"/>）。
    /// </summary>
    public CancellationToken CallerToken { get; set; }

    /// <summary>
    /// 执行期取消源。超时（CancelAfter）或调用方取消时触发，
    /// 令牌经 <see cref="RuntimeContext.CurrentCancellationToken"/> 向下流转供协作式取消。
    /// </summary>
    private CancellationTokenSource executionCancellationTokenSource;
    /// <summary>
    /// 执行工作单元。子类实现不得向外抛出异常（异常一律经 TrySetException 传递给等待方）。
    /// </summary>
    /// <returns>工作单元任务</returns>
    public abstract Task DoTaskAsync();

    /// <summary>
    /// 获取调用链
    /// </summary>
    /// <returns></returns>
    public abstract string GetTrace();

    /// <summary>
    /// 强制设置结果
    /// </summary>
    public abstract void ForceSetResult();

    /// <summary>
    /// 开始执行生命周期：创建执行期取消源（联动调用方令牌 + 超时 CancelAfter），
    /// 并将令牌写入 <see cref="RuntimeContext"/> 供业务代码协作式取消。
    /// </summary>
    /// <returns>执行期取消令牌</returns>
    public CancellationToken BeginExecution()
    {
        var cancellationSource = CallerToken.CanBeCanceled
            ? CancellationTokenSource.CreateLinkedTokenSource(CallerToken)
            : new CancellationTokenSource();

        // int.MaxValue 表示未显式配置超时（生成器默认），视为不限时，不挂取消定时器
        if (TimeOut > 0 && TimeOut != int.MaxValue)
        {
            cancellationSource.CancelAfter(TimeSpan.FromMilliseconds(TimeOut));
        }

        executionCancellationTokenSource = cancellationSource;
        RuntimeContext.SetCancellationToken(cancellationSource.Token);
        return cancellationSource.Token;
    }

    /// <summary>
    /// 结束执行生命周期：复位取消上下文并按需释放执行期取消源。
    /// </summary>
    /// <param name="disposeCancellationSource">
    /// 是否释放执行期取消源。工作单元已被放弃跟踪（Actor 被隔离）时必须传 false——
    /// 孤儿任务可能仍持有令牌注册，释放会破坏其取消信令。
    /// </param>
    public void EndExecution(bool disposeCancellationSource)
    {
        RuntimeContext.ResetCancellationToken();

        if (!disposeCancellationSource)
        {
            return;
        }

        var cancellationSource = Interlocked.Exchange(ref executionCancellationTokenSource, null);
        cancellationSource?.Dispose();
    }

    /// <summary>
    /// 超时回调：以 <see cref="TimeoutException"/> 完成等待方任务。
    /// 注意：此处不重置链上下文——孤儿任务仍在执行，链上下文由其 finally 经
    /// <see cref="ResetContext"/>（条件重置）安全清理；提前清零会让孤儿内部的重入调用误判自死锁。
    /// </summary>
    public void OnTimeout()
    {
        string trace;
        try
        {
            trace = GetTrace();
        }
        catch (Exception exception)
        {
            // Localization: CoreExceptions.WorkerActor.TraceUnavailable - trace不可用：{0}
            trace = LocalizationService.GetString(Localization.Keys.CoreExceptions.WorkerActor.TraceUnavailable, exception.Message);
        }

        // Localization: CoreExceptions.WorkerActor.Timeout - WorkerActor工作单元执行超时({0}ms) actorId:{1} trace:{2}
        SetTimeoutException(new TimeoutException(LocalizationService.GetString(Localization.Keys.CoreExceptions.WorkerActor.Timeout, TimeOut, Owner?.Id, trace)));
    }

    /// <summary>
    /// 以超时异常完成等待方任务（子类实现，不做任何上下文重置）
    /// </summary>
    /// <param name="exception">超时异常</param>
    protected abstract void SetTimeoutException(Exception exception);

    /// <summary>
    /// 尝试设置取消状态
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    public abstract void TrySetCanceled(CancellationToken cancellationToken);

    /// <summary>
    /// 尝试设置异常状态
    /// </summary>
    /// <param name="exception">异常</param>
    public abstract void TrySetException(Exception exception);

    /// <summary>
    /// 设置上下文
    /// </summary>
    protected void SetContext()
    {
        RuntimeContext.SetContext(CallChainId, Owner.Id);
        Owner.CurrentChainId = CallChainId;
    }

    /// <summary>
    /// 重置上下文。
    /// 条件重置：仅当 Actor 当前链 ID 仍归属本工作单元时才清零，
    /// 避免孤儿任务或被拒绝入队的工作单元误清后继消息的链上下文（重入判定依赖该值）。
    /// </summary>
    public void ResetContext()
    {
        Owner?.ResetChainIdIfCurrent(CallChainId);
    }
}
