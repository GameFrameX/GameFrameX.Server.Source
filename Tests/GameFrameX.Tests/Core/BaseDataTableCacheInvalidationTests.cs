using System.Threading.Tasks;
using GameFrameX.Core.Config;
using Xunit;

namespace GameFrameX.Tests.Core;

public class BaseDataTableCacheInvalidationTests
{
    private class CacheInvalidationTestTable : BaseDataTable<object>
    {
        private int _startId;
        private int _count;

        public void Configure(int startId, int count)
        {
            _startId = startId;
            _count = count;
        }

        public override async Task LoadAsync()
        {
            await Task.CompletedTask;
            LongDataMaps.Clear();
            StringDataMaps.Clear();
            DataList.Clear();
            for (var i = 0; i < _count; i++)
            {
                var id = _startId + i;
                LongDataMaps.Add(id, (long)id);
                StringDataMaps.Add(id.ToString(), (long)id);
                DataList.Add((long)id);
            }

            InvalidateCaches();
        }
    }

    [Fact]
    public async Task Reload_Refreshes_First_Last_Count_Caches()
    {
        var table = new CacheInvalidationTestTable();

        table.Configure(10, 3);
        await table.LoadAsync();
        Assert.Equal(3, table.Count);
        Assert.Equal(10L, table.FirstOrDefault);
        Assert.Equal(12L, table.LastOrDefault);

        // 热重载一：删除首行，数据变为 11..12
        table.Configure(11, 2);
        await table.LoadAsync();
        Assert.Equal(2, table.Count);
        Assert.Equal(11L, table.FirstOrDefault);
        Assert.Equal(12L, table.LastOrDefault);

        // 热重载二：清空
        table.Configure(0, 0);
        await table.LoadAsync();
        Assert.Equal(0, table.Count);
        Assert.Null(table.FirstOrDefault);
        Assert.Null(table.LastOrDefault);

        // 热重载三：重新填充
        table.Configure(20, 5);
        await table.LoadAsync();
        Assert.Equal(5, table.Count);
        Assert.Equal(20L, table.FirstOrDefault);
        Assert.Equal(24L, table.LastOrDefault);
    }

    /// <summary>
    /// 混合键测试表：DataList 登记全部条目，两键字典各只登记一部分——
    /// 用于锁定 Count 的事实源必须是条目列表而非两字典计数取 Max（GFX-1178 / CR-L02）。
    /// </summary>
    private class MixedKeyTestTable : BaseDataTable<object>
    {
        public override Task LoadAsync()
        {
            LongDataMaps.Clear();
            StringDataMaps.Clear();
            DataList.Clear();

            for (var i = 1; i <= 5; i++)
            {
                DataList.Add((long)i);
            }

            // 两键字典各只登记部分键型（2 + 3 = 5 条，但任一单字典都不足条目数）
            LongDataMaps.Add(1, 1L);
            LongDataMaps.Add(2, 2L);
            StringDataMaps.Add("3", 3L);
            StringDataMaps.Add("4", 4L);
            StringDataMaps.Add("5", 5L);

            InvalidateCaches();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Count_MixedKeyDictionaries_MatchesDataListCount()
    {
        var table = new MixedKeyTestTable();
        await table.LoadAsync();

        // 旧实现取 Math.Max(LongDataMaps.Count, StringDataMaps.Count) = 3，低估条目数
        Assert.Equal(5, table.Count);
    }

    /// <summary>
    /// 长整型值测试表：可配置条目数（含 0），用于 Max/Min 空表语义回归（GFX-1178 / CR-L02）。
    /// </summary>
    private class LongValueTestTable : BaseDataTable<object>
    {
        private int _count;

        public void Configure(int count)
        {
            _count = count;
        }

        public override Task LoadAsync()
        {
            LongDataMaps.Clear();
            StringDataMaps.Clear();
            DataList.Clear();
            for (var i = 0; i < _count; i++)
            {
                var value = 10L + i;
                LongDataMaps.Add(value, value);
                DataList.Add(value);
            }

            InvalidateCaches();
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task Max_EmptyTable_ReturnsDefaultWithoutThrowing()
    {
        var table = new LongValueTestTable();
        table.Configure(0);
        await table.LoadAsync();

        // 旧实现空表直接抛 InvalidOperationException
        Assert.Null(table.Max(item => item));
    }

    [Fact]
    public async Task Min_EmptyTable_ReturnsDefaultWithoutThrowing()
    {
        var table = new LongValueTestTable();
        table.Configure(0);
        await table.LoadAsync();

        Assert.Null(table.Min(item => item));
    }

    [Fact]
    public async Task Max_Min_NonEmptyTable_ReturnProjection()
    {
        var table = new LongValueTestTable();
        table.Configure(3);
        await table.LoadAsync();

        Assert.Equal(12L, (long)table.Max(item => item));
        Assert.Equal(10L, (long)table.Min(item => item));
    }
}
