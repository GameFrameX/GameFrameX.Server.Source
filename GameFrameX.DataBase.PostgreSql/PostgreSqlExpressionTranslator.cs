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
//   官方文档：https://gameframex.doc.arianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Linq.Expressions;
using System.Reflection;
using GameFrameX.DataBase;
using Npgsql;
using NpgsqlTypes;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// 有界表达式翻译器：将 <c>Expression&lt;Func&lt;TState,bool&gt;&gt;</c> 过滤与排序表达式翻译为参数化 jsonb SQL（C166 P1-8）。
/// </summary>
/// <remarks>
/// Bounded expression translator: converts filter and sort expressions into parameterized jsonb SQL
/// (C166 P1-8). The supported dialect matrix is fixed:
/// 属性等值/比较（含 nullable 与 <c>Convert</c> 解包）、<c>&amp;&amp;</c>/<c>||</c>/<c>!</c>、null 检查、
/// <c>string.Contains/StartsWith/EndsWith</c>、闭包常量；排序/投影限成员绑定。
/// <para>
/// null 语义对齐：SQL 三值逻辑下 <c>NULL &lt;&gt; v</c> 为 NULL（行被过滤），而 C#/MQL 的
/// <c>null != v</c> 为真（行被保留），因此 <c>NotEqual</c> 显式展开为 <c>(accessor IS NULL OR accessor &lt;&gt; v)</c>。
/// 排序 null 位置对齐 Mongo（升序 null 在前 / 降序 null 在后）。
/// </para>
/// <para>超集节点运行时抛出显式 <see cref="NotSupportedException"/>（指名节点与替代写法），绝不静默内存过滤。</para>
/// </remarks>
internal static class PostgreSqlExpressionTranslator
{
    /// <summary>
    /// 翻译过滤表达式为参数化 WHERE 片段。
    /// </summary>
    /// <remarks>
    /// Translates a filter expression into a parameterized WHERE fragment. Values are always bound as
    /// <c>NpgsqlParameter</c>s (never inlined into SQL text); unsupported nodes throw instead of
    /// silently falling back to in-memory filtering.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / State type</typeparam>
    /// <param name="filter">过滤表达式 / Filter expression</param>
    /// <returns>SQL 片段与参数列表 / SQL fragment and parameters</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="filter"/> 为 null 时抛出 / Thrown when <paramref name="filter"/> is null</exception>
    /// <exception cref="NotSupportedException">当表达式节点超出受支持方言矩阵时抛出 / Thrown when an expression node is outside the supported dialect matrix</exception>
    public static (string Sql, IReadOnlyList<NpgsqlParameter> Parameters) TranslateFilter<TState>(Expression<Func<TState, bool>> filter) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(filter, nameof(filter));
        var context = new TranslationContext();
        var sql = Visit(filter.Body, context, typeof(TState));
        return (sql, context.Parameters);
    }

    /// <summary>
    /// 翻译排序表达式为 ORDER BY 片段（仅成员绑定）。
    /// </summary>
    /// <remarks>
    /// Translates a sort expression into an ORDER BY fragment. Only single-property member bindings
    /// are supported; null placement mirrors Mongo (nulls/missing first ascending, last descending).
    /// </remarks>
    /// <typeparam name="TState">状态类型 / State type</typeparam>
    /// <param name="sortExpression">排序表达式 / Sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <returns>ORDER BY 片段 / ORDER BY fragment</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="sortExpression"/> 为 null 时抛出 / Thrown when <paramref name="sortExpression"/> is null</exception>
    /// <exception cref="NotSupportedException">当排序表达式不是单属性成员绑定时抛出 / Thrown when the sort expression is not a single-property member binding</exception>
    public static string TranslateSort<TState>(Expression<Func<TState, object>> sortExpression, bool descending) where TState : BaseCacheState, new()
    {
        ArgumentNullException.ThrowIfNull(sortExpression, nameof(sortExpression));
        var member = UnwrapConvert(sortExpression.Body);
        if (member is not MemberExpression { Expression: ParameterExpression, } memberExpression)
        {
            throw new NotSupportedException($"PostgreSqlExpressionTranslator: sort expression '{sortExpression}' is not a member binding (node type: {sortExpression.Body.NodeType}). Only single-property sort expressions like 'x => x.Property' are supported; compute complex ordering in memory instead.");
        }

        var property = ResolveProperty(memberExpression, typeof(TState));
        var accessor = PostgreSqlJsonbAccess.GetTypedAccessor(property.Name, property.PropertyType);
        // null 排列对齐 Mongo：升序 null/缺失在前，降序在后
        // Null placement mirrors Mongo: nulls/missing first ascending, last descending.
        return descending ? $"{accessor} DESC NULLS LAST" : $"{accessor} ASC NULLS FIRST";
    }

    /// <summary>
    /// 翻译上下文（参数名计数与集合）。
    /// </summary>
    /// <remarks>
    /// Translation context holding the parameter counter and the collected parameters.
    /// </remarks>
    private sealed class TranslationContext
    {
        public List<NpgsqlParameter> Parameters { get; } = new();

        public string AddParameter(object value, Type clrType)
        {
            var name = $"p{Parameters.Count}";
            Parameters.Add(CreateParameter(name, value, clrType));
            return "@" + name;
        }
    }

    /// <summary>
    /// 递归访问表达式节点。
    /// </summary>
    /// <remarks>
    /// Recursively visits expression nodes and returns the translated SQL fragment.
    /// </remarks>
    private static string Visit(Expression node, TranslationContext context, Type stateType)
    {
        switch (node.NodeType)
        {
            case ExpressionType.AndAlso:
            {
                var binary = (BinaryExpression)node;
                return $"({Visit(binary.Left, context, stateType)} AND {Visit(binary.Right, context, stateType)})";
            }

            case ExpressionType.OrElse:
            {
                var binary = (BinaryExpression)node;
                return $"({Visit(binary.Left, context, stateType)} OR {Visit(binary.Right, context, stateType)})";
            }

            case ExpressionType.Not:
            {
                var unary = (UnaryExpression)node;
                return $"NOT ({Visit(unary.Operand, context, stateType)})";
            }

            case ExpressionType.Equal:
            case ExpressionType.NotEqual:
            case ExpressionType.LessThan:
            case ExpressionType.LessThanOrEqual:
            case ExpressionType.GreaterThan:
            case ExpressionType.GreaterThanOrEqual:
                return VisitComparison((BinaryExpression)node, context, stateType);

            case ExpressionType.Call:
                return VisitMethodCall((MethodCallExpression)node, context, stateType);

            case ExpressionType.Convert:
                return Visit(((UnaryExpression)node).Operand, context, stateType);

            case ExpressionType.MemberAccess:
            {
                // 布尔成员作为独立谓词：x => x.IsActive
                if (node.Type == typeof(bool) || node.Type == typeof(bool?))
                {
                    var accessor = ResolveMemberAccessor((MemberExpression)node, stateType);
                    return $"{accessor} = TRUE";
                }

                throw CreateUnsupported(node);
            }

            case ExpressionType.Constant:
            {
                var constant = (ConstantExpression)node;
                if (constant.Type == typeof(bool))
                {
                    return (bool)constant.Value ? "TRUE" : "FALSE";
                }

                throw CreateUnsupported(node);
            }

            default:
                throw CreateUnsupported(node);
        }
    }

    /// <summary>
    /// 访问比较表达式（等值/不等/大小比较，支持左右互换与 null 常量）。
    /// </summary>
    /// <remarks>
    /// Visits a comparison expression (equality/inequality/ordering), supporting swapped operands and
    /// null constants.
    /// </remarks>
    private static string VisitComparison(BinaryExpression node, TranslationContext context, Type stateType)
    {
        if (TryResolveMemberAccessor(node.Left, stateType, out var accessor, out var property))
        {
            var value = EvaluateConstantOperand(node.Right);
            return BuildComparison(accessor, property, value, node.NodeType, context);
        }

        if (TryResolveMemberAccessor(node.Right, stateType, out accessor, out property))
        {
            // 常量在左（5 == x.Group）：交换后翻译（比较与等值对称）
            var value = EvaluateConstantOperand(node.Left);
            var mirrored = node.NodeType switch
            {
                ExpressionType.LessThan           => ExpressionType.GreaterThan,
                ExpressionType.LessThanOrEqual    => ExpressionType.GreaterThanOrEqual,
                ExpressionType.GreaterThan        => ExpressionType.LessThan,
                ExpressionType.GreaterThanOrEqual => ExpressionType.LessThanOrEqual,
                _                                 => node.NodeType,
            };
            return BuildComparison(accessor, property, value, mirrored, context);
        }

        throw CreateUnsupported(node);
    }

    /// <summary>
    /// 构建比较 SQL（null 常量 → IS [NOT] NULL；NotEqual 显式 OR IS NULL 对齐 C#/MQL null 语义）。
    /// </summary>
    /// <remarks>
    /// Builds the comparison SQL (null constant → IS [NOT] NULL; NotEqual explicitly ORs IS NULL to
    /// align with C#/MQL null semantics).
    /// </remarks>
    private static string BuildComparison(string accessor, PropertyInfo property, object value, ExpressionType nodeType, TranslationContext context)
    {
        var sqlOperator = nodeType switch
        {
            ExpressionType.Equal              => "=",
            ExpressionType.NotEqual           => "<>",
            ExpressionType.LessThan           => "<",
            ExpressionType.LessThanOrEqual    => "<=",
            ExpressionType.GreaterThan        => ">",
            ExpressionType.GreaterThanOrEqual => ">=",
            _                                 => throw CreateUnsupported(null),
        };

        if (value == null)
        {
            return nodeType switch
            {
                ExpressionType.Equal    => $"{accessor} IS NULL",
                ExpressionType.NotEqual => $"{accessor} IS NOT NULL",
                _                       => throw new NotSupportedException($"PostgreSqlExpressionTranslator: ordering comparison against a null constant is not supported (operator '{sqlOperator}'). Use an explicit null check like 'x => x.Field == null' instead."),
            };
        }

        var parameterName = context.AddParameter(value, property.PropertyType);
        if (nodeType == ExpressionType.NotEqual)
        {
            // SQL 三值逻辑：NULL <> v 为 NULL（行被过滤）；C#/MQL null != v 为真（行保留）→ 显式 OR IS NULL
            // SQL three-valued logic filters NULL <> v rows out; C#/MQL keep them → explicit OR IS NULL.
            return $"({accessor} IS NULL OR {accessor} {sqlOperator} {parameterName})";
        }

        return $"{accessor} {sqlOperator} {parameterName}";
    }

    /// <summary>
    /// 访问字符串方法调用（Contains/StartsWith/EndsWith）。
    /// </summary>
    /// <remarks>
    /// Visits string method calls (Contains/StartsWith/EndsWith) and translates them into LIKE patterns.
    /// </remarks>
    private static string VisitMethodCall(MethodCallExpression node, TranslationContext context, Type stateType)
    {
        if (node.Object == null || node.Arguments.Count != 1)
        {
            throw CreateUnsupported(node);
        }

        if (!TryResolveMemberAccessor(node.Object, stateType, out var accessor, out var property) || property.PropertyType != typeof(string))
        {
            throw CreateUnsupported(node);
        }

        var methodName = node.Method.Name;
        if (methodName is not ("Contains" or "StartsWith" or "EndsWith"))
        {
            throw new NotSupportedException($"PostgreSqlExpressionTranslator: string method '{node.Method.DeclaringType?.Name}.{methodName}' is not supported. Supported string methods: Contains, StartsWith, EndsWith.");
        }

        var value = EvaluateConstantOperand(node.Arguments[0]);
        if (value == null)
        {
            throw new NotSupportedException($"PostgreSqlExpressionTranslator: string.{methodName} with a null argument is not supported (SQL LIKE yields no match, diverging from a C# NullReferenceException). Guard with a null check instead.");
        }

        var parameterName = context.AddParameter(value.ToString(), typeof(string));
        return methodName switch
        {
            "Contains"   => $"{accessor} LIKE '%' || {parameterName} || '%'",
            "StartsWith" => $"{accessor} LIKE {parameterName} || '%'",
            _            => $"{accessor} LIKE '%' || {parameterName}",
        };
    }

    /// <summary>
    /// 尝试将表达式解析为 jsonb 访问器（参数成员绑定，含 Convert 解包；闭包/嵌套成员返回 false）。
    /// </summary>
    /// <remarks>
    /// Tries to resolve an expression to a jsonb accessor (parameter member binding with Convert
    /// unwrapping; closures and nested member chains return false).
    /// </remarks>
    private static bool TryResolveMemberAccessor(Expression node, Type stateType, out string accessor, out PropertyInfo property)
    {
        accessor = null;
        property = null;
        var unwrapped = UnwrapConvert(node);
        if (unwrapped is not MemberExpression { Expression: ParameterExpression, } memberExpression)
        {
            return false;
        }

        property = ResolveProperty(memberExpression, stateType);
        accessor = PostgreSqlJsonbAccess.GetTypedAccessor(property.Name, property.PropertyType);
        return true;
    }

    /// <summary>
    /// 解析 jsonb 访问器（成员必须绑定参数）。
    /// </summary>
    /// <remarks>
    /// Resolves the jsonb accessor (the member must bind to the lambda parameter).
    /// </remarks>
    private static string ResolveMemberAccessor(MemberExpression node, Type stateType)
    {
        var property = ResolveProperty(node, stateType);
        return PostgreSqlJsonbAccess.GetTypedAccessor(property.Name, property.PropertyType);
    }

    /// <summary>
    /// 解析成员表达式对应的属性信息（含基类属性）。
    /// </summary>
    /// <remarks>
    /// Resolves the property information for a member expression (including inherited properties).
    /// </remarks>
    private static PropertyInfo ResolveProperty(MemberExpression node, Type stateType)
    {
        var property = stateType.GetProperty(node.Member.Name, BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
        if (property == null)
        {
            throw new NotSupportedException($"PostgreSqlExpressionTranslator: member '{node.Member.Name}' is not a public instance property of '{stateType.Name}'. Only property accessors of the state type are supported.");
        }

        return property;
    }

    /// <summary>
    /// 求值常量/闭包操作数的运行时值（null 表示 null 常量）。
    /// </summary>
    /// <remarks>
    /// Evaluates the runtime value of a constant or closure operand (null denotes a null constant).
    /// </remarks>
    private static object EvaluateConstantOperand(Expression node)
    {
        var unwrapped = UnwrapConvert(node);
        return unwrapped.NodeType switch
        {
            ExpressionType.Constant     => ((ConstantExpression)unwrapped).Value,
            ExpressionType.MemberAccess => Expression.Lambda(unwrapped).Compile().DynamicInvoke(),
            _                           => throw new NotSupportedException($"PostgreSqlExpressionTranslator: expression operand '{unwrapped}' (node type: {unwrapped.NodeType}) is not a constant or closure value. Use a local constant captured by the lambda, e.g. 'var g = 130; x => x.Group == g'."),
        };
    }

    /// <summary>
    /// 解包 Convert 节点（装箱/可空/枚举转换）。
    /// </summary>
    /// <remarks>
    /// Unwraps Convert nodes (boxing/nullable/enum conversions).
    /// </remarks>
    private static Expression UnwrapConvert(Expression node)
    {
        while (node is UnaryExpression { NodeType: ExpressionType.Convert, } unary)
        {
            node = unary.Operand;
        }

        return node;
    }

    /// <summary>
    /// 创建指名节点与替代写法的 NotSupportedException。
    /// </summary>
    /// <remarks>
    /// Creates a <see cref="NotSupportedException"/> naming the offending node and suggesting alternatives.
    /// </remarks>
    private static NotSupportedException CreateUnsupported(Expression node)
    {
        var description = node == null ? "<null>" : $"{node.NodeType}: {node}";
        return new NotSupportedException($"PostgreSqlExpressionTranslator: expression node '{description}' is outside the supported dialect matrix (property equality/comparison, &&/||/!, null checks, string.Contains/StartsWith/EndsWith, closure constants). Rewrite the filter with supported nodes or fetch by id and filter in memory.");
    }

    /// <summary>
    /// 按 CLR 值类型创建强类型参数（null 直接输出 null 参数；DateTime 强制 UTC timestamptz，枚举按运行时类型统一转 bigint）。
    /// </summary>
    /// <remarks>
/// Creates a strongly typed parameter according to the CLR value type. A <c>null</c> value short-circuits
/// to a null parameter; DateTime is forced to UTC timestamptz; enums are handled once by their runtime
/// type and bound as bigint (the jsonb expression indexes store enums as bigint, so equality predicates
/// must bind the same shape). Premise: closure/constant values reach this method with the enum itself as
/// their runtime type — a value pre-unboxed to the underlying integral type (e.g. <c>object box = (int)E.A</c>)
/// skips the enum branch and binds by its integral runtime type instead.
    /// </remarks>
    private static NpgsqlParameter CreateParameter(string name, object value, Type propertyType)
    {
        if (value == null)
        {
            // null 判定直接输出 null 参数（属性类型分支不再接触 null 值）
            // Null checks bind a plain null parameter; no type conversion is attempted on null.
            return new NpgsqlParameter(name, DBNull.Value);
        }

        var valueUnderlying = Nullable.GetUnderlyingType(value.GetType()) ?? value.GetType();
        if (valueUnderlying.IsEnum)
        {
            // 枚举按值的运行时类型统一转底层整型并绑定为 bigint（与表达式索引的存储形态一致）
            // Enums are converted via the value's runtime underlying type and bound as bigint (matching the expression-index storage shape).
            var converted = Convert.ChangeType(value, Enum.GetUnderlyingType(valueUnderlying), System.Globalization.CultureInfo.InvariantCulture);
            return new NpgsqlParameter(name, NpgsqlDbType.Bigint) { Value = converted, };
        }

        if (value is DateTime dateTime)
        {
            // P1-7：全 UTC 写 timestamptz
            return new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = dateTime.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(dateTime, DateTimeKind.Utc) : dateTime.ToUniversalTime(), };
        }

        if (value is DateTimeOffset dateTimeOffset)
        {
            return new NpgsqlParameter(name, NpgsqlDbType.TimestampTz) { Value = dateTimeOffset.ToUniversalTime(), };
        }

        if (valueUnderlying == typeof(bool))
        {
            return new NpgsqlParameter(name, NpgsqlDbType.Boolean) { Value = value, };
        }

        return new NpgsqlParameter(name, value);
    }
}