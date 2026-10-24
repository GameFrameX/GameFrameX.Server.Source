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
//  ==========================================================================================

using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// 调度日志观察器（缺省观测出口：job 失败按结构化文案写装配日志出口；宿主注入真实指标管道时被替换）。
/// <para>
/// 维护约束：每轮指标（耗时 / 执行数 / 失败数）不落日志（归指标管道），本实现只承载失败行；
/// 观测回调自身异常吞掉（不得阻断调度循环）。
/// </para>
/// </summary>
internal sealed class CapabilitySchedulerLogObserver : ICapabilitySchedulerObserver
{
    /// <summary>
    /// 装配日志出口。
    /// </summary>
    private readonly ICapabilityAssemblyLog _assemblyLog;

    /// <summary>
    /// 初始化 <see cref="CapabilitySchedulerLogObserver"/>。
    /// </summary>
    /// <param name="assemblyLog">装配日志出口。</param>
    public CapabilitySchedulerLogObserver(ICapabilityAssemblyLog assemblyLog)
    {
        _assemblyLog = assemblyLog;
    }

    /// <summary>
    /// 上报一次 job 执行失败（按种类选结构化文案记 Error）。
    /// </summary>
    /// <param name="jobKind">job 种类。</param>
    /// <param name="jobName">job 全名。</param>
    /// <param name="exception">job 抛出的异常。</param>
    public void OnJobFailed(CapabilitySchedulerJobKind jobKind, string jobName, Exception exception)
    {
        var message = exception == null ? string.Empty : exception.Message;
        if (jobKind == CapabilitySchedulerJobKind.Interval)
        {
            // Localization: Capability.Hosting.Scheduler.IntervalJobFailed - 能力调度间隔 job 失败。
            _assemblyLog.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.SchedulerIntervalJobFailed), jobName, message));
            return;
        }

        // Localization: Capability.Hosting.Scheduler.TickJobFailed - 能力调度逐滴答 job 失败。
        _assemblyLog.Error(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.SchedulerTickJobFailed), jobName, message));
    }

    /// <summary>
    /// 上报一轮 tick 完成（日志观察器不落每轮指标行——归真实指标管道）。
    /// </summary>
    /// <param name="elapsed">本轮耗时。</param>
    /// <param name="executedJobCount">执行 job 数。</param>
    /// <param name="failedJobCount">失败 job 数。</param>
    public void OnTickRoundCompleted(TimeSpan elapsed, int executedJobCount, int failedJobCount)
    {
    }
}
