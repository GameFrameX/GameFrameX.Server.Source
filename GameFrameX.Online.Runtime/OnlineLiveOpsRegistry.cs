// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Runtime;

/// <summary>
/// LiveOps 最小承载（change C122 决策⑧②：InMemory 版本登记表 + publish/rollback 语义）。
/// <para>
/// 维护约束：这是装配层对 Admin LiveOps 同步命令的**最小**承接（远端下发 / 灰度引擎不存在，
/// vault C8 G7-4 半边）——只做 append-only 登记与最新版本视图，不实现灰度分流、设备分群计算、
/// 定时触发等引擎语义（全量引擎另立 change）；登记表不持久化（进程内，随宿主生命周期）。
/// </para>
/// </summary>
public sealed class OnlineLiveOpsRegistry
{
    /// <summary>
    /// 同步锁（登记表为普通列表 + 字典，全部操作临界区内完成）。
    /// </summary>
    private readonly object _sync = new object();

    /// <summary>
    /// 登记表（append-only）。
    /// </summary>
    private readonly List<OnlineLiveOpsEntry> _entries = new List<OnlineLiveOpsEntry>();

    /// <summary>
    /// 最新条目索引（kind:key → 条目；回滚后指向回滚条目）。
    /// </summary>
    private readonly Dictionary<string, OnlineLiveOpsEntry> _latest = new Dictionary<string, OnlineLiveOpsEntry>(StringComparer.Ordinal);

    /// <summary>
    /// 登记序号分配器。
    /// </summary>
    private long _sequence;

    /// <summary>
    /// 发布配置版本（同 kind+key 重复发布即追加新条目并接管最新视图）。
    /// </summary>
    /// <param name="kind">配置种类（如 <c>RemoteConfig</c> / <c>ConfigRollout</c>）。</param>
    /// <param name="key">配置键（种类内唯一）。</param>
    /// <param name="version">版本号（调用方给定）。</param>
    /// <param name="payloadText">配置载荷原文（原样登记，供查询端点回读）。</param>
    /// <param name="operatorId">操作者。</param>
    /// <param name="correlationId">关联标识。</param>
    /// <returns>登记条目。</returns>
    public OnlineLiveOpsEntry Publish(string kind, string key, string version, string payloadText, string operatorId, string correlationId)
    {
        return Append(OnlineLiveOpsOperation.Publish, kind, key, version, payloadText, operatorId, correlationId);
    }

    /// <summary>
    /// 回滚配置版本（登记回滚条目并接管最新视图；不删除历史条目）。
    /// </summary>
    /// <param name="kind">配置种类。</param>
    /// <param name="key">配置键。</param>
    /// <param name="version">被回滚到的版本。</param>
    /// <param name="payloadText">回滚说明或目标版本载荷。</param>
    /// <param name="operatorId">操作者。</param>
    /// <param name="correlationId">关联标识。</param>
    /// <returns>登记条目。</returns>
    public OnlineLiveOpsEntry Rollback(string kind, string key, string version, string payloadText, string operatorId, string correlationId)
    {
        return Append(OnlineLiveOpsOperation.Rollback, kind, key, version, payloadText, operatorId, correlationId);
    }

    /// <summary>
    /// 同步实体（设备分群 / 定时任务 / 玩家分群等 upsert 语义：追加条目并接管最新视图）。
    /// </summary>
    /// <param name="kind">实体种类（如 <c>DeviceGroup</c> / <c>ScheduledTask</c> / <c>PlayerSegment</c> / <c>Announcement</c>）。</param>
    /// <param name="key">实体键。</param>
    /// <param name="payloadText">实体载荷原文。</param>
    /// <param name="operatorId">操作者。</param>
    /// <param name="correlationId">关联标识。</param>
    /// <returns>登记条目。</returns>
    public OnlineLiveOpsEntry Sync(string kind, string key, string payloadText, string operatorId, string correlationId)
    {
        return Append(OnlineLiveOpsOperation.Sync, kind, key, string.Empty, payloadText, operatorId, correlationId);
    }

    /// <summary>
    /// 查询最新条目。
    /// </summary>
    /// <param name="kind">配置种类。</param>
    /// <param name="key">配置键。</param>
    /// <returns>最新条目；无登记返回 null。</returns>
    public OnlineLiveOpsEntry FindLatest(string kind, string key)
    {
        lock (_sync)
        {
            _latest.TryGetValue(BuildKey(kind, key), out var entry);
            return entry;
        }
    }

    /// <summary>
    /// 按种类列出登记条目（时间倒序，最新在前）。
    /// </summary>
    /// <param name="kind">配置种类（空串表示不限种类）。</param>
    /// <param name="maxCount">最多返回条数。</param>
    /// <returns>条目列表。</returns>
    public IReadOnlyList<OnlineLiveOpsEntry> List(string kind, int maxCount)
    {
        lock (_sync)
        {
            var source = string.IsNullOrEmpty(kind)
                ? _entries
                : _entries.Where(entry => string.Equals(entry.Kind, kind, StringComparison.Ordinal)).ToList();
            return source.OrderByDescending(entry => entry.Sequence).Take(Math.Max(1, maxCount)).ToList();
        }
    }

    /// <summary>
    /// 追加条目并接管最新视图。
    /// </summary>
    /// <param name="operation">登记操作。</param>
    /// <param name="kind">配置种类。</param>
    /// <param name="key">配置键。</param>
    /// <param name="version">版本号。</param>
    /// <param name="payloadText">载荷原文。</param>
    /// <param name="operatorId">操作者。</param>
    /// <param name="correlationId">关联标识。</param>
    /// <returns>登记条目。</returns>
    private OnlineLiveOpsEntry Append(OnlineLiveOpsOperation operation, string kind, string key, string version, string payloadText, string operatorId, string correlationId)
    {
        var entry = new OnlineLiveOpsEntry
        {
            Sequence = System.Threading.Interlocked.Increment(ref _sequence),
            Operation = operation,
            Kind = kind ?? string.Empty,
            Key = key ?? string.Empty,
            Version = version ?? string.Empty,
            PayloadText = payloadText ?? string.Empty,
            OperatorId = operatorId ?? string.Empty,
            CorrelationId = correlationId ?? string.Empty,
            CreatedAtTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        lock (_sync)
        {
            _entries.Add(entry);
            _latest[BuildKey(entry.Kind, entry.Key)] = entry;
        }

        return entry;
    }

    /// <summary>
    /// 构造最新视图索引键。
    /// </summary>
    /// <param name="kind">配置种类。</param>
    /// <param name="key">配置键。</param>
    /// <returns>索引键。</returns>
    private static string BuildKey(string kind, string key)
    {
        return (kind ?? string.Empty) + ":" + (key ?? string.Empty);
    }
}

