// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe on the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
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

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo;
using GameFrameX.Foundation.Orm.Attribute;
using MongoDB.Bson;
using MongoDB.Driver;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// MongoDbService 索引引导与索引判重回归测试（C264：异步单飞引导 / CreateIndex 精确名判重 / 冷并发唯一插入）。
/// </summary>
/// <remarks>
/// Regression tests for the MongoDbService index bootstrap and index dedup fixes (C264:
/// asynchronous single-flight bootstrap, exact-name dedup with full cursor traversal, and the
/// cold-collection concurrent unique insert). Only the real MongoDB provider is exercised
/// (<c>GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING</c>, the established gating pattern — facts are
/// silently skipped when the variable is unset). Each fact pins a behavior that the former
/// implementation got wrong: a fresh collection never got its index (the inverted first-batch
/// check), same-name calls could create duplicates, adjacent prefix names were misjudged as
/// existing, and unnamed definitions could not be deduplicated at all.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class MongoDbServiceIndexTests
{
    /// <summary>
    /// 并发竞态用例的写者数量。
    /// </summary>
    private const int RacerCount = 8;

    /// <summary>
    /// 测试新集合首建：CreateIndex 在空集合上真实创建索引并返回索引名（旧实现反向判重导致从不创建、返回空字符串）。
    /// </summary>
    [Fact]
    public async Task CreateIndex_FreshCollection_ShouldCreateAndReturnName()
    {
        await ExecuteWithMongoAsync(async (service, database) =>
        {
            var collectionName = "index_probe_fresh";
            var definition = new IndexDefinition { Name = "ux_index_probe_fresh", Key = "Level", Unique = true };

            var createdName = service.CreateIndex(collectionName, definition);

            Assert.Equal(definition.Name, createdName);
            var names = await ListIndexNamesAsync(database, collectionName);
            Assert.Equal(1, names.Count(name => name == definition.Name));
        });
    }

    /// <summary>
    /// 测试同名判重：同名索引二调返回空字符串且不重复建（旧实现首批未命中即建，可重复建同名索引）。
    /// </summary>
    [Fact]
    public async Task CreateIndex_SameExactName_SecondCall_ShouldReturnEmptyWithoutDuplicate()
    {
        await ExecuteWithMongoAsync(async (service, database) =>
        {
            var collectionName = "index_probe_dedup";
            var definition = new IndexDefinition { Name = "ux_index_probe_dedup", Key = "Level" };

            Assert.Equal(definition.Name, service.CreateIndex(collectionName, definition));
            Assert.Equal(string.Empty, service.CreateIndex(collectionName, definition));

            var names = await ListIndexNamesAsync(database, collectionName);
            Assert.Equal(1, names.Count(name => name == definition.Name));
        });
    }

    /// <summary>
    /// 测试前缀陷阱：前缀相近的显式名互不误判抑制；既有名为 "Level" 的索引（恰好以字段名 Level 为前缀）不得误判抑制字段 Level 默认名索引的真实创建（旧实现 StartsWith(Key) 前缀匹配漏建）。
    /// </summary>
    [Fact]
    public async Task CreateIndex_AdjacentPrefixName_ShouldNotBeSuppressed()
    {
        await ExecuteWithMongoAsync(async (service, database) =>
        {
            var collectionName = "index_probe_prefix";
            var first = new IndexDefinition { Name = "ux_pfx", Key = "Payload" };
            var second = new IndexDefinition { Name = "ux_pfx_v2", Key = "Tag" };

            Assert.Equal(first.Name, service.CreateIndex(collectionName, first));
            Assert.Equal(second.Name, service.CreateIndex(collectionName, second));

            // 诱饵：名为 "Level" 的索引（键在 Fresh 上，与字段名无关）。旧实现以 StartsWith(Key="Level")
            // 命中该名后误判已存在，返回空字符串且从不创建字段 Level 的索引；精确名匹配下两不相干。
            // Decoy: an index literally NAMED "Level" (keyed on Fresh). The old StartsWith(Key="Level")
            // check matched this name, wrongly concluding the field index existed and never creating it;
            // under exact-name matching the two are unrelated.
            var decoy = new IndexDefinition { Name = "Level", Key = "Fresh" };
            Assert.Equal(decoy.Name, service.CreateIndex(collectionName, decoy));
            var victim = new IndexDefinition { Key = "Level" };
            Assert.Equal("Level_1", service.CreateIndex(collectionName, victim));

            var names = await ListIndexNamesAsync(database, collectionName);
            Assert.Contains(first.Name, names);
            Assert.Contains(second.Name, names);
            Assert.Contains(decoy.Name, names);
            Assert.Contains("Level_1", names);
        });
    }

    /// <summary>
    /// 测试无名定义默认名判重：Name 为空时按渲染键推导服务端默认名（Level_1），二调返回空字符串。
    /// </summary>
    [Fact]
    public async Task CreateIndex_UnnamedDefinition_ShouldDedupByDerivedDefaultName()
    {
        await ExecuteWithMongoAsync(async (service, database) =>
        {
            var collectionName = "index_probe_unnamed";
            var definition = new IndexDefinition { Key = "Level" };

            Assert.Equal("Level_1", await service.CreateIndexAsync(collectionName, definition));
            Assert.Equal(string.Empty, await service.CreateIndexAsync(collectionName, definition));

            var names = await ListIndexNamesAsync(database, collectionName);
            Assert.Equal(1, names.Count(name => name == "Level_1"));
        });
    }

    /// <summary>
    /// 测试泛型异步索引判重：同名二调返回空字符串且不重复建（IndexDefinition&lt;TState&gt; 路径）。
    /// </summary>
    [Fact]
    public async Task CreateIndexAsync_Generic_SameName_ShouldDedup()
    {
        await ExecuteWithMongoAsync(async (service, database) =>
        {
            var definition = new IndexDefinition<IndexTestState> { Name = "ux_index_generic_level", Key = x => x.Level };

            Assert.Equal(definition.Name, await service.CreateIndexAsync(definition));
            Assert.Equal(string.Empty, await service.CreateIndexAsync(definition));

            var names = await ListIndexNamesAsync(database, nameof(IndexTestState));
            Assert.Equal(1, names.Count(name => name == definition.Name));
        });
    }

    /// <summary>
    /// 测试冷并发唯一插入（引导异步单飞时序钉）：全新类型无预热并发同键 TryAddUnique，恰一条入表。
    /// </summary>
    /// <remarks>
    /// The cold-collection variant of the C247 race fact: no warm-up insert, so the first
    /// concurrent touch triggers the asynchronous single-flight bootstrap. The unique index must
    /// be built before any probe-and-insert pair runs, or duplicates would slip through the
    /// uniqueness window (exactly the guarantee the C264 refactor must preserve).
    /// </remarks>
    [Fact]
    public async Task TryAddUnique_ColdCollection_ConcurrentSameKey_OnlyOneRowLands()
    {
        await ExecuteWithMongoAsync(async service =>
        {
            var tasks = Enumerable.Range(0, RacerCount).Select(i => Task.Run(async () =>
            {
                var racer = new IndexUniqueTestState { Id = NextId(), Identifier = "cold-race-key", Payload = $"racer-{i}" };
                return await service.TryAddUniqueAsync(racer, x => x.Identifier == "cold-race-key");
            }));
            var results = await Task.WhenAll(tasks);

            Assert.Equal(1, results.Count(won => won));
            Assert.Equal(1, await service.CountAsync<IndexUniqueTestState>(x => x.Identifier == "cold-race-key"));
        });
    }

    /// <summary>
    /// 生成唯一测试 Id。
    /// </summary>
    private static long NextId()
    {
        return Interlocked.Increment(ref _idSeed);
    }

    private static long _idSeed = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

    /// <summary>
    /// 列出集合的全部索引名（完整游标遍历）。
    /// </summary>
    private static async Task<List<string>> ListIndexNamesAsync(IMongoDatabase database, string collectionName)
    {
        var names = new List<string>();
        using (var cursor = await database.GetCollection<BsonDocument>(collectionName).Indexes.ListAsync())
        {
            while (await cursor.MoveNextAsync())
            {
                names.AddRange(cursor.Current.Where(doc => doc.Contains("name")).Select(doc => doc["name"].AsString));
            }
        }

        return names;
    }

    /// <summary>
    /// Mongo harness：环境变量门控 + 独立库创建/销毁（对齐 DatabaseConditionalWritePrimitivesTests 既有模式）。
    /// </summary>
    private static async Task ExecuteWithMongoAsync(Func<MongoDbService, IMongoDatabase, Task> action)
    {
        var connectionString = Environment.GetEnvironmentVariable("GAMEFRAMEX_TEST_MONGODB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        var dbName = $"gameframex_test_{Guid.NewGuid():N}";
        var service = new MongoDbService();
        var options = new DbOptions
        {
            ConnectionString = connectionString,
            Name = dbName,
        };

        var opened = await service.OpenAsync(options);
        Assert.True(opened);

        try
        {
            var database = new MongoClient(connectionString).GetDatabase(dbName);
            await action(service, database);
        }
        finally
        {
            await service.CloseAsync();
            try
            {
                var client = new MongoClient(connectionString);
                await client.DropDatabaseAsync(dbName);
            }
            catch
            {
            }
        }
    }

    /// <summary>
    /// 带单参服务回调的门控重载（无需直连 database 的用例）。
    /// </summary>
    private static Task ExecuteWithMongoAsync(Func<MongoDbService, Task> action)
    {
        return ExecuteWithMongoAsync((service, _) => action(service));
    }

    /// <summary>
    /// 索引测试用普通状态类型（无属性索引声明）。
    /// </summary>
    private sealed class IndexTestState : BaseCacheState
    {
        /// <summary>
        /// 等级字段。
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 载荷字段。
        /// </summary>
        public string Payload { get; set; }
    }

    /// <summary>
    /// 索引测试用唯一键状态类型（provider 唯一索引声明）。
    /// </summary>
    private sealed class IndexUniqueTestState : BaseCacheState
    {
        /// <summary>
        /// 唯一标识字段（provider 唯一索引）。
        /// </summary>
        [EntityIndex("ux_index_tests_identifier", Unique = true)]
        public string Identifier { get; set; }

        /// <summary>
        /// 载荷字段。
        /// </summary>
        public string Payload { get; set; }
    }
}
