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
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer and contributors; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Discovery;
using Xunit;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// DiscoveryWatcher graveyard 保留窗口回归测试（GFX-1068 / GitHub #696）。
/// </summary>
/// <remarks>
/// Regression tests for the incarnation graveyard retention window: sightings age out
/// after the retention so the dictionary stays bounded, restart pairs (Offline+Online)
/// survive inside the window, and live instances are never pruned. Uses an in-process
/// heartbeat store stub — no database dependency.
/// </remarks>
public sealed class DiscoveryWatcherGraveyardRetentionTests
{
    /// <summary>
    /// 测试轮询间隔（80ms，快于缺省以缩短用例时长）。
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(80);

    /// <summary>
    /// 测试判活阈值（60s，用例时长内不触发陈旧路径）。
    /// </summary>
    private static readonly TimeSpan StalenessThreshold = TimeSpan.FromSeconds(60);

    /// <summary>
    /// 测试窗口内重启：同 instanceId 换 incarnation 重现仍发 Offline+Online 对，graveyard 条目保留。
    /// </summary>
    [Fact]
    public async Task RestartWithinRetention_ShouldKeepOfflineOnlinePair()
    {
        var store = new InProcessHeartbeatStore();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(store, PollInterval, StalenessThreshold))
        {
            watcher.Subscribe(events);
            store.Upsert(CreateDescriptor("instance-a", incarnation: 1));
            await watcher.StartAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(400));

            store.Remove("instance-a");
            await Task.Delay(TimeSpan.FromMilliseconds(400));

            // 窗口内：实例退出（Evicted）后 graveyard 记忆仍在。
            Assert.Equal(1, watcher.GraveyardEntryCount);

            store.Upsert(CreateDescriptor("instance-a", incarnation: 2));
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            await watcher.StopAsync();

            Assert.Equal(1, watcher.GraveyardEntryCount);
            var observed = events.Snapshot();
            Assert.Contains((RoleInstanceChangeKind.Online, "instance-a"), observed);
            Assert.Contains((RoleInstanceChangeKind.Evicted, "instance-a"), observed);
            Assert.Contains((RoleInstanceChangeKind.Offline, "instance-a"), observed);
            Assert.Equal(4, observed.Count);
        }
    }

    /// <summary>
    /// 测试窗口过期清理：条目超过保留窗口后被清除，同 id 重现退化为首次观测（仅 Online，无 Offline）。
    /// </summary>
    [Fact]
    public async Task ExpiredSighting_ShouldBePrunedAndReappearAsFirstObservation()
    {
        var store = new InProcessHeartbeatStore();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(store, PollInterval, StalenessThreshold, TimeSpan.FromMilliseconds(300)))
        {
            watcher.Subscribe(events);
            store.Upsert(CreateDescriptor("instance-a", incarnation: 1));
            await watcher.StartAsync();
            Assert.Equal(1, watcher.GraveyardEntryCount);

            store.Remove("instance-a");
            await Task.Delay(TimeSpan.FromMilliseconds(1500));

            // 窗口外：graveyard 记忆被轮询清理，字典归零。
            Assert.Equal(0, watcher.GraveyardEntryCount);

            store.Upsert(CreateDescriptor("instance-a", incarnation: 2));
            await Task.Delay(TimeSpan.FromMilliseconds(400));
            await watcher.StopAsync();

            var observed = events.Snapshot();
            Assert.Contains((RoleInstanceChangeKind.Online, "instance-a"), observed);
            Assert.Contains((RoleInstanceChangeKind.Evicted, "instance-a"), observed);
            Assert.DoesNotContain((RoleInstanceChangeKind.Offline, "instance-a"), observed);
            Assert.Equal(3, observed.Count);
            Assert.Equal(1, watcher.GraveyardEntryCount);
        }
    }

    /// <summary>
    /// 测试活跃实例永不过期：持续被观测的实例条目跨多个保留窗口仍在，不误发事件。
    /// </summary>
    [Fact]
    public async Task LiveInstance_ShouldNeverExpireAcrossRetentionWindows()
    {
        var store = new InProcessHeartbeatStore();
        var events = new RecordingInstanceEvents();
        using (var watcher = new DiscoveryWatcher(store, PollInterval, StalenessThreshold, TimeSpan.FromMilliseconds(300)))
        {
            watcher.Subscribe(events);
            store.Upsert(CreateDescriptor("instance-a", incarnation: 1));
            await watcher.StartAsync();
            await Task.Delay(TimeSpan.FromMilliseconds(1500));
            await watcher.StopAsync();

            Assert.Equal(1, watcher.GraveyardEntryCount);
            var observed = events.Snapshot();
            Assert.Single(observed);
            Assert.Equal((RoleInstanceChangeKind.Online, "instance-a"), observed[0]);
        }
    }

    /// <summary>
    /// 测试构造守卫：保留窗口非正数抛 ArgumentOutOfRangeException。
    /// </summary>
    [Fact]
    public void Constructor_WithNonPositiveRetention_ShouldThrow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryWatcher(new InProcessHeartbeatStore(), PollInterval, StalenessThreshold, TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DiscoveryWatcher(new InProcessHeartbeatStore(), PollInterval, StalenessThreshold, TimeSpan.FromSeconds(-1)));
    }

    /// <summary>
    /// 构造测试实例描述符。
    /// </summary>
    /// <param name="instanceId">实例标识 / The instance id</param>
    /// <param name="incarnation">代数 / The incarnation</param>
    /// <returns>描述符 / The descriptor</returns>
    private static InstanceDescriptor CreateDescriptor(string instanceId, long incarnation)
    {
        return new InstanceDescriptor("Game", instanceId, "tcp://127.0.0.1:7701", InstanceStatus.Active, 0, EndpointAddressKind.IPv4, incarnation, DateTime.UtcNow);
    }

    /// <summary>
    /// 进程内心跳存储替身（线程安全；poll 循环线程与用例线程并发访问）。
    /// </summary>
    /// <remarks>
    /// An in-process heartbeat store stub (thread-safe: the poll loop and the test
    /// thread access it concurrently). DeleteExpiredAsync is a no-op — row
    /// disappearance is driven by the test via <see cref="Remove"/>.
    /// </remarks>
    private sealed class InProcessHeartbeatStore : IHeartbeatStore
    {
        private readonly object _lock = new object();
        private readonly Dictionary<string, InstanceDescriptor> _rows = new Dictionary<string, InstanceDescriptor>(StringComparer.Ordinal);

        /// <inheritdoc />
        public Task EnsureSchemaAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task UpsertAsync(InstanceDescriptor instance, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _rows[instance.InstanceId] = instance;
            }

            return Task.CompletedTask;
        }

        /// <inheritdoc />
        public Task<IReadOnlyList<InstanceDescriptor>> QueryAllAsync(CancellationToken cancellationToken = default)
        {
            List<InstanceDescriptor> snapshot;
            lock (_lock)
            {
                snapshot = new List<InstanceDescriptor>(_rows.Values);
            }

            return Task.FromResult<IReadOnlyList<InstanceDescriptor>>(snapshot);
        }

        /// <inheritdoc />
        public Task DeleteExpiredAsync(TimeSpan heartbeatTimeToLive, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 写入一条观测（用例侧驱动）。
        /// </summary>
        public void Upsert(InstanceDescriptor instance)
        {
            UpsertAsync(instance).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 移除一条观测（模拟心跳行被 TTL 清除）。
        /// </summary>
        public void Remove(string instanceId)
        {
            lock (_lock)
            {
                _rows.Remove(instanceId);
            }
        }
    }

    /// <summary>
    /// 事件记录器（记录变化类别与实例标识）。
    /// </summary>
    private sealed class RecordingInstanceEvents : IRoleInstanceEvents
    {
        private readonly List<(RoleInstanceChangeKind Kind, string InstanceId)> _observed = new List<(RoleInstanceChangeKind, string)>();

        /// <inheritdoc />
        public void OnInstanceChanged(RoleInstanceChangeKind kind, InstanceDescriptor instance)
        {
            lock (_observed)
            {
                _observed.Add((kind, instance.InstanceId));
            }
        }

        /// <summary>
        /// 获取事件快照。
        /// </summary>
        public List<(RoleInstanceChangeKind Kind, string InstanceId)> Snapshot()
        {
            lock (_observed)
            {
                return new List<(RoleInstanceChangeKind, string)>(_observed);
            }
        }
    }
}
