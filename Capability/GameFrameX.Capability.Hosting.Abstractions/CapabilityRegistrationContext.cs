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

namespace GameFrameX.Capability.Hosting;

/// <summary>
/// 能力注册上下文（装配期对单个能力模块开放的注册面）。
/// <para>
/// 维护约束：三类调度 job 的执行语义由调度壳统一承载——
/// <see cref="AddTickJob"/>（每调度滴答顺序执行，执行序 = 全宿主装配序）、
/// <see cref="AddIntervalJob"/>（独立周期，到点执行）、
/// <see cref="AddEventJob"/>（事件驱动爆发口，由宿主事件入口同步分发）；
/// job 全名（<c>能力Id/jobId</c>）全宿主唯一，重复即装配失败。
/// </para>
/// </summary>
public sealed class CapabilityRegistrationContext
{
    /// <summary>
    /// 逐滴答 job 注册表（注册序即执行序；装配器收集后移交调度壳）。
    /// </summary>
    public IReadOnlyList<CapabilityTickJob> TickJobs
    {
        get { return _tickJobs; }
    }

    /// <summary>
    /// 间隔 job 注册表。
    /// </summary>
    public IReadOnlyList<CapabilityIntervalJob> IntervalJobs
    {
        get { return _intervalJobs; }
    }

    /// <summary>
    /// 事件驱动 job 注册表。
    /// </summary>
    public IReadOnlyList<CapabilityEventJob> EventJobs
    {
        get { return _eventJobs; }
    }

    /// <summary>
    /// 逐滴答 job 收集表。
    /// </summary>
    private readonly List<CapabilityTickJob> _tickJobs = new();

    /// <summary>
    /// 间隔 job 收集表。
    /// </summary>
    private readonly List<CapabilityIntervalJob> _intervalJobs = new();

    /// <summary>
    /// 事件驱动 job 收集表。
    /// </summary>
    private readonly List<CapabilityEventJob> _eventJobs = new();

    /// <summary>
    /// 已注册 job 全名集合（防重复）。
    /// </summary>
    private readonly HashSet<string> _registeredJobNames = new(StringComparer.Ordinal);

    /// <summary>
    /// 所属能力的清单（job 全名限定用）。
    /// </summary>
    private readonly CapabilityManifest _manifest;

    /// <summary>
    /// 初始化 <see cref="CapabilityRegistrationContext"/>。
    /// </summary>
    /// <param name="manifest">所属能力清单。</param>
    public CapabilityRegistrationContext(CapabilityManifest manifest)
    {
        _manifest = manifest ?? throw new ArgumentNullException(nameof(manifest));
    }

    /// <summary>
    /// 注册逐滴答 job（每个调度滴算按装配序执行；单 job 异常吞掉不中断当轮其余 job）。
    /// </summary>
    /// <param name="jobId">job 标识（能力内唯一，非空）。</param>
    /// <param name="job">job 委托。</param>
    public void AddTickJob(string jobId, Func<CancellationToken, Task> job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        _tickJobs.Add(new CapabilityTickJob(CreateJobName(jobId), job));
    }

    /// <summary>
    /// 注册间隔 job（独立周期到点执行，与滴答序无关）。
    /// </summary>
    /// <param name="jobId">job 标识（能力内唯一，非空）。</param>
    /// <param name="interval">执行周期（调度壳按 100ms 下限钳制）。</param>
    /// <param name="job">job 委托。</param>
    public void AddIntervalJob(string jobId, TimeSpan interval, Func<CancellationToken, Task> job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        _intervalJobs.Add(new CapabilityIntervalJob(CreateJobName(jobId), interval, job));
    }

    /// <summary>
    /// 注册事件驱动 job（宿主事件入口同步分发；单 job 异常吞掉不影响其它 job）。
    /// </summary>
    /// <param name="jobId">job 标识（能力内唯一，非空）。</param>
    /// <param name="job">job 委托。</param>
    public void AddEventJob(string jobId, Action<CapabilityEventNotification> job)
    {
        if (job == null)
        {
            throw new ArgumentNullException(nameof(job));
        }

        _eventJobs.Add(new CapabilityEventJob(CreateJobName(jobId), job));
    }

    /// <summary>
    /// 生成并登记能力限定的 job 全名（重复即抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    /// <param name="jobId">job 标识。</param>
    /// <returns>job 全名（<c>能力Id/jobId</c>）。</returns>
    private string CreateJobName(string jobId)
    {
        if (string.IsNullOrWhiteSpace(jobId))
        {
            throw new ArgumentException("Job id must be a non-empty string.", nameof(jobId));
        }

        var fullName = _manifest.Id + "/" + jobId;
        if (!_registeredJobNames.Add(fullName))
        {
            throw new InvalidOperationException("Duplicate capability job registration: " + fullName);
        }

        return fullName;
    }
}
