// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//   This project is dual-licensed under the MIT License and Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等违法行为！
//   or infringe upon the legitimate rights and interest of others, as prohibited by laws and regulations!
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
using GameFrameX.Foundation.Logger;

namespace GameFrameX.Idempotency;

public sealed class ConnectionDedupWindow
{
    private readonly ConcurrentDictionary<(int MessageId, int UniqueId), DedupEntry> _entries = new();
    private readonly int _maxCapacity;
    private readonly TimeSpan _entryTtl;

    public ConnectionDedupWindow(int maxCapacity = 10000, int entryTtlSeconds = 30)
    {
        _maxCapacity = maxCapacity;
        _entryTtl = TimeSpan.FromSeconds(entryTtlSeconds);
    }

    public bool TryMarkSeen(int messageId, int uniqueId)
    {
        var key = (messageId, uniqueId);
        var now = DateTime.UtcNow;

        if (_entries.TryGetValue(key, out var existing))
        {
            if (now - existing.CreatedAt < _entryTtl)
            {
                return true;
            }

            _entries.TryRemove(key, out _);
        }

        EvictIfNeeded();

        var entry = new DedupEntry { CreatedAt = now };
        return !_entries.TryAdd(key, entry);
    }

    public void CacheRpcResponse(int messageId, int uniqueId, byte[] responseData)
    {
        var key = (messageId, uniqueId);
        var now = DateTime.UtcNow;

        _entries.AddOrUpdate(key,
            _ => new DedupEntry { CreatedAt = now, ResponseData = responseData },
            (_, existing) => new DedupEntry { CreatedAt = existing.CreatedAt, ResponseData = responseData });
    }

    public bool TryGetCachedResponse(int messageId, int uniqueId, out byte[] responseData)
    {
        responseData = null;

        if (_entries.TryGetValue((messageId, uniqueId), out var entry))
        {
            if (DateTime.UtcNow - entry.CreatedAt < _entryTtl && entry.ResponseData != null)
            {
                responseData = entry.ResponseData;
                return true;
            }
        }

        return false;
    }

    public int CleanupExpired()
    {
        var now = DateTime.UtcNow;
        var count = 0;

        foreach (var kvp in _entries)
        {
            if (now - kvp.Value.CreatedAt >= _entryTtl)
            {
                if (_entries.TryRemove(kvp.Key, out _))
                {
                    count++;
                }
            }
        }

        return count;
    }

    private void EvictIfNeeded()
    {
        if (_entries.Count < _maxCapacity)
        {
            return;
        }

        var oldestKey = default((int MessageId, int UniqueId));
        var oldestTime = DateTime.MaxValue;

        foreach (var kvp in _entries)
        {
            if (kvp.Value.CreatedAt < oldestTime)
            {
                oldestTime = kvp.Value.CreatedAt;
                oldestKey = kvp.Key;
            }
        }

        if (_entries.TryRemove(oldestKey, out _))
        {
            LogHelper.Warning("ConnectionDedupWindow.EvictIfNeeded, Evicted oldest entry due to capacity limit, key: {messageId}/{uniqueId}", oldestKey.MessageId, oldestKey.UniqueId);
        }
    }

    private sealed class DedupEntry
    {
        public DateTime CreatedAt { get; init; }
        public byte[] ResponseData { get; set; }
    }
}
