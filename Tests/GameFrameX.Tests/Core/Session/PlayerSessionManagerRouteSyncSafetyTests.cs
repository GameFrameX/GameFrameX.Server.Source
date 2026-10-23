// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws and the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Core.Session;
using GameFrameX.Discovery.Routing;

namespace GameFrameX.Tests.Core.Session;

/// <summary>
/// 路由同步钩子失败形态契约测试：同步抛出与异步 faulted 两种失败都必须被 swallow（仅 warning 日志），
/// 不向调用方传播、不产生 UnobservedTaskException，且内存 PlayerRouteMap 始终为权威态。
/// </summary>
public sealed class PlayerSessionManagerRouteSyncSafetyTests
{
    private const string FailureMarker = "route-sync-boom";

    [Fact]
    public void SetPlayerRouteOnline_UpsertHookThrowsSynchronously_ShouldNotPropagate()
    {
        var manager = PlayerSessionManager.Instance;
        using (var scope = SyncTargetScope.Use(new FailingRouteSyncTarget(failSynchronously: true)))
        {
            manager.SetPlayerRouteOnline(921001, "Game");

            Assert.True(manager.TryGetPlayerRoute(921001, out var snapshot));
            Assert.True(snapshot.IsOnline);
        }
    }

    [Fact]
    public void SetPlayerRouteOffline_DeleteHookThrowsSynchronously_ShouldNotPropagate()
    {
        var manager = PlayerSessionManager.Instance;
        manager.SetPlayerRouteOnline(921002, "Game");
        using (var scope = SyncTargetScope.Use(new FailingRouteSyncTarget(failSynchronously: true)))
        {
            manager.SetPlayerRouteOffline(921002);

            Assert.True(manager.TryGetPlayerRoute(921002, out var snapshot));
            Assert.False(snapshot.IsOnline);
        }
    }

    [Fact]
    public async Task SetPlayerRouteOnline_UpsertHookFaultsAsynchronously_ShouldNotPropagateNorStayUnobserved()
    {
        var unobservedSeen = false;
        void Handler(object sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception?.InnerExceptions.Any(exception => exception.Message == FailureMarker) == true)
            {
                unobservedSeen = true;
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            var manager = PlayerSessionManager.Instance;
            using (var scope = SyncTargetScope.Use(new FailingRouteSyncTarget(failSynchronously: false)))
            {
                manager.SetPlayerRouteOnline(921003, "Game");

                // 内存态立即生效（不等待钩子）
                Assert.True(manager.TryGetPlayerRoute(921003, out var snapshot));
                Assert.True(snapshot.IsOnline);

                // 强制回收并等待 finalizer，让潜在未观测异常路径充分暴露
                for (var attempt = 0; attempt < 3 && !unobservedSeen; attempt++)
                {
                    await Task.Delay(20);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                Assert.False(unobservedSeen);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    [Fact]
    public async Task SetPlayerRouteOffline_DeleteHookFaultsAsynchronously_ShouldNotPropagateNorStayUnobserved()
    {
        var unobservedSeen = false;
        void Handler(object sender, UnobservedTaskExceptionEventArgs args)
        {
            if (args.Exception?.InnerExceptions.Any(exception => exception.Message == FailureMarker) == true)
            {
                unobservedSeen = true;
            }
        }

        TaskScheduler.UnobservedTaskException += Handler;
        try
        {
            var manager = PlayerSessionManager.Instance;
            manager.SetPlayerRouteOnline(921004, "Game");
            using (var scope = SyncTargetScope.Use(new FailingRouteSyncTarget(failSynchronously: false)))
            {
                manager.SetPlayerRouteOffline(921004);

                Assert.True(manager.TryGetPlayerRoute(921004, out var snapshot));
                Assert.False(snapshot.IsOnline);

                for (var attempt = 0; attempt < 3 && !unobservedSeen; attempt++)
                {
                    await Task.Delay(20);
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                }

                Assert.False(unobservedSeen);
            }
        }
        finally
        {
            TaskScheduler.UnobservedTaskException -= Handler;
        }
    }

    /// <summary>
    /// 同步目标替换作用域：退出时恢复 NoOp 缺省，避免单例状态泄漏到其他测试。
    /// </summary>
    private readonly struct SyncTargetScope : IDisposable
    {
        private readonly IPlayerSessionManager _manager;

        private SyncTargetScope(IPlayerSessionManager manager)
        {
            _manager = manager;
        }

        public static SyncTargetScope Use(IPlayerRouteSyncTarget target)
        {
            var manager = PlayerSessionManager.Instance;
            manager.PlayerRouteSyncTarget = target;
            return new SyncTargetScope(manager);
        }

        public void Dispose()
        {
            _manager.PlayerRouteSyncTarget = NullPlayerRouteSyncTarget.Instance;
        }
    }

    /// <summary>
    /// 失败形态可控的同步目标假件：同步抛出或返回 faulted 任务，消息带标记便于未观测异常归因。
    /// </summary>
    private sealed class FailingRouteSyncTarget : IPlayerRouteSyncTarget
    {
        private readonly bool _failSynchronously;

        public FailingRouteSyncTarget(bool failSynchronously)
        {
            _failSynchronously = failSynchronously;
        }

        public Task UpsertAsync(PlayerRouteRecord record)
        {
            return Fail();
        }

        public Task DeleteAsync(long playerId)
        {
            return Fail();
        }

        private Task Fail()
        {
            if (_failSynchronously)
            {
                throw new InvalidOperationException(FailureMarker);
            }

            return Task.FromException(new InvalidOperationException(FailureMarker));
        }
    }
}
