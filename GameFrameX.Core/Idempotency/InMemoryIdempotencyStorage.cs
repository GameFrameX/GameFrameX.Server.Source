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

namespace GameFrameX.Core.Idempotency;

/// <summary>
/// 基于 ConcurrentDictionary 的内存幂等存储实现。
/// 以 (PlayerActorId, RequestId) 为复合键，全局一张表通过键前缀天然 per-player 隔离。
/// </summary>
public sealed class InMemoryIdempotencyStorage : IIdempotencyStorage
{
    private readonly ConcurrentDictionary<(long PlayerActorId, string RequestId), IdempotencyRecord> _records = new();

    public bool TryGet(long playerActorId, string requestId, out IdempotencyRecord record)
    {
        if (_records.TryGetValue((playerActorId, requestId), out var existing))
        {
            if (DateTime.UtcNow < existing.ExpiresAt)
            {
                record = existing;
                return true;
            }

            _records.TryRemove((playerActorId, requestId), out _);
        }

        record = null;
        return false;
    }

    public bool TryGetOrMark(long playerActorId, string requestId, out IdempotencyRecord existingRecord)
    {
        var now = DateTime.UtcNow;

        if (_records.TryGetValue((playerActorId, requestId), out var existing))
        {
            if (now < existing.ExpiresAt)
            {
                existingRecord = existing;
                return true;
            }

            _records.TryRemove((playerActorId, requestId), out _);
        }

        existingRecord = null;
        return false;
    }

    public void Set(long playerActorId, string requestId, IdempotencyRecord record)
    {
        _records[(playerActorId, requestId)] = record;
    }

    public bool Remove(long playerActorId, string requestId)
    {
        return _records.TryRemove((playerActorId, requestId), out _);
    }

    public int CleanupExpired()
    {
        var now = DateTime.UtcNow;
        var count = 0;

        foreach (var kvp in _records)
        {
            if (now >= kvp.Value.ExpiresAt)
            {
                if (_records.TryRemove(kvp.Key, out _))
                {
                    count++;
                }
            }
        }

        if (count > 0)
        {
            LogHelper.Debug("InMemoryIdempotencyStorage.CleanupExpired, Cleaned up {count} expired records, remaining: {remaining}", count, _records.Count);
        }

        return count;
    }
}
