// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   Gitee  Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB  Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Microsoft.Data.Sqlite;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// Sqlite 读路径 json_extract 谓词下推回归测试（C328：精确快路径零反序列化 / 裁剪读客户端谓词权威 / 不可翻译回退语义一致）。
/// </summary>
/// <remarks>
/// Regression tests for the SQLite read-path json_extract predicate pushdown (C328): the exact fast
/// paths deserialize zero rows, the pruned read keeps the client-side predicate authoritative, and
/// untranslatable filters fall back with semantics identical to the pre-fix behavior. The pushdown
/// discriminator is the state type's construction counter — System.Text.Json materializes each
/// deserialized row through the parameterless constructor, so a zero (or page-size) counter proves
/// the SQL layer did the filtering. Legacy documents with missing JSON keys are seeded through a raw
/// connection to pin the COALESCE / null-lifting exactness rules.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class SqlitePredicatePushdownTests : IDisposable
{
    private readonly List<string> _tempPaths = new();

    /// <summary>
    /// 可翻译过滤的 Count 走 SQL COUNT(*)：精确计数且零反序列化。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithTranslatableFilter_ShouldReturnExactCountWithoutDeserializing()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var count = await service.CountAsync<SqlitePushdownTestState>(state => state.Level >= 2 && state.GroupNumber != null);
            Assert.Equal(3, count);
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// null 过滤的 Count 只数可见行（软删排除），仍零反序列化。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithNullFilter_ShouldCountVisibleWithoutDeserializing()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var count = await service.CountAsync<SqlitePushdownTestState>(null);
            Assert.Equal(5, count);
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 闭包捕获常量同样可精确下推。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithClosureConstant_ShouldPushDownExactly()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            const int levelThreshold = 2;
            ResetConstructionCounter();
            var count = await service.CountAsync<SqlitePushdownTestState>(state => state.Level >= levelThreshold);
            Assert.Equal(4, count);
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// id 合取项映射 id 列（等值与范围），零反序列化。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithIdConjuncts_ShouldMapToIdColumn()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            Assert.Equal(3, await service.CountAsync<SqlitePushdownTestState>(state => state.Id >= 102 && state.Id <= 104));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 软删行被可见性合取项在 SQL 层排除；includeDeleted 时计入。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithSoftDeletedRow_ShouldExcludeUnlessIncluded()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            Assert.Equal(5, await service.CountAsync<SqlitePushdownTestState>(state => state.Coins > 0));
            Assert.Equal(6, await service.CountAsync<SqlitePushdownTestState>(state => state.Coins > 0, includeDeleted: true));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 可翻译过滤的 Any 走 SQL EXISTS：命中与未命中均零反序列化。
    /// </summary>
    [Fact]
    public async Task AnyAsync_WithTranslatableFilter_ShouldAnswerWithoutDeserializing()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            Assert.True(await service.AnyAsync<SqlitePushdownTestState>(state => state.Level >= 5));
            Assert.False(await service.AnyAsync<SqlitePushdownTestState>(state => state.Level >= 100));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 不可翻译过滤（方法调用）回退整表读 + 客户端过滤，结果与修复前一致。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithUntranslatableFilter_ShouldFallBackToMemoryFiltering()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var count = await service.CountAsync<SqlitePushdownTestState>(state => state.Name.StartsWith("player", StringComparison.Ordinal));
            Assert.Equal(4, count);
            Assert.Equal(5, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 部分可翻译：可译合取项下推裁剪读（SQL 读取行含被客户端谓词剔除者），客户端谓词权威定结果。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithPartiallyTranslatableFilter_ShouldPruneButStayAuthoritative()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var count = await service.CountAsync<SqlitePushdownTestState>(state => state.Level >= 2 && state.Name.StartsWith("player", StringComparison.Ordinal));
            Assert.Equal(3, count);
            // SQL 层 Level >= 2 读到 102/103/104/105 四行（含被客户端谓词剔除的 other-d）。
            // The Level >= 2 conjunct reads four rows (102/103/104/105), including other-d which the
            // client-side predicate then drops.
            Assert.Equal(4, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 白名单外类型（ulong / DateTime）、显式收窄与浮点常量、OrElse 均回退内存且结果正确。
    /// </summary>
    [Fact]
    public async Task CountAsync_WithNonWhitelistedMemberShapes_ShouldFallBackCorrectly()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            Assert.Equal(1, await service.CountAsync<SqlitePushdownTestState>(state => state.BigValue == 5UL));
            Assert.Equal(5, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(5, await service.CountAsync<SqlitePushdownTestState>(state => state.CreatedAt >= new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
            Assert.Equal(5, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(5, await service.CountAsync<SqlitePushdownTestState>(state => state.Coins >= 1.5));
            Assert.Equal(5, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(2, await service.CountAsync<SqlitePushdownTestState>(state => state.Level == 1 || state.Level == 5));
            Assert.Equal(5, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// FindPage 全谓词可译 + 数值排序键：SQL 分页 + 独立 COUNT，仅反序列化单页行，并列按 id 升序。
    /// </summary>
    [Fact]
    public async Task FindPageAsync_WithTranslatableSortKey_ShouldPageInSqlWithTieOrder()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var firstPage = await service.FindPageAsync<SqlitePushdownTestState>(state => state.Level >= 2, state => state.Coins, true, 0, 2);
            Assert.Equal(new[] { 104L, 102L }, firstPage.Items.Select(static state => state.Id).ToArray());
            Assert.Equal(4, firstPage.Total);
            Assert.Equal(2, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            var secondPage = await service.FindPageAsync<SqlitePushdownTestState>(state => state.Level >= 2, state => state.Coins, true, 1, 2);
            Assert.Equal(new[] { 105L, 103L }, secondPage.Items.Select(static state => state.Id).ToArray());
            Assert.Equal(4, secondPage.Total);
            Assert.Equal(2, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// FindPage 升序空值/并列语义：可空键 null 行最前，同键按 id 升序。
    /// </summary>
    [Fact]
    public async Task FindPageAsync_WithNullableSortKey_ShouldPlaceNullsFirstAscending()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var ascendingPage = await service.FindPageAsync<SqlitePushdownTestState>(state => state.Coins > 0, state => state.GroupNumber, false, 0, 3);
            // GroupNumber 升序：null(103) 最前 → 10(101) → 20(102) → 20(105) → 30(104)；同键 20 按 id 升序。
            // Ascending GroupNumber: null (103) first, then 10 (101), then the 20s (102 before 105, id order), then 30 (104).
            Assert.Equal(new[] { 103L, 101L, 102L }, ascendingPage.Items.Select(static state => state.Id).ToArray());
            Assert.Equal(5, ascendingPage.Total);
        });

        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var descendingPage = await service.FindPageAsync<SqlitePushdownTestState>(state => state.Coins > 0, state => state.GroupNumber, true, 0, 3);
            // 降序：30(104) → 20(102) → 20(105) → 10(101) → null(103) 最后；并列 20 仍按 id 升序。
            // Descending: 30 (104), the 20s (102 then 105, id order), 10 (101), null (103) last.
            Assert.Equal(new[] { 104L, 102L, 105L }, descendingPage.Items.Select(static state => state.Id).ToArray());
            Assert.Equal(5, descendingPage.Total);
        });
    }

    /// <summary>
    /// string 排序键不回推：回退裁剪读 + 内存排序，结果与内存语义一致。
    /// </summary>
    [Fact]
    public async Task FindPageAsync_WithStringSortKey_ShouldFallBackToMemorySort()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var page = await service.FindPageAsync<SqlitePushdownTestState>(state => state.Level >= 2, state => state.Name, true, 0, 3);
            // 序数降序：player-e > player-c > player-b > other-d。
            // Ordinal descending: player-e > player-c > player-b > other-d.
            Assert.Equal(new[] { 105L, 103L, 102L }, page.Items.Select(static state => state.Id).ToArray());
            Assert.Equal(4, page.Total);
            Assert.Equal(4, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// FindSortDescendingAsync / FirstOne 族快路径：排序分页与首元素仅反序列化所需行。
    /// </summary>
    [Fact]
    public async Task FindSortFamily_WithTranslatableKey_ShouldReadOnlyRequiredRows()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var descendingPage = await service.FindSortDescendingAsync<SqlitePushdownTestState>(state => state.Coins > 150, state => state.Coins, 0, 2);
            Assert.Equal(new[] { 104L, 102L }, descendingPage.Select(static state => state.Id).ToArray());
            Assert.Equal(2, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            var descendingFirst = await service.FindSortDescendingFirstOneAsync<SqlitePushdownTestState>(state => state.Coins > 0, state => state.Coins);
            Assert.Equal(104L, descendingFirst.Id);
            Assert.Equal(1, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            var ascendingFirst = await service.FindSortAscendingFirstOneAsync<SqlitePushdownTestState>(state => state.Coins > 0, state => state.Coins);
            Assert.Equal(101L, ascendingFirst.Id);
            Assert.Equal(1, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// id 合取项 + FindList：SQL 裁剪读仅反序列化命中行。
    /// </summary>
    [Fact]
    public async Task FindListAsync_WithIdEquality_ShouldDeserializeOnlyMatchedRow()
    {
        await ExecuteWithSeededServiceAsync(async service =>
        {
            ResetConstructionCounter();
            var list = await service.FindListAsync<SqlitePushdownTestState>(state => state.Id == 103 && state.Level >= 0);
            Assert.Single(list);
            Assert.Equal(103L, list[0].Id);
            Assert.Equal(1, SqlitePushdownTestState.ConstructionCounter);
        });
    }

    /// <summary>
    /// 旧档缺键文档的精确性矩阵：缺键 = default(T)（COALESCE 兜底）、可空缺键 = null、显式 IsDeleted 排除。
    /// </summary>
    [Fact]
    public async Task LegacyDocuments_WithMissingKeys_ShouldMatchDefaultSemanticsExactly()
    {
        var directory = CreateTempDirectory();
        var databaseFile = Path.Combine(directory, "legacy-documents.db");
        SeedRawDocuments(databaseFile);
        var service = new SqliteDbService();
        Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = $"Data Source={databaseFile}", Name = "pushdown-legacy-tests", }));
        try
        {
            ResetConstructionCounter();
            Assert.Equal(3, await service.CountAsync<SqlitePushdownTestState>(null));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(2, await service.CountAsync<SqlitePushdownTestState>(state => state.Level == 0));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(1, await service.CountAsync<SqlitePushdownTestState>(state => state.Level != 0));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(3, await service.CountAsync<SqlitePushdownTestState>(state => state.GroupNumber == null));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(0, await service.CountAsync<SqlitePushdownTestState>(state => state.GroupNumber != null));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(3, await service.CountAsync<SqlitePushdownTestState>(state => state.IsActive == false));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(0, await service.CountAsync<SqlitePushdownTestState>(state => state.IsActive));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(0, await service.CountAsync<SqlitePushdownTestState>(state => state.Status == SqlitePushdownTestStatus.Active));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(1, await service.CountAsync<SqlitePushdownTestState>(state => state.Name == "legacy-one"));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);

            ResetConstructionCounter();
            Assert.Equal(2, await service.CountAsync<SqlitePushdownTestState>(state => state.Name != "legacy-one"));
            Assert.Equal(0, SqlitePushdownTestState.ConstructionCounter);
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 重置反序列化计数器。
    /// </summary>
    private static void ResetConstructionCounter()
    {
        Interlocked.Exchange(ref SqlitePushdownTestState.ConstructionCounter, 0);
    }

    /// <summary>
    /// 以标准固定数据集打开内存库并执行断言（五可见行 + 一软删行）。
    /// </summary>
    private static async Task ExecuteWithSeededServiceAsync(Func<SqliteDbService, Task> action, string name = "sqlite-pushdown-tests")
    {
        var service = new SqliteDbService();
        Assert.True(await service.OpenAsync(new DbOptions { ConnectionString = "Data Source=:memory:", Name = name, }));
        try
        {
            await service.AddAsync(CreateState(101, "player-a", 1, 10, true, true, 100, SqlitePushdownTestStatus.Active));
            await service.AddAsync(CreateState(102, "player-b", 2, 20, false, null, 300, SqlitePushdownTestStatus.Active));
            await service.AddAsync(CreateState(103, "player-c", 3, null, true, false, 200, SqlitePushdownTestStatus.Inactive));
            await service.AddAsync(CreateState(104, "other-d", 5, 30, false, true, 400, SqlitePushdownTestStatus.Active));
            await service.AddAsync(CreateState(105, "player-e", 2, 20, true, null, 300, SqlitePushdownTestStatus.Suspended, bigValue: 5UL));
            var softDeleted = CreateState(106, "player-f", 4, 40, true, null, 500, SqlitePushdownTestStatus.Active);
            await service.AddAsync(softDeleted);
            await service.DeleteAsync(softDeleted);
            await action(service);
        }
        finally
        {
            await service.CloseAsync();
        }
    }

    /// <summary>
    /// 构造固定数据行。
    /// </summary>
    private static SqlitePushdownTestState CreateState(long id, string name, int level, int? groupNumber, bool isActive, bool? verificationStatus, long coins, SqlitePushdownTestStatus status, ulong bigValue = 0UL)
    {
        return new SqlitePushdownTestState
        {
            Id = id,
            Name = name,
            Level = level,
            GroupNumber = groupNumber,
            IsActive = isActive,
            VerificationStatus = verificationStatus,
            Coins = coins,
            Status = status,
            BigValue = bigValue,
            CreatedAt = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc),
        };
    }

    /// <summary>
    /// 以原生连接预置旧档缺键文档（表结构与惰性建表 DDL 同形）。
    /// </summary>
    private static void SeedRawDocuments(string databaseFile)
    {
        using (var connection = new SqliteConnection($"Data Source={databaseFile}"))
        {
            connection.Open();
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "CREATE TABLE IF NOT EXISTS \"SqlitePushdownTestState\" (id INTEGER PRIMARY KEY, doc TEXT NOT NULL);";
                command.ExecuteNonQuery();
            }

            var documents = new (long Identifier, string Document)[]
            {
                (201, "{\"Id\":201,\"Name\":\"legacy-one\"}"),
                (202, "{\"Id\":202,\"Name\":\"legacy-two\",\"Level\":7,\"Group\":null,\"IsDeleted\":false}"),
                (203, "{\"Id\":203,\"Name\":\"legacy-three\",\"Level\":0}"),
                (204, "{\"Id\":204,\"Name\":\"legacy-deleted\",\"Level\":3,\"IsDeleted\":true}"),
            };
            foreach (var (identifier, document) in documents)
            {
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = "INSERT INTO \"SqlitePushdownTestState\" (id, doc) VALUES ($id, $doc);";
                    command.Parameters.AddWithValue("$id", identifier);
                    command.Parameters.AddWithValue("$doc", document);
                    command.ExecuteNonQuery();
                }
            }
        }
    }

    /// <summary>
    /// 创建独立临时目录（Dispose 时清理）。
    /// </summary>
    private string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx-sqlite-pushdown-tests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempPaths.Add(path);
        return path;
    }

    /// <summary>
    /// 清理临时目录。
    /// </summary>
    public void Dispose()
    {
        foreach (var path in _tempPaths)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    Directory.Delete(path, recursive: true);
                }
            }
            catch
            {
                // 临时目录清理失败不影响测试结果
            }
        }
    }

    /// <summary>
    /// 谓词下推测试状态实体（构造计数器 = 反序列化判别器）。
    /// </summary>
    private sealed class SqlitePushdownTestState : BaseCacheState
    {
        /// <summary>
        /// 反序列化计数（STJ 物化每行必经无参构造）。
        /// </summary>
        public static int ConstructionCounter;

        /// <summary>
        /// 构造并计数。
        /// </summary>
        public SqlitePushdownTestState()
        {
            Interlocked.Increment(ref ConstructionCounter);
        }

        /// <summary>
        /// 名称。
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 等级（非可空整型，覆盖 COALESCE 缺键兜底）。
        /// </summary>
        public int Level { get; set; }

        /// <summary>
        /// 分组（可空整型，覆盖 null 提升语义）。
        /// </summary>
        public int? GroupNumber { get; set; }

        /// <summary>
        /// 是否激活（非可空 bool，含裸成员真值）。
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// 认证状态（可空 bool）。
        /// </summary>
        public bool? VerificationStatus { get; set; }

        /// <summary>
        /// 金币（long 排序键）。
        /// </summary>
        public long Coins { get; set; }

        /// <summary>
        /// 状态（数值底层枚举）。
        /// </summary>
        public SqlitePushdownTestStatus Status { get; set; }

        /// <summary>
        /// 大数值（ulong 白名单外，回退路径用）。
        /// </summary>
        public ulong BigValue { get; set; }

        /// <summary>
        /// 创建时间（DateTime 白名单外，回退路径用）。
        /// </summary>
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// 谓词下推测试状态枚举（数值序列化）。
    /// </summary>
    private enum SqlitePushdownTestStatus
    {
        /// <summary>
        /// 未激活。
        /// </summary>
        Inactive = 0,

        /// <summary>
        /// 激活。
        /// </summary>
        Active = 1,

        /// <summary>
        /// 暂停。
        /// </summary>
        Suspended = 2,
    }
}
