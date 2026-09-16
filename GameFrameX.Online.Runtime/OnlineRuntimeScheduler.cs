// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
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

using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Runtime;

/// <summary>
/// Online Runtime 后台调度器（change C122 决策⑦：定时驱动对局 Tick、撮合单轮与各域过期清扫）。
/// <para>
/// 维护约束：单循环顺序驱动（不做多任务并发——InMemory 存储为单进程默认实现，并发 Tick 与撮合
/// 对同一票据集合操作会放大竞态）；单轮异常吞掉并继续（调度中断即整个 Runtime 停摆，
/// 比单轮失败更糟）；Season/Tournament 到点驱动因 store 无枚举 API 登记为能力缺口（决策⑧③），
/// 不在本调度器内做扫描。
/// </para>
/// </summary>
public sealed class OnlineRuntimeScheduler
{
    /// <summary>
    /// 宿主（服务集合的驱动对象）。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 停止令牌。
    /// </summary>
    private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();

    /// <summary>
    /// 循环任务（启动后非空）。
    /// </summary>
    private Task _loopTask;

    /// <summary>
    /// 同步锁（Start/Stop 竞态保护）。
    /// </summary>
    private readonly object _sync = new object();

    /// <summary>
    /// 初始化 <see cref="OnlineRuntimeScheduler"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineRuntimeScheduler(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 启动调度循环（可重复调用：已启动时为空操作）。
    /// </summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_loopTask != null)
            {
                return;
            }

            _loopTask = Task.Run(() => RunAsync(_cancellation.Token));
        }
    }

    /// <summary>
    /// 停止调度循环（等待当轮完成后退出；可重复调用）。
    /// </summary>
    /// <returns>异步任务。</returns>
    public async Task StopAsync()
    {
        Task loopTask;
        lock (_sync)
        {
            _cancellation.Cancel();
            loopTask = _loopTask;
        }

        if (loopTask == null)
        {
            return;
        }

        try
        {
            await loopTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            // 等待周期被取消打断属预期退出路径。
        }
    }

    /// <summary>
    /// 调度循环主体。
    /// </summary>
    /// <param name="cancellationToken">停止令牌。</param>
    /// <returns>异步任务。</returns>
    private async Task RunAsync(CancellationToken cancellationToken)
    {
        var interval = TimeSpan.FromMilliseconds(Math.Max(100, _host.Options.SchedulerIntervalMilliseconds));
        using (var timer = new PeriodicTimer(interval))
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false);
                    await TickAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // 单轮失败不终止循环（见类注释维护约束）。
                }
            }
        }
    }

    /// <summary>
    /// 执行单轮调度（对局 Tick → 撮合单轮 → 各域过期清扫，顺序驱动）。
    /// </summary>
    /// <param name="cancellationToken">停止令牌。</param>
    /// <returns>异步任务。</returns>
    public async Task TickAsync(CancellationToken cancellationToken = default)
    {
        var scope = _host.AuthorizedScope;
        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        await _host.MatchRuntime.TickAllAsync(now, cancellationToken).ConfigureAwait(false);
        await _host.Matchmaker.RunOnceAsync(scope.TenantId, scope.AppId, now, cancellationToken).ConfigureAwait(false);
        await _host.MatchTickets.SweepExpiredAsync(scope.TenantId, scope.AppId, now, cancellationToken).ConfigureAwait(false);
        await _host.Presence.SweepTimeoutsAsync(scope.TenantId, scope.AppId, now, cancellationToken).ConfigureAwait(false);
        await _host.Tokens.SweepExpiredAsync(now, cancellationToken).ConfigureAwait(false);
        await _host.Notifications.SweepExpiredAsync(scope.TenantId, scope.AppId, now, cancellationToken).ConfigureAwait(false);
    }
}
