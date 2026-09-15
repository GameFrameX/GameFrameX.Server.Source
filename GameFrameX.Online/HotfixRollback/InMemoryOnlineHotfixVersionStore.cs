// ==========================================================================================
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
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
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
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 版本清单内存存储（单进程默认实现；生产持久化实现归运行时装配，X4）。
/// <para>
/// 维护约束：「登记判重 + 落档」与「活跃版本切换」在 <see cref="_gate"/> 锁内**同一临界区**完成
/// （并发登记与切换不撕裂）；作用域索引键为 (TenantId, AppId) 两键；出入参防御性拷贝
/// （持久化行语义，调用方引用不得别名存储内部状态）。
/// </para>
/// <para>
/// 天花板（ponytail）：进程内字典，重启即丢（活跃版本与已登记清单全部丢失）——
/// 升级路径 = 运行时装配提供持久化实现（X4，对齐 InMemoryOnlineAuditStore 同款边界），
/// 装配层须在启动时以 HotfixManager 实际加载版本对账活跃指针。
/// </para>
/// </summary>
public sealed class InMemoryOnlineHotfixVersionStore : IOnlineHotfixVersionStore
{
    /// <summary>
    /// 全局状态门（登记判重、落档与活跃切换的同一临界区）。
    /// </summary>
    private readonly object _gate = new object();

    /// <summary>
    /// 作用域级状态索引（键 = "TenantId|AppId"；值为作用域内的版本登记簿与活跃指针）。
    /// </summary>
    private readonly Dictionary<string, ScopeState> _statesByScope = new Dictionary<string, ScopeState>(StringComparer.Ordinal);

    /// <summary>
    /// 登记一份版本协议清单（锁内判重 + 落档同一临界区；重复登记不覆盖）。
    /// </summary>
    /// <param name="manifest">待登记清单。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>新登记返回 <c>true</c>；同 (作用域, 版本号) 已存在返回 <c>false</c>。</returns>
    public Task<bool> RegisterAsync(OnlineHotfixProtocolManifest manifest, CancellationToken cancellationToken = default)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        lock (_gate)
        {
            var state = GetOrCreateState(manifest.TenantId, manifest.AppId);
            if (state.Manifests.ContainsKey(manifest.Version))
            {
                return Task.FromResult(false);
            }

            state.Manifests[manifest.Version] = CloneManifest(manifest);
            return Task.FromResult(true);
        }
    }

    /// <summary>
    /// 按版本号查找清单（快照拷贝）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="version">版本号。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>清单快照；未登记返回 <see langword="null"/>。</returns>
    public Task<OnlineHotfixProtocolManifest> FindAsync(long tenantId, long appId, string version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var state = FindState(tenantId, appId);
            if (state == null || !state.Manifests.TryGetValue(version, out var manifest))
            {
                return Task.FromResult<OnlineHotfixProtocolManifest>(null);
            }

            return Task.FromResult(CloneManifest(manifest));
        }
    }

    /// <summary>
    /// 查找当前活跃版本清单（快照拷贝）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>活跃清单快照；从未激活返回 <see langword="null"/>。</returns>
    public Task<OnlineHotfixProtocolManifest> FindActiveAsync(long tenantId, long appId, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var state = FindState(tenantId, appId);
            if (state == null || string.IsNullOrEmpty(state.ActiveVersion) || !state.Manifests.TryGetValue(state.ActiveVersion, out var manifest))
            {
                return Task.FromResult<OnlineHotfixProtocolManifest>(null);
            }

            return Task.FromResult(CloneManifest(manifest));
        }
    }

    /// <summary>
    /// 把活跃版本切换到已登记的目标版本（锁内完成；返回切换前活跃版本号）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="version">目标版本号（须已登记）。</param>
    /// <param name="cancellationToken">取消令牌（内存实现无异步等待，令牌仅契约对齐）。</param>
    /// <returns>切换前的活跃版本号（从未激活返回空字符串）。</returns>
    public Task<string> SetActiveAsync(long tenantId, long appId, string version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            var state = GetOrCreateState(tenantId, appId);
            if (!state.Manifests.ContainsKey(version))
            {
                throw new InvalidOperationException("目标版本未登记，拒绝切换活跃版本：" + version);
            }

            var previous = state.ActiveVersion ?? string.Empty;
            state.ActiveVersion = version;
            return Task.FromResult(previous);
        }
    }

    /// <summary>
    /// 查找作用域状态（不在锁内新建）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <returns>作用域状态；作用域无任何登记返回 <see langword="null"/>。</returns>
    private ScopeState FindState(long tenantId, long appId)
    {
        var scopeKey = BuildScopeKey(tenantId, appId);
        return _statesByScope.TryGetValue(scopeKey, out var state) ? state : null;
    }

    /// <summary>
    /// 获取或创建作用域状态（调用方须持 <see cref="_gate"/>）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <returns>作用域状态。</returns>
    private ScopeState GetOrCreateState(long tenantId, long appId)
    {
        var scopeKey = BuildScopeKey(tenantId, appId);
        if (!_statesByScope.TryGetValue(scopeKey, out var state))
        {
            state = new ScopeState();
            _statesByScope[scopeKey] = state;
        }

        return state;
    }

    /// <summary>
    /// 构造作用域索引键（"TenantId|AppId"）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <returns>作用域键。</returns>
    private static string BuildScopeKey(long tenantId, long appId)
    {
        return tenantId.ToString(CultureInfo.InvariantCulture) + "|" + appId.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 深拷贝清单（含消息契约行集合；持久化行语义——出入参不与存储内部状态别名）。
    /// </summary>
    /// <param name="manifest">源清单。</param>
    /// <returns>可独立变更的拷贝。</returns>
    private static OnlineHotfixProtocolManifest CloneManifest(OnlineHotfixProtocolManifest manifest)
    {
        var messages = new List<OnlineHotfixProtocolMessage>(manifest.Messages?.Count ?? 0);
        foreach (var message in manifest.Messages ?? Array.Empty<OnlineHotfixProtocolMessage>())
        {
            if (message == null)
            {
                continue;
            }

            messages.Add(new OnlineHotfixProtocolMessage
            {
                MessageName = message.MessageName,
                MessageId = message.MessageId,
            });
        }

        return new OnlineHotfixProtocolManifest
        {
            Version = manifest.Version,
            TenantId = manifest.TenantId,
            AppId = manifest.AppId,
            Messages = messages,
            RegisteredTime = manifest.RegisteredTime,
        };
    }

    /// <summary>
    /// 作用域级状态（版本登记簿 + 活跃版本指针）。
    /// </summary>
    private sealed class ScopeState
    {
        /// <summary>
        /// 版本登记簿（版本号 → 清单；一经登记不可变更）。
        /// </summary>
        public Dictionary<string, OnlineHotfixProtocolManifest> Manifests
        {
            get;
        } = new Dictionary<string, OnlineHotfixProtocolManifest>(StringComparer.Ordinal);

        /// <summary>
        /// 活跃版本号（空字符串 / null 表示从未激活）。
        /// </summary>
        public string ActiveVersion
        {
            get;
            set;
        } = string.Empty;
    }
}
