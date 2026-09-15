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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System;
using System.Collections.Generic;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜提交限流器（vault:C8 S7.1 防刷校验的高频半边；VC-7.4 高频提交被拒、正常玩家不受影响）。
/// <para>
/// 维护约束（天花板）：滑动窗口计数，按 (TenantId, AppId, PlayerId) 分桶——阈值与窗口由
/// <see cref="OnlineLeaderboardOptions"/> 覆盖；只在幂等 Execute 分支计数（重放不烧配额）。
/// 形态对齐 C97 <c>OnlineMatchRateLimiter</c> / C99 <c>OnlineChatRateLimiter</c>（滑动窗口，不新增依赖）；
/// 桶在窗口过期后整桶重置，进程重启即清零——限流是速率保护不是配额，可接受；多实例共享存储归运行时装配。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardRateLimiter
{
    /// <summary>全局读写锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>窗口计数桶：键 = (TenantId, AppId, PlayerId)。</summary>
    private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>();

    /// <summary>窗口内允许的提交次数。</summary>
    private readonly int _maxSubmissions;

    /// <summary>窗口时长（毫秒）。</summary>
    private readonly long _windowMilliseconds;

    /// <summary>
    /// 初始化 <see cref="OnlineLeaderboardRateLimiter"/>。
    /// </summary>
    /// <param name="maxSubmissions">窗口内允许的提交次数（必须为正）。</param>
    /// <param name="windowSeconds">窗口时长（秒；必须为正）。</param>
    public OnlineLeaderboardRateLimiter(int maxSubmissions, int windowSeconds)
    {
        _maxSubmissions = maxSubmissions > 0 ? maxSubmissions : 1;
        _windowMilliseconds = (windowSeconds > 0 ? windowSeconds : 1) * 1000L;
    }

    /// <summary>
    public bool TryAcquire(long tenantId, long appId, long playerId, long nowUnixMilliseconds = 0)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = tenantId + ":" + appId + ":" + playerId;

        lock (_syncRoot)
        {
            if (!_windows.TryGetValue(key, out var window) || now - window.StartedAtTime >= _windowMilliseconds)
            {
                _windows[key] = new Window
                {
                    StartedAtTime = now,
                    Count = 1,
                };
                return true;
            }

            if (window.Count >= _maxSubmissions)
            {
                return false;
            }

            window.Count++;
            return true;
        }
    }

    /// <summary>
    /// 清空全部窗口（测试与运维重置用）。
    /// </summary>
    public void Reset()
    {
        lock (_syncRoot)
        {
            _windows.Clear();
        }
    }


    /// <summary>
    /// 滑动窗口计数桶。
    /// </summary>
    private sealed class Window
    {
        /// <summary>获取或设置窗口起始时刻（UTC 毫秒）。</summary>
        public long StartedAtTime
        {
            get;
            set;
        }

        /// <summary>获取或设置窗口内已计数。</summary>
        public int Count
        {
            get;
            set;
        }
    }
}
