// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or violate the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Threading.Tasks;
using GameFrameX.Config;
using GameFrameX.Config.Tables;
using GameFrameX.Core.Config;
using Xunit;

namespace GameFrameX.Tests.Config;

/// <summary>
/// TablesComponent 配置表热重载与逐表发布回归测试：
/// 重复 LoadAsync 发布新实例、加载期间保留上一版数据、加载失败不修改已发布映射。
/// 共享 ConfigComponent.Instance 单例，全部用例收敛在本测试类内串行执行。
/// </summary>
/// <remarks>
/// Regression tests for TablesComponent hot reload and per-table publishing:
/// repeated LoadAsync publishes new instances, loading preserves published mappings,
/// and a failed load leaves those mappings intact; cross-table snapshot isolation is not provided.
/// Shares the ConfigComponent.Instance singleton; all cases are kept in this class for
/// serialized execution.
/// </remarks>
public class TablesComponentHotReloadTests
{
    /// <summary>
    /// 空表字节：ReadSize 变长编码首字节 0x00 表示 0 条记录。
    /// </summary>
    /// <remarks>
    /// Empty-table bytes: a leading 0x00 byte in ReadSize variable-length encoding means zero records.
    /// </remarks>
    private static readonly byte[] EmptyTableBytes = { 0x00 };

    /// <summary>
    /// 空表加载器：对任意表名返回空表字节（不触磁盘）。
    /// </summary>
    /// <remarks>
    /// Empty-table loader returning empty-table bytes for any file name (no disk access).
    /// </remarks>
    private static Task<ByteBuf> EmptyTableLoader(string file, bool tag)
    {
        return Task.FromResult(ByteBuf.Wrap(EmptyTableBytes));
    }

    /// <summary>
    /// 加载前 IsLoaded 为 false，加载完成后为 true。
    /// </summary>
    [Fact]
    public async Task IsLoaded_FalseBeforeLoad_TrueAfterLoad()
    {
        var tables = new TablesComponent();
        tables.Init(ConfigComponent.Instance);

        Assert.False(tables.IsLoaded);

        await tables.LoadAsync(EmptyTableLoader);

        Assert.True(tables.IsLoaded);
    }

    /// <summary>
    /// 二次 LoadAsync 发布新表实例（热重载生效）；旧实现的 if (IsLoaded) return 短路使本用例必败。
    /// </summary>
    [Fact]
    public async Task LoadAsync_SecondCall_PublishesNewTableInstances()
    {
        var tables = new TablesComponent();
        tables.Init(ConfigComponent.Instance);
        await tables.LoadAsync(EmptyTableLoader);
        var firstItemTable = ConfigComponent.Instance.GetConfig<TbItemConfig>();
        Assert.NotNull(firstItemTable);

        await tables.LoadAsync(EmptyTableLoader);
        var secondItemTable = ConfigComponent.Instance.GetConfig<TbItemConfig>();
        Assert.NotNull(secondItemTable);

        Assert.NotSame(firstItemTable, secondItemTable);
    }

    /// <summary>
    /// 重载加载期间读者始终看到上一版已发布表（非 null 且为旧实例），发布后切换为新实例。
    /// </summary>
    [Fact]
    public async Task LoadAsync_DuringReload_ReadersKeepSeeingPublishedTables()
    {
        var tables = new TablesComponent();
        tables.Init(ConfigComponent.Instance);
        await tables.LoadAsync(EmptyTableLoader);
        var publishedItemTable = ConfigComponent.Instance.GetConfig<TbItemConfig>();
        Assert.NotNull(publishedItemTable);

        var loaderReachedBlockedTable = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBlockedTable = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<ByteBuf> BlockingLoader(string file, bool tag)
        {
            if (file == "tables_tbitemconfig")
            {
                loaderReachedBlockedTable.TrySetResult(true);
                await releaseBlockedTable.Task;
            }

            return ByteBuf.Wrap(EmptyTableBytes);
        }

        var reloadTask = tables.LoadAsync(BlockingLoader);
        // 若回归重新引入加载短路（loader 永不被调用），超时让用例快速失败而非挂死
        await loaderReachedBlockedTable.Task.WaitAsync(TimeSpan.FromSeconds(30));

        // 新表尚未发布：映射中仍是旧实例，绝不出现空窗
        Assert.Same(publishedItemTable, ConfigComponent.Instance.GetConfig<TbItemConfig>());

        releaseBlockedTable.TrySetResult(true);
        await reloadTask;

        Assert.NotSame(publishedItemTable, ConfigComponent.Instance.GetConfig<TbItemConfig>());
    }

    /// <summary>
    /// 重载中任一表加载失败：异常向上传播且零部分发布——旧实例保留、IsLoaded 保持 true；
    /// 旧实现短路直接返回不抛异常，本用例必败。
    /// </summary>
    [Fact]
    public async Task LoadAsync_FailedReload_KeepsPublishedTablesIntact()
    {
        var tables = new TablesComponent();
        tables.Init(ConfigComponent.Instance);
        await tables.LoadAsync(EmptyTableLoader);
        var publishedItemTable = ConfigComponent.Instance.GetConfig<TbItemConfig>();
        Assert.NotNull(publishedItemTable);

        async Task<ByteBuf> FailingLoader(string file, bool tag)
        {
            if (file == "tables_tbsoundsconfig")
            {
                throw new InvalidOperationException("模拟配置文件加载失败");
            }

            return ByteBuf.Wrap(EmptyTableBytes);
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() => tables.LoadAsync(FailingLoader));

        Assert.Same(publishedItemTable, ConfigComponent.Instance.GetConfig<TbItemConfig>());
        Assert.True(tables.IsLoaded);
    }
}
