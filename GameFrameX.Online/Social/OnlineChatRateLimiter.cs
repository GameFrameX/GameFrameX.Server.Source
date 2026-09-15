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

namespace GameFrameX.Online.Social;

/// <summary>
/// 聊天发送限流器（vault:C7 S6.7 / VC-6.14：刷屏被限流，且正常玩家不受影响）。
/// <para>
/// 维护约束（为什么按频道分桶）：键 = (租户, App, 玩家, 频道)。按玩家单键会让
/// 「在全局频道刷屏」把私聊配额一起吃掉——玩家在公共频道的活跃不该惩罚他的私聊能力。
/// 反过来，按频道分桶后玩家仍无法用「多开频道」绕过：每个频道各自计数，总速率仍有界。
/// </para>
/// <para>
/// 天花板（ponytail）：滑动窗口计数（桶在窗口过期后整桶重置，不逐条出队），
/// 内存与「活跃玩家 × 频道数」同阶；进程重启即清零——限流是速率保护不是配额。
/// 生产多实例下需换成共享存储实现（归运行时装配）。
/// </para>
/// </summary>
public sealed class OnlineChatRateLimiter
{
    /// <summary>并发保护锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>窗口计数桶：键 = (租户, App, 玩家, 频道)。</summary>
    private readonly Dictionary<string, Window> _windows = new Dictionary<string, Window>(StringComparer.Ordinal);

    /// <summary>窗口内允许的发送条数。</summary>
    private readonly int _maxMessages;

    /// <summary>窗口时长（毫秒）。</summary>
    private readonly long _windowMilliseconds;

    /// <summary>
    /// 初始化 <see cref="OnlineChatRateLimiter"/>。
    /// </summary>
    /// <param name="maxMessages">窗口内允许的发送条数（必须为正）。</param>
    /// <param name="windowSeconds">窗口时长（秒；必须为正）。</param>
    public OnlineChatRateLimiter(int maxMessages, int windowSeconds)
    {
        _maxMessages = maxMessages > 0 ? maxMessages : 1;
        _windowMilliseconds = (windowSeconds > 0 ? windowSeconds : 1) * 1000L;
    }

    /// <summary>
    /// 尝试获取一次发送配额。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">发送者。</param>
    /// <param name="channelId">频道标识。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <returns>允许返回 <c>true</c>；超限返回 <c>false</c>。</returns>
    public bool TryAcquire(long tenantId, long appId, long playerId, string channelId, long nowUnixMilliseconds = 0)
    {
        var now = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var key = tenantId + ":" + appId + ":" + playerId + ":" + (channelId ?? string.Empty);

        lock (_syncRoot)
        {
            Window window;
            if (!_windows.TryGetValue(key, out window) || now - window.StartedAtTime >= _windowMilliseconds)
            {
                _windows[key] = new Window
                {
                    StartedAtTime = now,
                    Count = 1,
                };
                return true;
            }

            if (window.Count >= _maxMessages)
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
