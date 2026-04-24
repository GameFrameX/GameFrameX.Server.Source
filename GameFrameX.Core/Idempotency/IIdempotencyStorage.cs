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

namespace GameFrameX.Core.Idempotency;

/// <summary>
/// 幂等存储接口，提供请求去重的基础操作。
/// 预留扩展到 Redis 等外部存储。
/// </summary>
public interface IIdempotencyStorage
{
    /// <summary>
    /// 尝试获取已缓存的幂等记录
    /// </summary>
    /// <param name="playerActorId">玩家 Actor ID</param>
    /// <param name="requestId">请求 ID</param>
    /// <param name="record">缓存的幂等记录</param>
    /// <returns>true 表示命中缓存</returns>
    bool TryGet(long playerActorId, string requestId, out IdempotencyRecord record);

    /// <summary>
    /// 原子操作：若不存在则标记为"处理中"并返回 false；若已存在则返回 true。
    /// 用于保证同一请求只有一个执行者。
    /// </summary>
    bool TryGetOrMark(long playerActorId, string requestId, out IdempotencyRecord existingRecord);

    /// <summary>
    /// 设置幂等记录（缓存结果）
    /// </summary>
    void Set(long playerActorId, string requestId, IdempotencyRecord record);

    /// <summary>
    /// 移除幂等记录
    /// </summary>
    bool Remove(long playerActorId, string requestId);

    /// <summary>
    /// 清理所有过期记录
    /// </summary>
    /// <returns>清理的记录数</returns>
    int CleanupExpired();
}

/// <summary>
/// 幂等记录
/// </summary>
public sealed class IdempotencyRecord
{
    /// <summary>
    /// 记录创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 过期时间
    /// </summary>
    public DateTime ExpiresAt { get; init; }

    /// <summary>
    /// 是否已完成（业务逻辑执行完毕）
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// 是否为 RPC 请求
    /// </summary>
    public bool IsRpc { get; init; }

    /// <summary>
    /// RPC 响应缓存数据（深拷贝 byte[]），非 RPC 为 null
    /// </summary>
    public byte[] ResponseData { get; set; }

    /// <summary>
    /// 异常信息缓存（CachePolicy = AllOutcomes 时）
    /// </summary>
    public string ExceptionMessage { get; set; }

    /// <summary>
    /// 是否为异常记录
    /// </summary>
    public bool IsFaulted => ExceptionMessage != null;
}
