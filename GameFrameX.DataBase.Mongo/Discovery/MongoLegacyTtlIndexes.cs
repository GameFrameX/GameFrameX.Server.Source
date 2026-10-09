// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目必须严格遵守适用法律法规与开源许可证之规定。
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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using MongoDB.Bson;
using MongoDB.Driver;

namespace GameFrameX.DataBase.Mongo.Discovery;

/// <summary>
/// 遗留 TTL 索引的幂等清除工具（客户端 TTL 清理切换的存量迁移）。
/// </summary>
/// <remarks>
/// Removes the legacy server-side TTL indexes (named <c>{element}_ttl_{window}</c> by the
/// former naming rule) from the discovery-layer collections. Expiry deletion is client-side
/// now: both stores' <c>DeleteExpiredAsync</c> executes the real delete driven by the generic
/// registry's cleanup loop, so a surviving TTL index would keep deleting rows on the server's
/// own schedule (the mongod TTL monitor, ~60 s) and silently override a lengthened
/// <c>TtlCleanupInterval</c>. Identification is by the name segment <c>_ttl_</c> (the element
/// names never contain it); <c>_id_</c> and every other index are untouched.
/// </remarks>
internal static class MongoLegacyTtlIndexes
{
    /// <summary>
    /// 删除集合中全部遗留 TTL 索引（名字含 <c>_ttl_</c> 段；不存在则 no-op）。
    /// </summary>
    /// <remarks>
    /// Drops every legacy TTL index in the collection (names carrying the <c>_ttl_</c>
    /// segment; a no-op when none exist). Called from the stores' EnsureSchema bootstrap.
    /// </remarks>
    /// <typeparam name="TDocument">文档类型 / The document type</typeparam>
    /// <param name="collection">目标集合 / The target collection</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务 / Async task</returns>
    internal static async Task DropAllAsync<TDocument>(IMongoCollection<TDocument> collection, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(collection, nameof(collection));

        using var cursor = await collection.Indexes.ListAsync(cancellationToken).ConfigureAwait(false);
        var indexes = await cursor.ToListAsync(cancellationToken).ConfigureAwait(false);
        foreach (var index in indexes)
        {
            if (!index.TryGetElement("name", out var nameElement) || nameElement.Value.BsonType != BsonType.String)
            {
                continue;
            }

            var name = nameElement.Value.AsString;
            if (name.Contains("_ttl_", StringComparison.Ordinal))
            {
                try
                {
                    await collection.Indexes.DropOneAsync(name, cancellationToken).ConfigureAwait(false);
                }
                catch (MongoCommandException exception) when (exception.Code == IndexNotFoundCode)
                {
                    // 并发装配（多进程共享控制库同时 EnsureSchema）时对方已删同名索引：视作本方完成。
                    // A concurrent bootstrap already dropped it; treat as done.
                }
            }
        }
    }

    /// <summary>
    /// IndexNotFound 错误码（并发装配窗口内对方进程已删同名索引）。
    /// </summary>
    /// <remarks>
    /// The IndexNotFound error code (a concurrent bootstrap process already
    /// dropped the same-named index within the race window).
    /// </remarks>
    private const int IndexNotFoundCode = 27;
}
