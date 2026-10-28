// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Linq.Expressions;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Foundation.Orm.Attribute;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// SQLite TryAddUniqueAsync 快路径谓词归约判定矩阵（GFX-1036 / C326，internal 测试缝）。
/// </summary>
/// <remarks>
/// Unit matrix over <see cref="SqliteDbService.ResolveUniqueInsertFastPathProperty{TState}"/> (the internal
/// test seam): which predicate shapes take the engine-index fast path and which must fall back to the scan.
/// A wrong positive (fast path taken where the predicate is not equivalent to the state's own key) would
/// silently admit duplicate keys or reject legitimate inserts; a wrong negative only costs the scan.
/// </remarks>
public sealed class SqliteTryAddUniqueFastPathRouterTests
{
    /// <summary>
    /// 可归约形态走快路径：常量等值、闭包捕获、反向常量、数值与枚举成员。
    /// </summary>
    [Fact]
    public void ResolveFastPathProperty_ReducedShapes_ShouldReturnDeclaredUniqueProperty()
    {
        var constantState = new RouterUniqueState { Identifier = "alpha" };
        Assert.NotNull(Reduce(constantState, x => x.Identifier == "alpha"));

        var closureValue = "alpha";
        var closureState = new RouterUniqueState { Identifier = closureValue };
        Assert.NotNull(Reduce(closureState, x => x.Identifier == closureValue));

        var reversedState = new RouterUniqueState { Identifier = "alpha" };
        Assert.NotNull(Reduce(reversedState, x => "alpha" == x.Identifier));

        var numericState = new RouterUniqueState { Identifier = "irrelevant", Level = 7 };
        Assert.NotNull(Reduce(numericState, x => x.Level == 7));

        var enumState = new RouterUniqueState { Identifier = "irrelevant", Kind = RouterKind.Email };
        Assert.NotNull(Reduce(enumState, x => x.Kind == RouterKind.Email));
    }

    /// <summary>
    /// 不可归约形态必须回落：复合谓词、未声明成员、双成员比较、嵌套成员、不等于。
    /// </summary>
    [Fact]
    public void ResolveFastPathProperty_IrreducibleShapes_ShouldReturnNull()
    {
        var state = new RouterUniqueState { Identifier = "alpha", Payload = "p", Level = 3, Kind = RouterKind.Email };

        Assert.Null(Reduce(state, x => x.Identifier == "alpha" || x.Payload == "p"));
        Assert.Null(Reduce(state, x => x.Identifier == "alpha" && x.Level == 3));
        Assert.Null(Reduce(state, x => x.Payload == "p"));
        Assert.Null(Reduce(state, x => x.Identifier == x.Payload));
        Assert.Null(Reduce(state, x => x.Identifier != "beta"));
        Assert.Null(Reduce(state, x => x.Identifier.Length == 5));
    }

    /// <summary>
    /// 等价守卫必须回落：谓词常量与自身值不一致、自身值为 null。
    /// </summary>
    [Fact]
    public void ResolveFastPathProperty_EquivalenceGuardViolations_ShouldReturnNull()
    {
        var mismatch = new RouterUniqueState { Identifier = "beta" };
        Assert.Null(Reduce(mismatch, x => x.Identifier == "alpha"));

        var nullMember = new RouterUniqueState { Identifier = null };
        Assert.Null(Reduce(nullMember, x => x.Identifier == "present"));
    }

    /// <summary>
    /// 声明被跳过的属性必须回落：JsonIgnore、不可索引类型（DateTime / ulong / double）。
    /// </summary>
    [Fact]
    public void ResolveFastPathProperty_SkippedDeclarations_ShouldReturnNull()
    {
        var state = new RouterSkippedUniqueState { JsonIgnoredKey = "k", Moment = DateTime.UtcNow, HugeValue = ulong.MaxValue, Ratio = 1.5 };

        Assert.Null(Reduce(state, x => x.JsonIgnoredKey == "k"));
        Assert.Null(Reduce(state, x => x.Moment == state.Moment));
        Assert.Null(Reduce(state, x => x.HugeValue == ulong.MaxValue));
        Assert.Null(Reduce(state, x => x.Ratio == 1.5));
    }

    /// <summary>
    /// 以编译谓词执行快路径判定（与生产调用形态一致）。
    /// </summary>
    private static System.Reflection.PropertyInfo Reduce<TState>(TState state, Expression<Func<TState, bool>> uniqueFilter) where TState : BaseCacheState, new()
    {
        return SqliteDbService.ResolveUniqueInsertFastPathProperty(uniqueFilter, state, uniqueFilter.Compile());
    }

    /// <summary>
    /// 归约矩阵状态实体（字符串 / 数值 / 枚举成员与未声明成员并存）。
    /// </summary>
    private sealed class RouterUniqueState : BaseCacheState
    {
        /// <summary>
        /// 声明唯一索引的字符串字段。
        /// </summary>
        [EntityIndex("ux_router_identifier", Unique = true)]
        public string Identifier { get; set; }

        /// <summary>
        /// 声明唯一索引的数值字段。
        /// </summary>
        [EntityIndex("ux_router_level", Unique = true)]
        public int Level { get; set; }

        /// <summary>
        /// 声明唯一索引的枚举字段。
        /// </summary>
        [EntityIndex("ux_router_kind", Unique = true)]
        public RouterKind Kind { get; set; }

        /// <summary>
        /// 未声明唯一索引的载荷字段。
        /// </summary>
        public string Payload { get; set; }
    }

    /// <summary>
    /// 枚举测试类型。
    /// </summary>
    private enum RouterKind
    {
        /// <summary>邮箱。</summary>
        Email = 1,

        /// <summary>手机号。</summary>
        Phone = 2,
    }

    /// <summary>
    /// 声明被跳过规则覆盖的状态实体（JsonIgnore / DateTime / ulong / double 各一）。
    /// </summary>
    private sealed class RouterSkippedUniqueState : BaseCacheState
    {
        /// <summary>
        /// JsonIgnore 的声明字段（不写入 doc，索引会被静默架空）。
        /// </summary>
        [EntityIndex("ux_router_json_ignored", Unique = true)]
        [System.Text.Json.Serialization.JsonIgnore]
        public string JsonIgnoredKey { get; set; }

        /// <summary>
        /// DateTime 声明字段（等值语义与 json_extract 值域存在漂移面）。
        /// </summary>
        [EntityIndex("ux_router_moment", Unique = true)]
        public DateTime Moment { get; set; }

        /// <summary>
        /// ulong 声明字段（超 int64 值域在 json_extract 下退化为 REAL）。
        /// </summary>
        [EntityIndex("ux_router_huge_value", Unique = true)]
        public ulong HugeValue { get; set; }

        /// <summary>
        /// double 声明字段（浮点文本/精度漂移）。
        /// </summary>
        [EntityIndex("ux_router_ratio", Unique = true)]
        public double Ratio { get; set; }
    }
}
