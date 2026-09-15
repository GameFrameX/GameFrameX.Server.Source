//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Match;

/// <summary>
/// Match Runtime 运行参数（vault:C6 Tick/Timer 与清理策略、重连窗口的装配面）。
/// <para>
/// 维护约束：所有阈值必须可由装配方覆盖——压测要同时验证「超窗后确定态」（VC-5.7）与「窗口内重连成功」
/// （VC-5.6），阈值写死则该验证无法进行。默认值只保证单机测试可用，不代表线上取值。
/// </para>
/// </summary>
public sealed class OnlineMatchRuntimeOptions
{
    /// <summary>
    /// 获取或设置等待阶段时限（秒；默认 60）。超时转 <see cref="OnlineMatchState.Timeout"/>（VC-5.12）。
    /// </summary>
    public int WaitingTimeoutSeconds
    {
        get;
        set;
    } = 60;

    /// <summary>
    /// 获取或设置运行阶段时限（秒；默认 300）。超时转 <see cref="OnlineMatchState.Timeout"/>。
    /// </summary>
    public int RunningTimeoutSeconds
    {
        get;
        set;
    } = 300;

    /// <summary>
    /// 获取或设置断线重连窗口（秒；默认 30）。窗口内可重连，超窗转退出/托管（VC-5.6 / VC-5.7）。
    /// </summary>
    public int ReconnectWindowSeconds
    {
        get;
        set;
    } = 30;

    /// <summary>
    /// 获取或设置结束态保留时长（秒；默认 30）。保留期内可查询结果，到期由 Tick 释放（VC-5.11）。
    /// </summary>
    public int EndedRetentionSeconds
    {
        get;
        set;
    } = 30;

    /// <summary>
    /// 获取或设置服务器事件日志上限（条；默认 512）。超出后仅保留最近 N 条，
    /// 重连跨越该窗口时退回全量快照（vault 风险缓解「快照只含必要状态，对快照大小设上限并测量」）。
    /// </summary>
    public int MaxServerEventsPerMatch
    {
        get;
        set;
    } = 512;

    /// <summary>
    /// 复制配置（装配方传递后不被后续改动影响）。
    /// </summary>
    /// <returns>配置副本。</returns>
    public OnlineMatchRuntimeOptions Copy()
    {
        return new OnlineMatchRuntimeOptions
        {
            WaitingTimeoutSeconds = WaitingTimeoutSeconds,
            RunningTimeoutSeconds = RunningTimeoutSeconds,
            ReconnectWindowSeconds = ReconnectWindowSeconds,
            EndedRetentionSeconds = EndedRetentionSeconds,
            MaxServerEventsPerMatch = MaxServerEventsPerMatch,
        };
    }
}
