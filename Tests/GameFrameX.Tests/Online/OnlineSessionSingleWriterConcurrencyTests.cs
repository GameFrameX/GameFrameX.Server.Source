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
//   or to infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
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
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Foundation.Identity;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 会话状态单写者并发回归（GFX-1091）：SessionManager 与 TokenService 并发写同一会话时，
    /// 共享写入串行门保证两方效果（State 推进 × Token 轮换）均不丢失。
    /// </summary>
    public class OnlineSessionSingleWriterConcurrencyTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用区服标识。</summary>
        private const long ServerId = 100;

        /// <summary>测试用玩家标识。</summary>
        private const long PlayerId = 1001;

        /// <summary>
        /// 验证并发「建连标记 × Token 刷新」在拷贝语义 store（读写均返回/落库独立副本，
        /// 放大「读—改—全量写」竞态）下，最终会话同时保有 State=Connected 与刷新后的 Token 指纹。
        /// </summary>
        [Fact]
        public async Task ConcurrentMarkConnectedAndRefresh_BothWritesSurvive()
        {
            for (var attempt = 0; attempt < 50; attempt++)
            {
                var recorder = new OnlineEventRecorder();
                var store = new CopyingOnlineSessionStore();
                var writeGate = new OnlineSessionWriteGate();
                var sessions = new OnlineSessionManager(store, recorder, writeGate);
                var tokens = new OnlineSessionTokenService(store, recorder, sessions, writeGate);
                var scope = new OnlineScope(TenantId, AppId, ServerId, PlayerId);

                var issued = await tokens.IssueSessionAsync(scope, 3600, OnlineMultiDevicePolicy.Coexist, null);
                Assert.True(issued.IsSuccess);
                var sessionId = issued.Data.Session.Id;

                var connectTask = sessions.MarkConnectedAsync(sessionId, "conn-" + attempt);
                var refreshTask = tokens.RefreshSessionTokenAsync(scope, issued.Data.Token, null);
                await Task.WhenAll(connectTask, refreshTask);

                Assert.True(connectTask.Result.IsSuccess, "建连标记不应失败（第 " + attempt + " 轮）");
                Assert.True(refreshTask.Result.IsSuccess, "Token 刷新不应失败（第 " + attempt + " 轮）");

                var finalSession = await store.FindAsync(sessionId);
                Assert.NotNull(finalSession);
                Assert.Equal(OnlineSessionState.Connected, finalSession.State);
                Assert.Equal(2, finalSession.TokenGeneration);
                Assert.Equal(OnlineSessionCloseReason.None, finalSession.CloseReason);

                var revalidated = await tokens.ValidateTokenAsync(refreshTask.Result.Data.Token);
                Assert.True(revalidated.IsSuccess, "刷新后的新 Token 必须仍可验证（指纹未被建连写回滚）");
            }
        }

        /// <summary>
        /// 拷贝语义会话存储（<see cref="IOnlineSessionStore"/> 测试替身）：每次读返回深拷贝、每次写落独立副本——
        /// 模拟持久化存储的全量行覆盖语义，使「读—改—全量写」丢更新竞态可被测试观测
        /// （共享引用的 <see cref="InMemoryOnlineSessionStore"/> 观测不到该竞态）。
        /// </summary>
        private sealed class CopyingOnlineSessionStore : IOnlineSessionStore
        {
            /// <summary>会话表：键 = SessionId。</summary>
            private readonly Dictionary<string, OnlineSession> _sessions = new();

            /// <summary>Token 指纹索引：键 = TokenHash，值 = SessionId。</summary>
            private readonly Dictionary<string, string> _tokenIndex = new();

            /// <summary>各会话当前已入册的指纹（索引维护事实源）。</summary>
            private readonly Dictionary<string, string> _indexedHashBySessionId = new();

            /// <summary>同步根。</summary>
            private readonly object _syncRoot = new();


            /// <summary>并发计数（本桩不承载该路径，返回空字典）。</summary>
            public Task<IReadOnlyDictionary<OnlineSessionState, int>> CountNonTerminalByScopeAsync(long tenantId, long appId, long serverId, CancellationToken cancellationToken = default)
            {
                return Task.FromResult<IReadOnlyDictionary<OnlineSessionState, int>>(new Dictionary<OnlineSessionState, int>());
            }
            /// <summary>写入或覆盖会话（落独立副本并同步维护指纹索引）。</summary>
            public Task AddOrUpdateAsync(OnlineSession session, CancellationToken cancellationToken = default)
            {
                lock (_syncRoot)
                {
                    if (_indexedHashBySessionId.TryGetValue(session.Id, out var indexedHash) && !string.IsNullOrEmpty(indexedHash))
                    {
                        _tokenIndex.Remove(indexedHash);
                    }

                    var stored = Clone(session);
                    _sessions[session.Id] = stored;
                    _indexedHashBySessionId[session.Id] = stored.CurrentTokenHash ?? string.Empty;
                    if (!string.IsNullOrEmpty(stored.CurrentTokenHash))
                    {
                        _tokenIndex[stored.CurrentTokenHash] = stored.Id;
                    }
                }

                return Task.CompletedTask;
            }

            /// <summary>按会话标识查找（返回副本）。</summary>
            public Task<OnlineSession> FindAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                lock (_syncRoot)
                {
                    _sessions.TryGetValue(sessionId, out var session);
                    return Task.FromResult(session == null ? null : Clone(session));
                }
            }

            /// <summary>按 Token 指纹查找会话（返回副本）。</summary>
            public Task<OnlineSession> FindByTokenHashAsync(string tokenHash, CancellationToken cancellationToken = default)
            {
                lock (_syncRoot)
                {
                    if (tokenHash == null || !_tokenIndex.TryGetValue(tokenHash, out var sessionId))
                    {
                        return Task.FromResult<OnlineSession>(null);
                    }

                    _sessions.TryGetValue(sessionId, out var session);
                    return Task.FromResult(session == null ? null : Clone(session));
                }
            }

            /// <summary>列出玩家在 (TenantId, AppId) 下的非终态会话（返回副本）。</summary>
            public Task<IReadOnlyList<OnlineSession>> ListActiveByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default)
            {
                lock (_syncRoot)
                {
                    var result = new List<OnlineSession>();
                    foreach (var session in _sessions.Values)
                    {
                        if (session.TenantId == tenantId && session.AppId == appId && session.PlayerId == playerId && !session.State.IsTerminal())
                        {
                            result.Add(Clone(session));
                        }
                    }

                    return Task.FromResult<IReadOnlyList<OnlineSession>>(result);
                }
            }

            /// <summary>列出全部非终态会话（返回副本）。</summary>
            public Task<IReadOnlyList<OnlineSession>> ListNonTerminalAsync(CancellationToken cancellationToken = default)
            {
                lock (_syncRoot)
                {
                    var result = new List<OnlineSession>();
                    foreach (var session in _sessions.Values)
                    {
                        if (!session.State.IsTerminal())
                        {
                            result.Add(Clone(session));
                        }
                    }

                    return Task.FromResult<IReadOnlyList<OnlineSession>>(result);
                }
            }

            /// <summary>移除会话（同步清指纹索引）。</summary>
            public Task RemoveAsync(string sessionId, CancellationToken cancellationToken = default)
            {
                lock (_syncRoot)
                {
                    if (_sessions.Remove(sessionId) && _indexedHashBySessionId.Remove(sessionId, out var indexedHash))
                    {
                        if (!string.IsNullOrEmpty(indexedHash))
                        {
                            _tokenIndex.Remove(indexedHash);
                        }
                    }
                }

                return Task.CompletedTask;
            }

            /// <summary>深拷贝会话实体。</summary>
            private static OnlineSession Clone(OnlineSession source)
            {
                return new OnlineSession
                {
                    Id = source.Id,
                    TenantId = source.TenantId,
                    AppId = source.AppId,
                    ServerId = source.ServerId,
                    GameAccountId = source.GameAccountId,
                    PlayerId = source.PlayerId,
                    DeviceId = source.DeviceId,
                    ConnectionId = source.ConnectionId,
                    State = source.State,
                    MultiDevicePolicy = source.MultiDevicePolicy,
                    CurrentTokenHash = source.CurrentTokenHash,
                    TokenGeneration = source.TokenGeneration,
                    TokenTimeToLiveSeconds = source.TokenTimeToLiveSeconds,
                    TokenExpiresAtTime = source.TokenExpiresAtTime,
                    CreatedAtTime = source.CreatedAtTime,
                    AuthenticatedAtTime = source.AuthenticatedAtTime,
                    ConnectedAtTime = source.ConnectedAtTime,
                    LastHeartbeatAtTime = source.LastHeartbeatAtTime,
                    DisconnectAtTime = source.DisconnectAtTime,
                    ReconnectDeadlineAtTime = source.ReconnectDeadlineAtTime,
                    ClosedAtTime = source.ClosedAtTime,
                    CloseReason = source.CloseReason,
                };
            }
        }
    }
}
