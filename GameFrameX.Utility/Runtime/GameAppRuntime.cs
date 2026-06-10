// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
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

namespace GameFrameX.Utility.Runtime;

/// <summary>
/// 游戏应用程序运行期状态。
/// </summary>
/// <remarks>
/// Game application runtime state for the current process, including launch time, running flag, exit reason, and exit token.
/// </remarks>
public static class GameAppRuntime
{
    private static readonly Lock SyncRoot = new();
    private static TaskCompletionSource<string> _appExitSource = CreateExitSource();

    /// <summary>
    /// 获取应用程序启动时间。
    /// </summary>
    /// <remarks>
    /// Gets the application launch time.
    /// </remarks>
    public static DateTime LaunchTime { get; private set; }

    /// <summary>
    /// 获取应用程序是否正在运行。
    /// </summary>
    /// <remarks>
    /// Gets whether the application is running.
    /// </remarks>
    public static bool IsRunning { get; private set; }

    /// <summary>
    /// 获取应用程序退出原因。
    /// </summary>
    /// <remarks>
    /// Gets the application exit reason.
    /// </remarks>
    public static string ExitReason { get; private set; } = string.Empty;

    /// <summary>
    /// 获取应用程序退出任务。
    /// </summary>
    /// <remarks>
    /// Gets the application exit task. The task result is the exit reason.
    /// </remarks>
    public static Task<string> AppExitToken
    {
        get
        {
            lock (SyncRoot)
            {
                return _appExitSource.Task;
            }
        }
    }

    /// <summary>
    /// 标记应用程序已启动。
    /// </summary>
    /// <remarks>
    /// Marks the application as started.
    /// </remarks>
    /// <param name="launchTime">启动时间 / Launch time</param>
    public static void MarkStarted(DateTime launchTime)
    {
        lock (SyncRoot)
        {
            if (_appExitSource.Task.IsCompleted)
            {
                _appExitSource = CreateExitSource();
            }

            LaunchTime = launchTime;
            IsRunning = true;
            ExitReason = string.Empty;
        }
    }

    /// <summary>
    /// 标记应用程序正在停止。
    /// </summary>
    /// <remarks>
    /// Marks the application as stopping without completing the exit token.
    /// </remarks>
    public static void MarkStopping()
    {
        lock (SyncRoot)
        {
            IsRunning = false;
        }
    }

    /// <summary>
    /// 标记应用程序已停止。
    /// </summary>
    /// <remarks>
    /// Marks the application as stopped and completes the exit token with the specified reason.
    /// </remarks>
    /// <param name="reason">退出原因 / Exit reason</param>
    /// <returns>如果本次调用完成了退出任务则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if this call completed the exit task; otherwise <c>false</c></returns>
    public static bool MarkStopped(string reason)
    {
        lock (SyncRoot)
        {
            IsRunning = false;
            if (_appExitSource.Task.IsCompleted)
            {
                return false;
            }

            ExitReason = reason ?? string.Empty;
            return _appExitSource.TrySetResult(ExitReason);
        }
    }

    private static TaskCompletionSource<string> CreateExitSource()
    {
        return new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
