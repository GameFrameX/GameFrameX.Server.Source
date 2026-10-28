// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// KCP 会话连接提供器：镜像 TCP 连接池语义（每 endpoint 懒建有限会话、轮询分发、
/// 信号量串行建连、死亡原位替换），在此基础上叠加陈旧会话淘汰。
/// </summary>
/// <remarks>
/// KCP session connection provider: mirrors the TCP pool semantics (lazily grown bounded sessions
/// per endpoint, round-robin distribution, semaphore-serialized setup, in-place replacement of
/// dead entries) and adds stale-session eviction on top.
/// <para>陈旧淘汰时序不变量（与 StartUp 服务器间 KCP 监听的 idle 清理窗 300s 配对）：
/// 客户端淘汰阈 240s 严格小于服务端清理窗——客户端永不复用服务端可能已清理的 conv
/// （服务端清理后同 conv 重建会遭遇 KCP 序列号状态错位，表现为静默卡死）。</para>
/// </remarks>
internal sealed class KcpConnectionProvider : IConnectionProvider
{
    /// <summary>
    /// 每个 endpoint 的默认会话池大小（与 TCP 连接池一致）。
    /// </summary>
    private const int DefaultPoolSizePerEndpoint = 4;

    /// <summary>
    /// 陈旧会话淘汰阈（毫秒）：空闲超过该窗的会话按死亡处理并重建；
    /// 必须小于服务端监听的 idle 清理窗（见类备注的时序不变量）。
    /// </summary>
    internal const int StaleSessionIdleMilliseconds = 240_000;

    private readonly int _maxConnectionsPerEndpoint;
    private readonly ConcurrentDictionary<string, KcpEndpointPool> _endpointPools = new();
    private bool _disposed;

    /// <summary>
    /// 初始化 KCP 会话连接提供器。
    /// </summary>
    /// <remarks>
    /// Initializes the KCP session connection provider.
    /// </remarks>
    /// <param name="maxConnectionsPerEndpoint">每个 endpoint 的最大会话数 / Maximum sessions per endpoint</param>
    /// <exception cref="ArgumentOutOfRangeException">当 <paramref name="maxConnectionsPerEndpoint"/> 小于 1 时抛出 / Thrown when it is less than 1</exception>
    public KcpConnectionProvider(int maxConnectionsPerEndpoint = DefaultPoolSizePerEndpoint)
    {
        if (maxConnectionsPerEndpoint < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnectionsPerEndpoint));
        }

        _maxConnectionsPerEndpoint = maxConnectionsPerEndpoint;
    }

    /// <summary>
    /// 获取或创建到指定目标的可用 KCP 会话流。
    /// 同一 endpoint 维护最多 <c>maxConnectionsPerEndpoint</c> 个会话并轮询分发；不同 endpoint 各自独立成池。
    /// </summary>
    /// <remarks>
    /// Gets an existing or creates a new usable KCP session stream to the specified target.
    /// Each endpoint maintains up to <c>maxConnectionsPerEndpoint</c> sessions with round-robin
    /// distribution; different endpoints keep independent pools.
    /// </remarks>
    /// <param name="host">目标主机 / Target host</param>
    /// <param name="port">目标端口 / Target port</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>可用的会话流 / A usable session stream</returns>
    public async Task<Stream> GetOrCreateStreamAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var pool = _endpointPools.GetOrAdd($"{host}:{port}", _ => new KcpEndpointPool());
        return await pool.GetOrCreateStreamAsync(host, port, _maxConnectionsPerEndpoint, cancellationToken);
    }

    /// <summary>
    /// 标记全部会话为失效，下次调用时重建。
    /// </summary>
    /// <remarks>
    /// Marks all sessions invalid so they are re-established on the next call.
    /// </remarks>
    public void Invalidate()
    {
        foreach (var pool in _endpointPools.Values)
        {
            pool.DisposeAll();
        }
    }

    /// <summary>
    /// 标记指定流所属会话为失效（仅移除该会话，其余会话与其它 endpoint 的池不受影响）。
    /// </summary>
    /// <remarks>
    /// Marks the session owning the specified stream invalid (removes only that session,
    /// leaving other sessions and other endpoint pools untouched).
    /// </remarks>
    /// <param name="stream">要失效的会话流 / The session stream to invalidate</param>
    public void Invalidate(Stream stream)
    {
        foreach (var pool in _endpointPools.Values)
        {
            if (pool.TryRemove(stream))
            {
                return;
            }
        }
    }

    /// <summary>
    /// 释放全部 KCP 会话和关联资源。
    /// </summary>
    /// <remarks>
    /// Releases all KCP sessions and associated resources.
    /// </remarks>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Invalidate();
    }

    /// <summary>
    /// 单个 endpoint 的 KCP 会话池。池内信号量串行化会话建立，轮询索引分发会话。
    /// </summary>
    /// <remarks>
    /// KCP session pool for a single endpoint. The in-pool semaphore serializes session setup,
    /// and a round-robin index distributes sessions.
    /// </remarks>
    private sealed class KcpEndpointPool : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<KcpClientSession> _sessions = new();
        private int _roundRobinIndex;
        private int _pendingAcquisitions;

        public async Task<Stream> GetOrCreateStreamAsync(string host, int port, int maxSessions, CancellationToken cancellationToken)
        {
            // 排队需求驱动池增长：扩容信号是「gate 等待队列深度」而非获取动作的时间重叠——
            // 先后到达的并发获取在旧 _inFlight 判据下互不可见时池退化为单会话。
            // 顺序调用无排队 → 永不扩容（守住单会话复用）；并发获取必有 gate 排队 → 确定性扩容，与建连快慢无关。
            Interlocked.Increment(ref _pendingAcquisitions);
            try
            {
                // gate 串行化会话建立（创建期间不持任何 lock，await 不可入 lock）；
                // _sessions 的一切读写均在 lock 内短临界区完成，与 TryRemove/DisposeAll 互斥
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    RemoveDeadEntries();

                    var aliveCount = CountSessions();
                    // 等待数排除当前持 gate 者自身；gate 串行化建连，同一时刻只有一个持 gate 者判断扩容
                    var waitingCount = Volatile.Read(ref _pendingAcquisitions) - 1;
                    if (aliveCount == 0 || (aliveCount < maxSessions && waitingCount > 0))
                    {
                        var created = await CreateSessionAsync(host, port, cancellationToken);
                        lock (_sessions)
                        {
                            _sessions.Add(created);
                        }
                    }

                    KcpClientSession picked;
                    lock (_sessions)
                    {
                        picked = _sessions[(_roundRobinIndex++) % _sessions.Count];
                    }

                    if (!IsUsable(picked))
                    {
                        // 轮询命中已死亡/陈旧会话（未到清理周期的兜底）：原位替换为新 conv 会话
                        var replacement = await CreateSessionAsync(host, port, cancellationToken);
                        lock (_sessions)
                        {
                            var index = _sessions.IndexOf(picked);
                            if (index >= 0)
                            {
                                _sessions[index] = replacement;
                            }
                            else
                            {
                                _sessions.Add(replacement);
                            }
                        }

                        picked.Dispose();
                        picked = replacement;
                    }

                    return picked.Stream;
                }
                finally
                {
                    _gate.Release();
                }
            }
            finally
            {
                Interlocked.Decrement(ref _pendingAcquisitions);
            }
        }

        /// <summary>
        /// 会话可用性 = 存活 且 空闲未超陈旧淘汰阈（陈旧会话的服务端对端可能已被 idle 清理）。
        /// </summary>
        private static bool IsUsable(KcpClientSession session)
        {
            return session.IsAlive && DateTimeOffset.UtcNow - session.LastActiveTime < TimeSpan.FromMilliseconds(StaleSessionIdleMilliseconds);
        }

        private void RemoveDeadEntries()
        {
            List<KcpClientSession> dead = null;
            lock (_sessions)
            {
                for (var i = _sessions.Count - 1; i >= 0; i--)
                {
                    if (!IsUsable(_sessions[i]))
                    {
                        dead ??= new List<KcpClientSession>();
                        dead.Add(_sessions[i]);
                        _sessions.RemoveAt(i);
                    }
                }
            }

            // 释放动作（Dispose 触发 UDP Socket 关闭）移出 lock：避免拉长临界区
            dead?.ForEach(session => session.Dispose());
        }

        private int CountSessions()
        {
            lock (_sessions)
            {
                return _sessions.Count;
            }
        }

        public bool TryRemove(Stream stream)
        {
            lock (_sessions)
            {
                for (var i = 0; i < _sessions.Count; i++)
                {
                    if (ReferenceEquals(_sessions[i].Stream, stream))
                    {
                        var session = _sessions[i];
                        _sessions.RemoveAt(i);
                        session.Dispose();
                        return true;
                    }
                }
            }

            return false;
        }

        public void DisposeAll()
        {
            lock (_sessions)
            {
                foreach (var session in _sessions)
                {
                    session.Dispose();
                }

                _sessions.Clear();
            }
        }

        public void Dispose()
        {
            DisposeAll();
            _gate.Dispose();
        }

        /// <summary>
        /// 建立新 KCP 会话：解析主机地址族后按 conv 分配规则创建独占 UDP 会话。
        /// </summary>
        private static async Task<KcpClientSession> CreateSessionAsync(string host, int port, CancellationToken cancellationToken)
        {
            var remoteEndPoint = await ResolveRemoteEndPointAsync(host, port, cancellationToken);
            return new KcpClientSession(remoteEndPoint);
        }

        /// <summary>
        /// 解析远端端点（域名 → 地址族匹配的首个地址；IP 字面量由 GetHostAddresses 直返）。
        /// </summary>
        private static async Task<IPEndPoint> ResolveRemoteEndPointAsync(string host, int port, CancellationToken cancellationToken)
        {
            var addresses = await Dns.GetHostAddressesAsync(host, cancellationToken);
            var address = addresses.Length > 0 ? addresses[0] : throw new SocketException((int)SocketError.HostNotFound);
            return new IPEndPoint(address, port);
        }
    }
}
