// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or violate the legal rights and interests of others as prohibited by laws and regulations!
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


using System.Linq.Expressions;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// 契约表达式参数重写器：把 <c>Expression&lt;Func&lt;TState, ...&gt;&gt;</c> 的参数引用重写为 <c>StateRow&lt;TState&gt;.Doc</c> 成员访问。
/// </summary>
/// <remarks>
/// Rewrites parameter references of contract expressions (<c>x =&gt; x.Field</c>) into <c>row =&gt; row.Doc.Field</c>
/// member accesses so <see cref="IDatabaseService"/> expressions can drive EF queries over the
/// <c>StateRow&lt;TState&gt;</c> wrapper entity. 禁止 <c>Expression.Invoke</c> 形态（EF 不翻译）；重写保持表达式树
/// 结构不变，仅替换参数节点，翻译能力与 EF 官方翻译器一致。
/// </remarks>
internal static class PostgreSqlStateRowExpressionRewriter
{
    /// <summary>
    /// 重写过滤表达式（TState 参数 → row.Doc）。
    /// </summary>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="filter">契约过滤表达式 / The contract filter expression</param>
    /// <returns>面向包装实体的过滤表达式 / The wrapper-entity filter expression</returns>
    public static Expression<Func<StateRow<TState>, bool>> RewriteFilter<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        var rowParameter = Expression.Parameter(typeof(StateRow<TState>), "row");
        var body = new DocAccessRewriter(filter.Parameters[0], BuildDocAccess<TState>(rowParameter)).Visit(filter.Body);
        return Expression.Lambda<Func<StateRow<TState>, bool>>(body, rowParameter);
    }

    /// <summary>
    /// 重写选择器表达式（TState 参数 → row.Doc；排序 / 投影共用）。
    /// </summary>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <typeparam name="TResult">选择器结果类型 / The selector result type</typeparam>
    /// <param name="selector">契约选择器表达式 / The contract selector expression</param>
    /// <returns>面向包装实体的选择器表达式 / The wrapper-entity selector expression</returns>
    public static Expression<Func<StateRow<TState>, TResult>> RewriteSelector<TState, TResult>(Expression<Func<TState, TResult>> selector) where TState : BaseCacheState, new()
    {
        var rowParameter = Expression.Parameter(typeof(StateRow<TState>), "row");
        var body = new DocAccessRewriter(selector.Parameters[0], BuildDocAccess<TState>(rowParameter)).Visit(selector.Body);
        return Expression.Lambda<Func<StateRow<TState>, TResult>>(body, rowParameter);
    }

    /// <summary>
    /// 构建 <c>row.Doc</c> 成员访问表达式。
    /// </summary>
    /// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
    /// <param name="rowParameter">包装实体参数 / The wrapper-entity parameter</param>
    /// <returns><c>row.Doc</c> 访问表达式 / The <c>row.Doc</c> access expression</returns>
    private static MemberExpression BuildDocAccess<TState>(ParameterExpression rowParameter) where TState : BaseCacheState, new()
    {
        return Expression.Property(rowParameter, nameof(StateRow<TState>.Doc));
    }

    /// <summary>
    /// 参数节点替换访问器（仅替换契约 lambda 参数，其余节点原样保留）。
    /// </summary>
    /// <remarks>
    /// Replaces only the contract lambda's parameter nodes; every other node is returned unchanged.
    /// </remarks>
    private sealed class DocAccessRewriter : ExpressionVisitor
    {
        private readonly ParameterExpression _stateParameter;
        private readonly Expression _docAccess;

        /// <summary>
        /// 初始化参数替换访问器。
        /// </summary>
        /// <param name="stateParameter">契约参数 / The contract parameter</param>
        /// <param name="docAccess">row.Doc 访问表达式 / The row.Doc access expression</param>
        public DocAccessRewriter(ParameterExpression stateParameter, Expression docAccess)
        {
            _stateParameter = stateParameter;
            _docAccess = docAccess;
        }

        /// <summary>
        /// 访问参数节点，命中契约参数时替换为 row.Doc。
        /// </summary>
        /// <param name="node">参数节点 / The parameter node</param>
        /// <returns>替换后的节点 / The replaced node</returns>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            return node == _stateParameter ? _docAccess : node;
        }
    }
}
