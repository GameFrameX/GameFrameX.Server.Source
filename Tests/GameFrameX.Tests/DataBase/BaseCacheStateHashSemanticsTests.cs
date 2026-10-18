// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.DataBase;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// BaseCacheState/StateHash 脏检查语义回归测试（C263 / GFX-903，无数据库依赖）：
/// 懒基线不得吞变异、精确提交不得吃到并发检查推进后的未落库快照。
/// </summary>
/// <remarks>
/// Dirty-check semantics regression tests for BaseCacheState/StateHash (C263 / GFX-903, no database
/// dependency): the lazy baseline must not swallow mutations, and the exact-snapshot commit must not
/// consume an unpersisted snapshot advanced by a concurrent check.
/// </remarks>
public sealed class BaseCacheStateHashSemanticsTests
{
    /// <summary>
    /// 懒基线（未调 LoadFromDbPostHandler）在变异后首检必须报已修改：
    /// 修复前懒初始化以当前序列化固化基线（isNew=false），变异被静默吞掉（永不回存）。
    /// </summary>
    [Fact]
    public void LazyBaseline_AfterMutation_FirstCheckMustReportChanged()
    {
        var state = new SampleHashState { Id = 1, Level = 10, };

        state.Level = 20; // 首检前变异（绕过 Provider 查询路径的业务构造场景）

        Assert.True(state.IsModify());
    }

    /// <summary>
    /// 懒基线首检必报已修改（按未持久化语义），精确提交一次后收敛为未修改；
    /// 之后再次变异必须再次报已修改。
    /// </summary>
    [Fact]
    public void LazyBaseline_ExactCommitOnce_SettlesThenDetectsNextMutation()
    {
        var state = new SampleHashState { Id = 2, Level = 1, };

        var (changed, data) = state.IsChanged();
        Assert.True(changed); // 新语义：未持久化 → 必 changed，强制一次幂等回存

        state.SaveToDbPostHandler(data);

        Assert.False(state.IsModify()); // 基线已按送库快照收敛

        state.Level = 99;
        Assert.True(state.IsModify());
    }

    /// <summary>
    /// 竞态窗口模拟：检查（取得送库快照）之后、精确提交之前，并发 IsChanged
    /// 把 ToSaveHash 推进到未落库快照；精确提交只认送库快照，不得吞掉该并发变异。
    /// </summary>
    [Fact]
    public void ExactCommit_ConcurrentCheckAdvancedToSaveHash_MustNotSwallowConcurrentMutation()
    {
        var state = new SampleHashState { Id = 3, Level = 100, };
        state.LoadFromDbPostHandler(); // 模拟 Provider 查询路径建立基线

        state.Level = 101;
        var (changed, persistedData) = state.IsChanged(); // saver 线程：检查并取得送库快照（Level=101）
        Assert.True(changed);

        // 检查与提交之间（写库窗口内）另一线程（如 Actor 反激活 ReadyToInactive）检查同一状态，
        // ToSaveHash 被推进到 Level=102 的快照——该快照尚未落库。
        state.Level = 102;
        Assert.True(state.IsModify());

        // saver 线程按实际送库快照（Level=101）精确提交。
        state.SaveToDbPostHandler(persistedData);

        // 修复前：参数less 提交会把 CacheHash 推进到 Level=102（从未落库）→ 此处误判未修改 → 静默丢数据。
        Assert.True(state.IsModify());

        // 收敛路径：再次回存 Level=102 后基线收敛。
        var (resave, resaveData) = state.IsChanged();
        Assert.True(resave);
        state.SaveToDbPostHandler(resaveData);
        Assert.False(state.IsModify());
    }

    /// <summary>
    /// 参数less 兼容提交语义保持：提交最近一次检查快照（ToSaveHash），状态未再变异时判未修改。
    /// </summary>
    [Fact]
    public void ParameterlessCommit_CommitsLatestCheckSnapshot_CompatSemanticsPreserved()
    {
        var state = new SampleHashState { Id = 4, Level = 7, };
        state.LoadFromDbPostHandler();

        state.Level = 8;
        Assert.True(state.IsModify());

        state.SaveToDbPostHandler(); // 外部自定义保存流程的兼容路径
        Assert.False(state.IsModify());
    }

    /// <summary>
    /// 快照语义钉（既有已裁定行为，PG/Mongo/Sqlite 测试同构）：
    /// UpdateAsync 提交的是时间戳 bump 前的检查快照，已更新过的对象再次检查仍视为已变更。
    /// </summary>
    [Fact]
    public void ExactCommit_UpdateAsyncFlow_CommitsPreBumpSnapshot_RestStillCountsAsChanged()
    {
        var state = new SampleHashState { Id = 5, Level = 30, };
        state.LoadFromDbPostHandler();

        state.Level = 31;
        var (changed, checkData) = state.IsChanged();
        Assert.True(changed);

        // 模拟 Provider UpdateAsync：bump 审计字段后整档写库，再按检查快照精确提交。
        state.UpdateTime = 1234567890L;
        state.UpdateCount = (state.UpdateCount ?? 0) + 1;
        state.SaveToDbPostHandler(checkData);

        // resting 序列化含 bump 后的审计字段 ≠ 已提交的 bump 前快照 → 仍视为已变更。
        Assert.True(state.IsModify());
    }

    /// <summary>
    /// 脏检查测试实体。
    /// </summary>
    private sealed class SampleHashState : BaseCacheState
    {
        /// <summary>
        /// 等级字段（测试变异用）。
        /// </summary>
        public int Level { get; set; }
    }
}
