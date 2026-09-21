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
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Linq.Expressions;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// GameDb 多库注册与门面别名行为的单元测试（C143a D20#2）。
/// </summary>
/// <remarks>
/// Unit tests for GameDb multi-database registration and facade alias behaviour (C143a D20#2).
/// Uses <see cref="NoConnectionDatabaseService"/> so no real database connection is required.
/// </remarks>
public class GameDbMultiDatabaseTests : IDisposable
{
    /// <summary>
    /// 构造函数：每个测试前重置 GameDb 静态门面与多库注册表。
    /// </summary>
    public GameDbMultiDatabaseTests()
    {
        GameDb.ResetForTesting();
    }

    /// <summary>
    /// 释放：测试后再次重置，避免静态状态泄漏到其它测试。
    /// </summary>
    public void Dispose()
    {
        GameDb.ResetForTesting();
    }

    /// <summary>
    /// 新签名按名注册后，As(name) 能取回对应实例。
    /// </summary>
    [Fact]
    public async Task Init_WithExplicitConnectionString_RegistersByName()
    {
        var opened = await GameDb.Init<NoConnectionDatabaseService>("mongodb://business", new DbOptions { Name = "gameframex" });

        Assert.True(opened);
        Assert.True(MultiDbRegistry.Contains("gameframex"));
        var service = GameDb.As<NoConnectionDatabaseService>("gameframex");
        Assert.NotNull(service);
        Assert.Equal("mongodb://business", service.LastOpenedOptions.ConnectionString);
        Assert.Equal("gameframex", service.LastOpenedOptions.Name);
    }

    /// <summary>
    /// 连接串为空时回落 DbOptions.ConnectionString。
    /// </summary>
    [Fact]
    public async Task Init_WithEmptyConnectionString_FallsBackToOptionsConnectionString()
    {
        var opened = await GameDb.Init<NoConnectionDatabaseService>(null, new DbOptions { ConnectionString = "mongodb://fallback", Name = "gameframex" });

        Assert.True(opened);
        Assert.Equal("mongodb://fallback", GameDb.As<NoConnectionDatabaseService>("gameframex").LastOpenedOptions.ConnectionString);
    }

    /// <summary>
    /// 控制库 + 业务库双注册：两个名字各自可取回，互不覆盖（D20#2 修复）。
    /// </summary>
    [Fact]
    public async Task Init_ControlAndBusinessDatabase_BothResolvable()
    {
        var connectionString = "mongodb://127.0.0.1:27017";
        var controlOpened = await GameDb.Init<NoConnectionDatabaseService>(connectionString, new DbOptions { Name = MultiDbRegistry.ControlDatabaseName });
        var businessOpened = await GameDb.Init<NoConnectionDatabaseService>(connectionString, new DbOptions { Name = "gameframex" });

        Assert.True(controlOpened);
        Assert.True(businessOpened);
        Assert.Equal(2, MultiDbRegistry.GetRegisteredDatabaseNames().Count);
        Assert.Equal(MultiDbRegistry.ControlDatabaseName, GameDb.As<NoConnectionDatabaseService>(MultiDbRegistry.ControlDatabaseName).LastOpenedOptions.Name);
        Assert.Equal("gameframex", GameDb.As<NoConnectionDatabaseService>("gameframex").LastOpenedOptions.Name);
    }

    /// <summary>
    /// 二次 Init 同名库：拒绝静默覆盖，抛 InvalidOperationException（D20#2 fail fast）。
    /// </summary>
    [Fact]
    public async Task Init_SameNameTwice_Throws()
    {
        await GameDb.Init<NoConnectionDatabaseService>("mongodb://first", new DbOptions { Name = "gameframex" });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            GameDb.Init<NoConnectionDatabaseService>("mongodb://second", new DbOptions { Name = "gameframex" }));
    }

    /// <summary>
    /// 静态门面绑定首个注册库，后续 Init 不再覆盖（D20#2 修复：不漂移到最后一次 Init）。
    /// </summary>
    [Fact]
    public async Task Init_FacadeStaysOnFirstRegisteredDatabase()
    {
        await GameDb.Init<NoConnectionDatabaseService>("mongodb://first", new DbOptions { Name = "first_database" });
        await GameDb.Init<NoConnectionDatabaseService>("mongodb://second", new DbOptions { Name = "second_database" });

        var facade = GameDb.As<NoConnectionDatabaseService>();
        Assert.Equal("first_database", facade.LastOpenedOptions.Name);
    }

    /// <summary>
    /// CloseAsync 关闭全部已注册库。
    /// </summary>
    [Fact]
    public async Task CloseAsync_ClosesAllRegisteredDatabases()
    {
        await GameDb.Init<NoConnectionDatabaseService>("mongodb://first", new DbOptions { Name = "first_database" });
        await GameDb.Init<NoConnectionDatabaseService>("mongodb://second", new DbOptions { Name = "second_database" });

        await GameDb.CloseAsync();

        Assert.Equal(2, NoConnectionDatabaseService.TotalCloseCallCount);
    }

    /// <summary>
    /// As(name) 未注册名：抛 InvalidOperationException 且消息包含已注册名。
    /// </summary>
    [Fact]
    public void As_UnknownName_Throws()
    {
        var exception = Assert.Throws<InvalidOperationException>(() => GameDb.As<NoConnectionDatabaseService>("missing_database"));

        Assert.Contains("missing_database", exception.Message);
    }

    /// <summary>
    /// 旧签名（[Obsolete]）委托新重载：连接串取自 DbOptions 并按名注册（AC-3 行为零变化）。
    /// </summary>
    [Fact]
    public async Task Init_LegacyOverload_RegistersWithOptionName()
    {
#pragma warning disable CS0618 // 旧签名兼容性验证
        var opened = await GameDb.Init<NoConnectionDatabaseService>(new DbOptions { ConnectionString = "mongodb://legacy", Name = "gameframex" });
#pragma warning restore CS0618

        Assert.True(opened);
        Assert.Equal("mongodb://legacy", GameDb.As<NoConnectionDatabaseService>("gameframex").LastOpenedOptions.ConnectionString);
    }

    /// <summary>
    /// 无连接的数据库服务测试替身：Open 记录选项并成功，其余成员一律不支持。
    /// </summary>
    /// <remarks>
    /// Test double without a real connection: Open records the options and succeeds; every other member throws NotSupportedException.
    /// </remarks>
    public sealed class NoConnectionDatabaseService : IDatabaseService
    {
        /// <summary>
        /// 全部实例的 Close 调用总次数。
        /// </summary>
        private static int _totalCloseCallCount;

        /// <summary>
        /// 全部实例的 Close 调用总次数（只读快照）。
        /// </summary>
        public static int TotalCloseCallCount
        {
            get { return Volatile.Read(ref _totalCloseCallCount); }
        }

        /// <summary>
        /// 最近一次 Open 收到的选项。
        /// </summary>
        public DbOptions LastOpenedOptions { get; private set; }

        /// <summary>
        /// 记录选项并返回成功。
        /// </summary>
        public Task<bool> Open(DbOptions dbOptions)
        {
            LastOpenedOptions = dbOptions;
            return Task.FromResult(true);
        }

        /// <summary>
        /// 计数并完成。
        /// </summary>
        public Task Close()
        {
            Volatile.Write(ref _totalCloseCallCount, TotalCloseCallCount + 1);
            return Task.CompletedTask;
        }

        public Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter = null, bool isCreateIfNotExists = true) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindAsync.");
        }

        public Task<TState> FindAsync<TState>(long id, Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindAsync.");
        }

        public Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists = true) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindAsync.");
        }

        public Task<TState> FindAsync<TState>(Expression<Func<TState, bool>> filter, bool isCreateIfNotExists, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindAsync.");
        }

        public Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindListAsync.");
        }

        public Task<List<TState>> FindListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindListAsync.");
        }

        public Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindByIdsAsync.");
        }

        public Task<List<TState>> FindByIdsAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindByIdsAsync.");
        }

        public Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindPageAsync.");
        }

        public Task<(List<TState> Items, long Total)> FindPageAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, bool descending, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindPageAsync.");
        }

        public Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindProjectedAsync.");
        }

        public Task<List<TResult>> FindProjectedAsync<TState, TResult>(Expression<Func<TState, bool>> filter, Expression<Func<TState, TResult>> selector, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindProjectedAsync.");
        }

        public Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortAscendingFirstOneAsync.");
        }

        public Task<TState> FindSortAscendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortAscendingFirstOneAsync.");
        }

        public Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortDescendingFirstOneAsync.");
        }

        public Task<TState> FindSortDescendingFirstOneAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortDescendingFirstOneAsync.");
        }

        public Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex = 0, int pageSize = 10) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortDescendingAsync.");
        }

        public Task<List<TState>> FindSortDescendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortDescendingAsync.");
        }

        public Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex = 0, int pageSize = 10) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortAscendingAsync.");
        }

        public Task<List<TState>> FindSortAscendingAsync<TState>(Expression<Func<TState, bool>> filter, Expression<Func<TState, object>> sortExpression, int pageIndex, int pageSize, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement FindSortAscendingAsync.");
        }

        public Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement CountAsync.");
        }

        public Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement CountAsync.");
        }

        public Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement CountAsync.");
        }

        public Task<long> CountAsync<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement CountAsync.");
        }

        public Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteAsync.");
        }

        public Task<long> DeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteAsync.");
        }

        public Task<long> DeleteAsync<TState>(TState state) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteAsync.");
        }

        public Task<long> DeleteAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteAsync.");
        }

        public Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement HardDeleteAsync.");
        }

        public Task<long> HardDeleteAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement HardDeleteAsync.");
        }

        public Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement RestoreAsync.");
        }

        public Task<long> RestoreAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement RestoreAsync.");
        }

        public Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteListAsync.");
        }

        public Task<long> DeleteListAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteListAsync.");
        }

        public Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteListIdAsync.");
        }

        public Task<long> DeleteListIdAsync<TState>(IEnumerable<long> ids, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement DeleteListIdAsync.");
        }

        public Task AddAsync<TState>(TState state) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddAsync.");
        }

        public Task AddAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddAsync.");
        }

        public Task<TState> AddOrUpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddOrUpdateAsync.");
        }

        public Task<TState> AddOrUpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddOrUpdateAsync.");
        }

        public Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddOrUpdateListAsync.");
        }

        public Task<long> AddOrUpdateListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddOrUpdateListAsync.");
        }

        public Task AddListAsync<TState>(IEnumerable<TState> states) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddListAsync.");
        }

        public Task AddListAsync<TState>(IEnumerable<TState> states, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AddListAsync.");
        }

        public Task<TState> UpdateAsync<TState>(TState state) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement UpdateAsync.");
        }

        public Task<TState> UpdateAsync<TState>(TState state, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement UpdateAsync.");
        }

        public Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement UpdateAsync.");
        }

        public Task<long> UpdateAsync<TState>(IEnumerable<TState> stateList, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement UpdateAsync.");
        }

        public Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement UpdatePartialAsync.");
        }

        public Task<long> UpdatePartialAsync<TState>(long id, IReadOnlyDictionary<string, object> updateFields, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement UpdatePartialAsync.");
        }

        public Task ExecuteInTransactionAsync(Func<Task> action)
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement ExecuteInTransactionAsync.");
        }

        public Task ExecuteInTransactionAsync(Func<Task> action, CancellationToken cancellationToken)
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement ExecuteInTransactionAsync.");
        }

        public Task<bool> ExistsByIdAsync<TState>(long id) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement ExistsByIdAsync.");
        }

        public Task<bool> ExistsByIdAsync<TState>(long id, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement ExistsByIdAsync.");
        }

        public Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AnyAsync.");
        }

        public Task<bool> AnyAsync<TState>(Expression<Func<TState, bool>> filter, CancellationToken cancellationToken) where TState : BaseCacheState, new()
        {
            throw new NotSupportedException($"NoConnectionDatabaseService does not implement AnyAsync.");
        }

    }
}
