// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
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

using System.Collections.Concurrent;
using System.Net.Sockets;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// TCP 长连接提供器。管理单条 TCP 连接的创建、复用和释放。
/// </summary>
/// <remarks>
/// TCP persistent connection provider. Manages the creation, reuse, and disposal of a single TCP connection.
/// </remarks>
internal sealed class TcpConnectionProvider : IConnectionProvider
{
    private const int DefaultTimeoutMs = 5000;

    /// <summary>
    /// 每个 endpoint 的默认连接池大小。
    /// </summary>
    private const int DefaultPoolSizePerEndpoint = 4;

    private readonly int _maxConnectionsPerEndpoint;
    private readonly ConcurrentDictionary<string, EndpointPool> _endpointPools = new();
    private bool _disposed;

    /// <summary>
    /// 初始化 TCP 连接提供器。
    /// </summary>
    /// <remarks>
    /// Initializes the TCP connection provider.
    /// </remarks>
    /// <param name="maxConnectionsPerEndpoint">每个 endpoint 的最大连接数 / Maximum connections per endpoint</param>
    public TcpConnectionProvider(int maxConnectionsPerEndpoint = DefaultPoolSizePerEndpoint)
    {
        if (maxConnectionsPerEndpoint < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(maxConnectionsPerEndpoint));
        }

        _maxConnectionsPerEndpoint = maxConnectionsPerEndpoint;
    }

    /// <summary>
    /// 获取或创建到指定目标的可用网络流。
    /// 同一 endpoint 维护最多 <c>maxConnectionsPerEndpoint</c> 条连接并轮询分发；
    /// 不同 endpoint 各自独立成池，互不挤占。
    /// </summary>
    /// <remarks>
    /// Gets an existing or creates a new usable network stream to the specified target.
    /// Each endpoint maintains up to <c>maxConnectionsPerEndpoint</c> connections with round-robin distribution;
    /// different endpoints keep independent pools.
    /// </remarks>
    /// <param name="host">目标主机 / Target host</param>
    /// <param name="port">目标端口 / Target port</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>可用的网络流 / An available network stream</returns>
    public async Task<Stream> GetOrCreateStreamAsync(string host, int port, CancellationToken cancellationToken = default)
    {
        var pool = _endpointPools.GetOrAdd($"{host}:{port}", _ => new EndpointPool());
        return await pool.GetOrCreateStreamAsync(host, port, _maxConnectionsPerEndpoint, cancellationToken);
    }

    /// <summary>
    /// 标记全部连接为失效，下次调用时重建。
    /// </summary>
    /// <remarks>
    /// Marks all connections as invalid so they will be re-established on the next call.
    /// </remarks>
    public void Invalidate()
    {
        foreach (var pool in _endpointPools.Values)
        {
            pool.DisposeAll();
        }
    }

    /// <summary>
    /// 标记指定流所属连接为失效（仅移除该连接，其余连接与其它 endpoint 的池不受影响）。
    /// </summary>
    /// <remarks>
    /// Marks the connection owning the specified stream as invalid (removes only that connection,
    /// leaving other connections and other endpoint pools untouched).
    /// </remarks>
    /// <param name="stream">要失效的连接流 / The connection stream to invalidate</param>
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
    /// 释放全部 TCP 连接和关联资源。
    /// </summary>
    /// <remarks>
    /// Releases all TCP connections and associated resources.
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
    /// 单个 endpoint 的连接池。池内信号量串行化连接建立，轮询索引分发连接。
    /// </summary>
    /// <remarks>
    /// Connection pool for a single endpoint. The in-pool semaphore serializes connection setup,
    /// and a round-robin index distributes connections.
    /// </remarks>
    private sealed class EndpointPool : IDisposable
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private readonly List<ConnectionEntry> _entries = new();
        private int _roundRobinIndex;
        private int _inFlight;

        public async Task<Stream> GetOrCreateStreamAsync(string host, int port, int maxConnections, CancellationToken cancellationToken)
        {
            // 并发需求驱动池增长：仅当存活连接数低于「池上限与当前并发获取数的较小值」时新建。
            // 顺序调用保持单连接复用；并发调用按需扩展连接数。
            Interlocked.Increment(ref _inFlight);
            try
            {
                // gate 串行化连接建立（创建期间不持任何 lock，await 不可入 lock）；
                // _entries 的一切读写均在 lock 内短临界区完成，与 TryRemove/DisposeAll 互斥
                await _gate.WaitAsync(cancellationToken);
                try
                {
                    RemoveDeadEntries();

                    // 池未满足当前并发需求则新建连接（懒建：随并发调用逐步扩到池上限）
                    if (CountEntries() < Math.Min(maxConnections, Volatile.Read(ref _inFlight)))
                    {
                        var created = await CreateConnectionAsync(host, port, cancellationToken);
                        lock (_entries)
                        {
                            _entries.Add(created);
                        }
                    }

                    ConnectionEntry picked;
                    lock (_entries)
                    {
                        picked = _entries[(_roundRobinIndex++) % _entries.Count];
                    }

                    if (!picked.IsAlive)
                    {
                        // 轮询命中已死亡连接（未到清理周期的兜底）：原位替换
                        var replacement = await CreateConnectionAsync(host, port, cancellationToken);
                        lock (_entries)
                        {
                            var index = _entries.IndexOf(picked);
                            if (index >= 0)
                            {
                                _entries[index] = replacement;
                            }
                            else
                            {
                                _entries.Add(replacement);
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
                Interlocked.Decrement(ref _inFlight);
            }
        }

        private void RemoveDeadEntries()
        {
            List<ConnectionEntry> dead = null;
            lock (_entries)
            {
                for (var i = _entries.Count - 1; i >= 0; i--)
                {
                    if (!_entries[i].IsAlive)
                    {
                        dead ??= new List<ConnectionEntry>();
                        dead.Add(_entries[i]);
                        _entries.RemoveAt(i);
                    }
                }
            }

            // 释放动作（Dispose 触发流关闭）移出 lock：避免与读泵阻塞读交互拉长临界区
            dead?.ForEach(entry => entry.Dispose());
        }

        private int CountEntries()
        {
            lock (_entries)
            {
                return _entries.Count;
            }
        }

        public bool TryRemove(Stream stream)
        {
            lock (_entries)
            {
                for (var i = 0; i < _entries.Count; i++)
                {
                    if (ReferenceEquals(_entries[i].Stream, stream))
                    {
                        var entry = _entries[i];
                        _entries.RemoveAt(i);
                        entry.Dispose();
                        return true;
                    }
                }
            }

            return false;
        }

        public void DisposeAll()
        {
            lock (_entries)
            {
                foreach (var entry in _entries)
                {
                    entry.Dispose();
                }

                _entries.Clear();
            }
        }

        public void Dispose()
        {
            DisposeAll();
            _gate.Dispose();
        }

        private static async Task<ConnectionEntry> CreateConnectionAsync(string host, int port, CancellationToken cancellationToken)
        {
            var client = new TcpClient
            {
                NoDelay = true,
                ReceiveTimeout = DefaultTimeoutMs,
                SendTimeout = DefaultTimeoutMs,
            };

            try
            {
                await client.ConnectAsync(host, port, cancellationToken);
                return new ConnectionEntry(client);
            }
            catch
            {
                client.Dispose();
                throw;
            }
        }
    }

    /// <summary>
    /// 池内单条 TCP 连接。
    /// </summary>
    /// <remarks>
    /// A single pooled TCP connection.
    /// </remarks>
    private sealed class ConnectionEntry : IDisposable
    {
        private readonly TcpClient _tcpClient;

        public ConnectionEntry(TcpClient tcpClient)
        {
            _tcpClient = tcpClient;
            Stream = tcpClient.GetStream();
        }

        public Stream Stream { get; }

        public bool IsAlive
        {
            get { return _tcpClient.Connected && Stream != null; }
        }

        public void Dispose()
        {
            Stream?.Dispose();
            _tcpClient?.Dispose();
        }
    }
}