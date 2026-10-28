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
}
