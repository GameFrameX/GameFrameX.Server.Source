//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 举报提交限流器（双固定窗口计数：分钟窗 + 日窗，防脚本刷单与骚扰式重复举报）。
/// <para>
/// 维护约束（为什么双窗）：分钟窗拦截短时爆发（把运营后台刷满），日窗限制单举报人对全服的
/// 长期骚扰总量。键 = (租户, App, 举报人)——按举报人而非「举报人 + 被举报人」分桶，
/// 否则换目标就能绕过日额度。合法提交尝试（含命中去重的重复举报）都计数：
/// 否则脚本可以无限刷重复提交探测存储。
/// </para>
/// <para>
/// 维护约束（两窗原子）：任一窗口超限即拒绝，且失败不消耗另一窗口额度；两窗都在同一临界区内
/// 判定与计数，并发提交不会突破额度。桶采用值类型窗口副本存储，避免逐请求堆分配。
/// </para>
/// <para>
/// 天花板（ponytail）：固定窗口在边界处可能出现两倍瞬时突发，与聊天域既有约定一致；
/// 进程内计数重启清零、多实例不共享——生产多实例需换共享存储实现（归运行时装配）。
/// </para>
/// </summary>
public sealed class OnlineReportRateLimiter
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new();

    /// <summary>双窗计数桶：键 = (租户, App, 举报人)。</summary>
    private readonly Dictionary<string, ReportWindow> _windows = new(StringComparer.Ordinal);

    /// <summary>上一次惰性清扫时刻（UTC 毫秒）。</summary>
    private long _lastSweepAtTime;

    /// <summary>分钟窗内允许的提交次数。</summary>
    private readonly int _maximumSubmissionsPerMinute;

    /// <summary>日窗内允许的提交次数。</summary>
    private readonly int _maximumSubmissionsPerDay;

    /// <summary>分钟窗时长（毫秒）。</summary>
    private const long MinuteWindowMilliseconds = 60000L;

    /// <summary>日窗时长（毫秒）。</summary>
    private const long DayWindowMilliseconds = 86400000L;

    /// <summary>
    /// 初始化 <see cref="OnlineReportRateLimiter"/>。
    /// </summary>
    /// <param name="maximumSubmissionsPerMinute">分钟窗内允许的提交次数（必须为正）。</param>
    /// <param name="maximumSubmissionsPerDay">日窗内允许的提交次数（必须为正）。</param>
    public OnlineReportRateLimiter(int maximumSubmissionsPerMinute, int maximumSubmissionsPerDay)
    {
        _maximumSubmissionsPerMinute = maximumSubmissionsPerMinute > 0 ? maximumSubmissionsPerMinute : 1;
        _maximumSubmissionsPerDay = maximumSubmissionsPerDay > 0 ? maximumSubmissionsPerDay : 1;
    }

    /// <summary>
    /// 尝试获取一次举报提交配额（两窗都在额度内才同时计数；任一超限则两窗都不消耗）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="reporterId">举报人。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <returns>允许返回 <c>true</c>；任一窗口超限返回 <c>false</c>。</returns>
    public bool TryAcquire(long tenantId, long appId, long reporterId, long nowUnixMilliseconds = 0)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = tenantId + ":" + appId + ":" + reporterId;

        lock (_syncRoot)
        {
            if (now - _lastSweepAtTime >= MinuteWindowMilliseconds)
            {
                SweepExpiredCore(now);
            }

            ReportWindow window;
            if (!_windows.TryGetValue(key, out window))
            {
                window = new ReportWindow();
            }

            // 窗口过期即重开（固定起点计数窗；过期重开不携带旧计数）。
            if (now - window.MinuteWindowStartedAtTime >= MinuteWindowMilliseconds)
            {
                window.MinuteWindowStartedAtTime = now;
                window.MinuteCount = 0;
            }

            if (now - window.DayWindowStartedAtTime >= DayWindowMilliseconds)
            {
                window.DayWindowStartedAtTime = now;
                window.DayCount = 0;
            }

            if (window.MinuteCount >= _maximumSubmissionsPerMinute || window.DayCount >= _maximumSubmissionsPerDay)
            {
                // 拒绝：不消耗任何窗口额度（重复探测不能烧掉正常玩家的日额度）。
                _windows[key] = window;
                return false;
            }

            window.MinuteCount++;
            window.DayCount++;
            _windows[key] = window;
            return true;
        }
    }

    /// <summary>
    /// 删除两窗均已过期的桶（运维/测试可显式触发；TryAcquire 亦按分钟惰性触发）。
    /// <para>
    /// 维护约束：仅当分钟窗与日窗**同时**过期才可删桶——日窗未过期的桶删除会凭空重置日额度，
    /// 限流形同虚设。
    /// </para>
    /// </summary>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <returns>本次删除的桶数量。</returns>
    public int SweepExpired(long nowUnixMilliseconds = 0)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        lock (_syncRoot)
        {
            return SweepExpiredCore(now);
        }
    }

    /// <summary>
    /// 锁内全量清扫两窗均已过期的桶（调用方必须已持有 <see cref="_syncRoot"/>）。
    /// </summary>
    /// <param name="now">当前时刻（UTC 毫秒）。</param>
    /// <returns>本次删除的桶数量。</returns>
    private int SweepExpiredCore(long now)
    {
        _lastSweepAtTime = now;
        List<string> expiredKeys = null;
        foreach (var pair in _windows)
        {
            var window = pair.Value;
            if (now - window.MinuteWindowStartedAtTime >= MinuteWindowMilliseconds && now - window.DayWindowStartedAtTime >= DayWindowMilliseconds)
            {
                (expiredKeys ?? (expiredKeys = new List<string>())).Add(pair.Key);
            }
        }

        if (expiredKeys == null)
        {
            return 0;
        }

        for (var index = 0; index < expiredKeys.Count; index++)
        {
            _windows.Remove(expiredKeys[index]);
        }

        return expiredKeys.Count;
    }

    /// <summary>
    /// 双窗计数桶（值类型：读写均为副本，避免逐请求堆分配）。
    /// </summary>
    private struct ReportWindow
    {
        /// <summary>获取或设置分钟窗起始时刻（UTC 毫秒）。</summary>
        public long MinuteWindowStartedAtTime { get; set; }

        /// <summary>获取或设置分钟窗内已计数。</summary>
        public int MinuteCount { get; set; }

        /// <summary>获取或设置日窗起始时刻（UTC 毫秒）。</summary>
        public long DayWindowStartedAtTime { get; set; }

        /// <summary>获取或设置日窗内已计数。</summary>
        public int DayCount { get; set; }
    }
}
