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


using System.Collections.Concurrent;
using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace GameFrameX.DataBase.Sqlite;

/// <summary>
/// SQLite 读路径 json_extract 谓词下推（表达式 → SQL 翻译 + 命令文本构建，C328）。
/// </summary>
/// <remarks>
/// Predicate pushdown for the SQLite read path (C328): translates the contract filter's top-level
/// AndAlso conjuncts — plus soft-delete visibility and id comparisons (against the <c>id</c> column) —
/// into exact <c>json_extract(doc, '$."Name")</c> SQL conjuncts. The client-side compiled predicate
/// stays authoritative in the pruned-read path, so the result set is unchanged; a fully-exact
/// translation additionally powers the zero-deserialization COUNT / EXISTS / paged-SELECT fast paths.
/// Exactness rules: a missing JSON key deserializes to <c>default(T)</c> for non-nullable value
/// types, so those members are wrapped in <c>COALESCE(..., 0)</c>; <c>!= v</c> on nullable members
/// renders <c>(extract IS NULL OR extract != v)</c> matching the C# lifted null semantics;
/// <c>== null</c> / <c>!= null</c> become IS NULL / IS NOT NULL on nullable members and constant
/// truth values on non-nullable ones; strings translate equality / inequality only; floating-point
/// members are excluded (text-to-float parse round-trip is not guaranteed bit-exact). Everything
/// else (DateTime, Guid, decimal, nested members, OrElse, method calls, narrowing casts) is left
/// to the authoritative client-side predicate. SQL text composition is centralized in the static
/// builders with the C225 invariant: the table name is the compile-time closed type name, the JSON
/// path literal contains identifier characters only, and every value is bound via a
/// <c>$parameterN</c> placeholder (Sonar S2077).
/// </remarks>
public sealed partial class SqliteDbService
{
    /// <summary>
    /// json_extract 路径字面量前缀（成员名以双引号内嵌，仅标识符字符）。
    /// </summary>
    /// <remarks>
    /// The json_extract path literal prefix (the member name is embedded quoted, identifier characters only).
    /// </remarks>
    private const string JsonExtractPathPrefix = "json_extract(doc, '$.\"";

    /// <summary>
    /// json_extract 路径字面量后缀。
    /// </summary>
    /// <remarks>
    /// The json_extract path literal suffix.
    /// </remarks>
    private const string JsonExtractPathSuffix = "\"')";

    /// <summary>
    /// 下推成员的取值类别。
    /// </summary>
    /// <remarks>
    /// The value category of a pushdown-eligible member.
    /// </remarks>
    private enum JsonExtractMemberKind
    {
        /// <summary>
        /// 主键列（Id 成员映射 id 列而非 JSON 路径）。
        /// </summary>
        /// <remarks>
        /// The primary-key column (the Id member maps to the id column, not a JSON path).
        /// </remarks>
        IdColumn,

        /// <summary>
        /// 字符串（仅等值 / 不等值可翻译）。
        /// </summary>
        /// <remarks>
        /// A string member (only equality / inequality translate).
        /// </remarks>
        String,

        /// <summary>
        /// 布尔（JSON true/false 与 1/0 同形）。
        /// </summary>
        /// <remarks>
        /// A bool member (JSON true/false is isomorphic to 1/0).
        /// </remarks>
        Bool,

        /// <summary>
        /// 整型含数值底层枚举（ulong 排除，常量 checked 转 long 绑定）。
        /// </summary>
        /// <remarks>
        /// An integral member including numeric-backed enums (ulong excluded; constants convert checked to long for binding).
        /// </remarks>
        Integer,
    }

    /// <summary>
    /// 单个下推成员的翻译元数据。
    /// </summary>
    /// <remarks>
    /// The translation metadata of a single pushdown-eligible member.
    /// </remarks>
    private sealed class JsonExtractMemberTranslation
    {
        /// <summary>
        /// 取值类别。
        /// </summary>
        /// <remarks>
        /// The value category.
        /// </remarks>
        public JsonExtractMemberKind Kind;

        /// <summary>
        /// 是否可空（Nullable&lt;T&gt; 或引用类型；SQL NULL 语义与 C# 提升语义同形，无需 COALESCE）。
        /// </summary>
        /// <remarks>
        /// Whether the member is nullable (Nullable&lt;T&gt; or a reference type; SQL NULL semantics
        /// already match the lifted C# semantics, so no COALESCE is needed).
        /// </remarks>
        public bool IsNullable;

        /// <summary>
        /// JSON 路径提取表达式文本（IdColumn 时为空）。
        /// </summary>
        /// <remarks>
        /// The JSON extract expression text (empty for the IdColumn kind).
        /// </remarks>
        public string ExtractExpression = string.Empty;
    }

    /// <summary>
    /// 一次 filter → WHERE 翻译的结果。
    /// </summary>
    /// <remarks>
    /// The result of one filter → WHERE translation.
    /// </remarks>
    private sealed class JsonExtractWhereTranslation
    {
        /// <summary>
        /// 合取项以 " AND " 连接的 WHERE 主体（空串 = 无可下推合取项）。
        /// </summary>
        /// <remarks>
        /// The AND-joined conjuncts (empty when nothing could be pushed down).
        /// </remarks>
        public string WhereSql = string.Empty;

        /// <summary>
        /// 整个谓词（结构 + 全部合取项 + 软删可见性）是否被 SQL 精确表达（启用零反序列化快路径）。
        /// </summary>
        /// <remarks>
        /// Whether the whole predicate (structure, every conjunct and soft-delete visibility) is
        /// exactly represented in SQL (enabling the zero-deserialization fast paths).
        /// </remarks>
        public bool IsFilterExact;

        /// <summary>
        /// 与 $parameter0.. 占位符一一对应的绑定值。
        /// </summary>
        /// <remarks>
        /// The bound values matching the $parameter0.. placeholders one-to-one.
        /// </remarks>
        public readonly List<object> ParameterValues = new();

        /// <summary>
        /// 是否有可下推的合取项。
        /// </summary>
        /// <remarks>
        /// Whether any conjunct could be pushed down.
        /// </remarks>
        public bool HasConjuncts
        {
            get { return WhereSql.Length > 0; }
        }
    }

    /// <summary>
    /// 成员元数据进程级缓存（PropertyInfo 键；哨兵值 = 不可翻译，类型可卸载不受强引用）。
    /// </summary>
    /// <remarks>
    /// The process-wide member metadata cache (keyed by PropertyInfo; a sentinel value marks
    /// untranslatable members, and ConditionalWeakTable keeps collectible types unloadable).
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<PropertyInfo, JsonExtractMemberTranslation> MemberTranslationCache = new();

    /// <summary>
    /// 软删可见性合取项进程级缓存（状态类型键；null 哨兵 = 不可精确表达）。
    /// </summary>
    /// <remarks>
    /// The process-wide soft-delete visibility cache (keyed by state type; a null sentinel marks
    /// types whose visibility is not exactly expressible).
    /// </remarks>
    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Type, string> SoftDeleteVisibilityCache = new();

    /// <summary>
    /// 不可翻译成员的哨兵值（ConditionalWeakTable 不接受 null 值）。
    /// </summary>
    /// <remarks>
    /// The untranslatable-member sentinel (ConditionalWeakTable does not accept null values).
    /// </remarks>
    private static readonly JsonExtractMemberTranslation UntranslatableMember = new() { Kind = JsonExtractMemberKind.String };

    /// <summary>
    /// 不可精确表达可见性的哨兵值。
    /// </summary>
    /// <remarks>
    /// The not-exactly-expressible-visibility sentinel.
    /// </remarks>
    private static readonly string UntranslatableVisibility = string.Empty;

    /// <summary>
    /// 将 filter（含软删可见性）翻译为 json_extract WHERE 下推；不可翻译的合取项留给客户端权威谓词。
    /// </summary>
    /// <remarks>
    /// Translates the filter (plus soft-delete visibility) into json_extract WHERE pushdown conjuncts;
    /// untranslatable conjuncts stay with the authoritative client-side predicate.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="filter">契约过滤表达式 / The contract filter</param>
    /// <param name="includeDeleted">是否包含软删数据（false 时追加可见性合取项）/ Whether to include soft-deleted documents</param>
    /// <returns>翻译结果 / The translation</returns>
    private static JsonExtractWhereTranslation TranslateFilterToWhere<TState>(Expression<Func<TState, bool>> filter, bool includeDeleted) where TState : BaseCacheState, new()
    {
        var translation = new JsonExtractWhereTranslation();
        if (!IsJsonPushdownSerializerCompatible())
        {
            translation.IsFilterExact = false;
            return translation;
        }

        var conjuncts = new List<string>();
        var isExact = true;
        if (filter != null && !TryTranslateAndAlsoChain(filter.Body, filter.Parameters[0], translation, conjuncts))
        {
            isExact = false;
        }

        // 可见性合取项置末：SQLite 按序短路 AND，选择性高的业务合取项先淘汰行，减少 doc 解析次数。
        // The visibility conjunct goes last: SQLite short-circuits AND left to right, so the more
        // selective business conjuncts reject rows first and fewer documents get parsed.
        if (!includeDeleted)
        {
            var visibilityConjunct = ResolveSoftDeleteVisibilityConjunct(typeof(TState));
            if (ReferenceEquals(visibilityConjunct, UntranslatableVisibility))
            {
                isExact = false;
            }
            else if (visibilityConjunct != null)
            {
                conjuncts.Add(visibilityConjunct);
            }
        }

        translation.WhereSql = conjuncts.Count == 0 ? string.Empty : string.Join(" AND ", conjuncts);
        translation.IsFilterExact = isExact;
        return translation;
    }

    /// <summary>
    /// 序列化选项兼容性闸：命名策略 / 转换器 / 宽松数值任一启用即整体不翻译（保守回退内存）。
    /// </summary>
    /// <remarks>
    /// Serializer-options compatibility gate: any naming policy, converter or relaxed number
    /// handling disables translation entirely (a conservative in-memory fallback).
    /// </remarks>
    /// <returns>当前序列化选项是否可安全按属性名推导 JSON 键 / Whether JSON keys can be safely derived from property names</returns>
    private static bool IsJsonPushdownSerializerCompatible()
    {
        return DocumentJsonOptions.PropertyNamingPolicy == null
               && DocumentJsonOptions.Converters.Count == 0
               && DocumentJsonOptions.NumberHandling == JsonNumberHandling.Strict
               && !DocumentJsonOptions.PropertyNameCaseInsensitive;
    }

    /// <summary>
    /// 解析状态类型的软删可见性合取项（缓存）：可见 = IsDeleted 缺失 / null / false。
    /// </summary>
    /// <remarks>
    /// Resolves (with caching) the soft-delete visibility conjunct for a state type:
    /// visible = IsDeleted missing / null / false (a JSON false compares equal to 0).
    /// </remarks>
    /// <param name="stateType">缓存状态类型 / The state type</param>
    /// <returns>可见性 SQL 合取项；null = IsDeleted 未序列化（恒可见，无需合取项）；空串哨兵 = 不可精确表达 / The conjunct, null when IsDeleted is never serialized (always visible), or the empty-string sentinel when not exactly expressible</returns>
    private static string ResolveSoftDeleteVisibilityConjunct(Type stateType)
    {
        return SoftDeleteVisibilityCache.GetValue(stateType, static type =>
        {
            var property = type.GetProperty("IsDeleted", BindingFlags.Public | BindingFlags.Instance);
            if (property == null)
            {
                return null;
            }

            var metadata = ResolveJsonMemberTranslation(property);
            if (metadata == null)
            {
                return UntranslatableVisibility;
            }

            if (metadata.Kind != JsonExtractMemberKind.Bool)
            {
                return UntranslatableVisibility;
            }

            // 单次提取：缺失 / null → 0、false → 0、true → 1；= 0 即可见（与 IS NULL OR = 0 逐行等价）。
            // A single extraction: missing / null → 0, false → 0, true → 1; = 0 means visible (row-for-row equivalent to IS NULL OR = 0).
            return "COALESCE(" + metadata.ExtractExpression + ", 0) = 0";
        });
    }

    /// <summary>
    /// 递归翻译顶层 AndAlso 合取链（任一项失败即整体不精确，已翻译项仍下推）。
    /// </summary>
    /// <remarks>
    /// Recursively translates the top-level AndAlso conjunct chain (one failure marks the whole
    /// translation inexact; the translated conjuncts are still pushed down).
    /// </remarks>
    /// <param name="node">当前表达式节点 / The current node</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <param name="translation">翻译结果承载 / The translation being built</param>
    /// <param name="conjuncts">已翻译合取项集合 / The translated conjuncts</param>
    /// <returns>整个链是否全部精确翻译 / Whether the whole chain translated exactly</returns>
    private static bool TryTranslateAndAlsoChain(Expression node, ParameterExpression parameter, JsonExtractWhereTranslation translation, List<string> conjuncts)
    {
        if (node is BinaryExpression { NodeType: ExpressionType.AndAlso } andAlso)
        {
            var leftExact = TryTranslateAndAlsoChain(andAlso.Left, parameter, translation, conjuncts);
            var rightExact = TryTranslateAndAlsoChain(andAlso.Right, parameter, translation, conjuncts);
            return leftExact && rightExact;
        }

        return TryTranslateConjunct(node, parameter, translation, conjuncts);
    }

    /// <summary>
    /// 翻译单个合取项：成员-常量比较（六运算符）、裸非可空 bool 成员真值或常量布尔真值；不可翻译返回 false。
    /// </summary>
    /// <remarks>
    /// Translates a single conjunct: a member-vs-constant comparison (six operators), a bare
    /// non-nullable bool member truthiness, or a literal bool constant; returns false when untranslatable.
    /// </remarks>
    /// <param name="node">合取项表达式 / The conjunct</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <param name="translation">翻译结果承载 / The translation being built</param>
    /// <param name="conjuncts">已翻译合取项集合 / The translated conjuncts</param>
    /// <returns>是否精确翻译 / Whether the conjunct translated exactly</returns>
    private static bool TryTranslateConjunct(Expression node, ParameterExpression parameter, JsonExtractWhereTranslation translation, List<string> conjuncts)
    {
        // 字面量布尔：true 恒真（无合取项）、false 恒假（SQL 常量假）。
        // Literal booleans: true is a tautology (no conjunct), false is a SQL constant-false.
        if (node is ConstantExpression { Value: bool literalValue })
        {
            if (literalValue)
            {
                return true;
            }

            conjuncts.Add("1 = 0");
            return true;
        }

        // 裸非可空 bool 成员真值：x.Flag 等价 x.Flag == true（可空形态必须显式比较）。
        // A bare non-nullable bool member is truthiness — equivalent to x.Flag == true (nullable forms must compare explicitly).
        if (node is MemberExpression bareMember && TryGetParameterMember(bareMember, parameter, out var bareResolved))
        {
            var bareMetadata = ResolveJsonMemberTranslation(bareResolved.Member as PropertyInfo);
            if (bareMetadata != null && bareMetadata.Kind == JsonExtractMemberKind.Bool && !bareMetadata.IsNullable)
            {
                conjuncts.Add("COALESCE(" + bareMetadata.ExtractExpression + ", 0) = 1");
                return true;
            }

            return false;
        }

        if (node is not BinaryExpression binary)
        {
            return false;
        }

        var operatorType = binary.NodeType;
        if (operatorType is not (ExpressionType.Equal or ExpressionType.NotEqual
            or ExpressionType.GreaterThan or ExpressionType.GreaterThanOrEqual
            or ExpressionType.LessThan or ExpressionType.LessThanOrEqual))
        {
            return false;
        }

        // 字符串 ==/!= 使用 String.op_Equality（序数）；其余自定义运算符重载不翻译。
        // String ==/!= uses String.op_Equality (ordinal); any other operator overload is untranslatable.
        if (binary.Method != null && binary.Method.DeclaringType != typeof(string))
        {
            return false;
        }

        MemberExpression memberSide;
        Expression constantSide;
        if (TryGetParameterMember(binary.Left, parameter, out var leftMember) && !ContainsParameter(binary.Right, parameter))
        {
            memberSide = leftMember;
            constantSide = binary.Right;
        }
        else if (TryGetParameterMember(binary.Right, parameter, out var rightMember) && !ContainsParameter(binary.Left, parameter))
        {
            memberSide = rightMember;
            constantSide = binary.Left;
            operatorType = MirrorComparisonOperator(operatorType);
        }
        else
        {
            return false;
        }

        var metadata = ResolveJsonMemberTranslation(memberSide.Member as PropertyInfo);
        if (metadata == null)
        {
            return false;
        }

        if (metadata.Kind == JsonExtractMemberKind.String
            && operatorType is not (ExpressionType.Equal or ExpressionType.NotEqual))
        {
            return false;
        }

        if (!TryEvaluateClosureConstant(constantSide, parameter, out var constantValue))
        {
            return false;
        }

        // Id 成员走 id 列（与 doc 内 Id 键同步，比较运算符直传）。
        // The Id member maps to the id column (kept in sync with the doc's Id key); operators pass through.
        if (metadata.Kind == JsonExtractMemberKind.IdColumn)
        {
            if (constantValue == null || !TryConvertToLong(constantValue, out var idValue))
            {
                return false;
            }

            conjuncts.Add("id " + OperatorSql(operatorType) + " " + AppendParameter(translation, idValue));
            return true;
        }

        // 与 null 比较：可空成员 → IS [NOT] NULL；非可空值类型成员 → 常量真值（恒非 null）。
        // Comparisons against null: nullable members → IS [NOT] NULL; non-nullable value-type members → constant truth values.
        if (constantValue == null)
        {
            switch (operatorType)
            {
                case ExpressionType.Equal:
                    if (metadata.IsNullable)
                    {
                        conjuncts.Add(metadata.ExtractExpression + " IS NULL");
                        return true;
                    }

                    conjuncts.Add("1 = 0");
                    return true;
                case ExpressionType.NotEqual:
                    if (metadata.IsNullable)
                    {
                        conjuncts.Add(metadata.ExtractExpression + " IS NOT NULL");
                        return true;
                    }

                    return true;
                default:
                    return false;
            }
        }

        if (!TryNormalizeConstant(metadata.Kind, constantValue, out var boundValue))
        {
            return false;
        }

        conjuncts.Add(BuildComparisonConjunct(metadata, operatorType, AppendParameter(translation, boundValue)));
        return true;
    }

    /// <summary>
    /// 按成员可空性构建比较合取项（非可空值类型 COALESCE 兜底缺键 = default(T)）。
    /// </summary>
    /// <remarks>
    /// Builds the comparison conjunct per member nullability (non-nullable value types get a
    /// COALESCE fallback for a missing key, which deserializes to default(T)).
    /// </remarks>
    /// <param name="metadata">成员翻译元数据 / The member metadata</param>
    /// <param name="operatorType">比较运算符（成员在左的归一形态）/ The comparison operator (normalized, member on the left)</param>
    /// <param name="parameterName">绑定参数占位符 / The bound parameter placeholder</param>
    /// <returns>SQL 合取项 / The SQL conjunct</returns>
    private static string BuildComparisonConjunct(JsonExtractMemberTranslation metadata, ExpressionType operatorType, string parameterName)
    {
        var extract = metadata.ExtractExpression;
        var coalesced = metadata.IsNullable ? extract : "COALESCE(" + extract + ", 0)";
        switch (operatorType)
        {
            case ExpressionType.Equal:
                return coalesced + " = " + parameterName;
            case ExpressionType.NotEqual:
                // 可空成员：C# 提升语义 null != v 为 true，SQL 需显式放行 NULL 行。
                // Nullable members: C# lifted semantics make null != v true, so SQL must admit NULL rows explicitly.
                return metadata.IsNullable
                    ? "(" + extract + " IS NULL OR " + extract + " != " + parameterName + ")"
                    : coalesced + " != " + parameterName;
            default:
                return coalesced + " " + OperatorSql(operatorType) + " " + parameterName;
        }
    }

    /// <summary>
    /// 映射比较运算符为 SQL 文本。
    /// </summary>
    /// <remarks>
    /// Maps a comparison operator to its SQL text.
    /// </remarks>
    /// <param name="operatorType">比较运算符 / The comparison operator</param>
    /// <returns>SQL 运算符文本 / The SQL operator text</returns>
    private static string OperatorSql(ExpressionType operatorType)
    {
        switch (operatorType)
        {
            case ExpressionType.Equal:
                return "=";
            case ExpressionType.NotEqual:
                return "!=";
            case ExpressionType.GreaterThan:
                return ">";
            case ExpressionType.GreaterThanOrEqual:
                return ">=";
            case ExpressionType.LessThan:
                return "<";
            case ExpressionType.LessThanOrEqual:
                return "<=";
            default:
                throw new InvalidOperationException("Unsupported comparison operator: " + operatorType);
        }
    }

    /// <summary>
    /// 追加一个绑定参数并返回其占位符名。
    /// </summary>
    /// <remarks>
    /// Appends one bound parameter and returns its placeholder name.
    /// </remarks>
    /// <param name="translation">翻译结果承载 / The translation being built</param>
    /// <param name="value">绑定值 / The bound value</param>
    /// <returns>$parameterN 占位符 / The $parameterN placeholder</returns>
    private static string AppendParameter(JsonExtractWhereTranslation translation, object value)
    {
        var name = "$parameter" + translation.ParameterValues.Count.ToString(CultureInfo.InvariantCulture);
        translation.ParameterValues.Add(value);
        return name;
    }

    /// <summary>
    /// 将常量归一为绑定值（bool→1/0、整型/枚举 checked→long；字符串原样）。
    /// </summary>
    /// <remarks>
    /// Normalizes a constant into its bound value (bool→1/0, integral/enum checked→long; strings as-is).
    /// </remarks>
    /// <param name="memberKind">成员取值类别 / The member value category</param>
    /// <param name="constantValue">原始常量值 / The raw constant value</param>
    /// <param name="boundValue">归一后的绑定值 / The normalized bound value</param>
    /// <returns>是否可绑定 / Whether the value is bindable</returns>
    private static bool TryNormalizeConstant(JsonExtractMemberKind memberKind, object constantValue, out object boundValue)
    {
        boundValue = null;
        try
        {
            switch (memberKind)
            {
                case JsonExtractMemberKind.Bool:
                    boundValue = Convert.ToBoolean(constantValue, CultureInfo.InvariantCulture) ? 1L : 0L;
                    return true;
                case JsonExtractMemberKind.Integer:
                    if (!TryConvertToLong(constantValue, out var integralBoundValue))
                    {
                        return false;
                    }

                    boundValue = integralBoundValue;
                    return true;
                case JsonExtractMemberKind.String:
                    boundValue = constantValue as string;
                    return boundValue != null;
                default:
                    return false;
            }
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>
    /// checked 语义转 long（ulong 超界等溢出即不翻译）。
    /// </summary>
    /// <remarks>
    /// Converts to long with checked semantics (overflow — e.g. a large ulong — makes it untranslatable).
    /// </remarks>
    /// <param name="value">原始值 / The raw value</param>
    /// <param name="longValue">转换结果 / The converted value</param>
    /// <returns>是否转换成功 / Whether the conversion succeeded</returns>
    private static bool TryConvertToLong(object value, out long longValue)
    {
        try
        {
            longValue = Convert.ToInt64(value, CultureInfo.InvariantCulture);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
        {
            longValue = 0;
            return false;
        }
    }

    /// <summary>
    /// 成员在右侧时镜像比较运算符。
    /// </summary>
    /// <remarks>
    /// Mirrors the comparison operator when the member is on the right.
    /// </remarks>
    /// <param name="operatorType">原运算符 / The original operator</param>
    /// <returns>镜像运算符 / The mirrored operator</returns>
    private static ExpressionType MirrorComparisonOperator(ExpressionType operatorType)
    {
        switch (operatorType)
        {
            case ExpressionType.GreaterThan:
                return ExpressionType.LessThan;
            case ExpressionType.GreaterThanOrEqual:
                return ExpressionType.LessThanOrEqual;
            case ExpressionType.LessThan:
                return ExpressionType.GreaterThan;
            case ExpressionType.LessThanOrEqual:
                return ExpressionType.GreaterThanOrEqual;
            default:
                return operatorType;
        }
    }

    /// <summary>
    /// 提取参数的直接成员访问：剥 Convert/ConvertChecked，仅放行无损数值转换（整型/枚举底层加宽与可空包装；显式收窄即拒绝）。
    /// </summary>
    /// <remarks>
    /// Extracts a direct member access on the parameter: Convert/ConvertChecked nodes are unwrapped,
    /// admitting only lossless numeric conversions (integral/enum-underlying widening and nullable
    /// wrapping; an explicit narrowing cast is rejected).
    /// </remarks>
    /// <param name="expression">待检表达式 / The expression to inspect</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <param name="member">参数成员访问 / The parameter member access</param>
    /// <returns>是否为参数成员访问 / Whether it is a parameter member access</returns>
    private static bool TryGetParameterMember(Expression expression, ParameterExpression parameter, out MemberExpression member)
    {
        member = null;
        var current = expression;
        while (current is UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert)
        {
            if (!IsLosslessNumericConversion(convert.Operand.Type, convert.Type))
            {
                return false;
            }

            current = convert.Operand;
        }

        if (current is not MemberExpression candidate || candidate.Expression != parameter)
        {
            return false;
        }

        member = candidate;
        return true;
    }

    /// <summary>
    /// 判定数值 Convert 是否无损（源/目标剥 Nullable 后：同型、或整型/枚举底层按 C# 隐式加宽表收窄即无损；浮点/decimal 一律非无损）。
    /// </summary>
    /// <remarks>
    /// Determines whether a numeric Convert is lossless (source/target nullable-unwrapped: same type,
    /// or an integral/enum-underlying widening per the C# implicit table — anything else, including
    /// floating-point and decimal targets, is lossy and rejected).
    /// </remarks>
    /// <param name="sourceType">源类型 / The source type</param>
    /// <param name="targetType">目标类型 / The target type</param>
    /// <returns>是否无损 / Whether the conversion is lossless</returns>
    private static bool IsLosslessNumericConversion(Type sourceType, Type targetType)
    {
        var source = Nullable.GetUnderlyingType(sourceType) ?? sourceType;
        var target = Nullable.GetUnderlyingType(targetType) ?? targetType;

        // 装箱 Convert（object 目标）无损，直接放行（收窄转换会在剥完装箱后逐层复核）。
        // A boxing Convert (an object target) is lossless and admitted (a narrowing cast underneath
        // is still checked layer by layer after unwrapping).
        if (target == typeof(object))
        {
            return true;
        }

        if (source.IsEnum)
        {
            source = Enum.GetUnderlyingType(source);
        }

        if (target.IsEnum)
        {
            target = Enum.GetUnderlyingType(target);
        }

        if (source == target)
        {
            return true;
        }

        // C# 隐式整型加宽表（checked 下无损；uint→long 覆盖、ulong 目标排除在成员白名单外）。
        // The C# implicit integral widening table (lossless under checked semantics; uint→long covered, ulong targets are excluded by the member whitelist).
        return source == typeof(sbyte) && (target == typeof(short) || target == typeof(int) || target == typeof(long))
               || source == typeof(byte) && (target == typeof(short) || target == typeof(ushort) || target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong))
               || source == typeof(short) && (target == typeof(int) || target == typeof(long))
               || source == typeof(ushort) && (target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong))
               || source == typeof(int) && (target == typeof(long))
               || source == typeof(uint) && (target == typeof(long) || target == typeof(ulong))
               || source == typeof(char) && (target == typeof(ushort) || target == typeof(int) || target == typeof(uint) || target == typeof(long) || target == typeof(ulong));
    }

    /// <summary>
    /// 解析属性翻译元数据（进程级缓存；public 读写、无序列化排除特性、JSON 名标识符安全、类型白名单）。
    /// </summary>
    /// <remarks>
    /// Resolves a property's translation metadata (process-wide cache; requires public read/write,
    /// no serialization-excluding attributes, an identifier-safe JSON name and a whitelisted type).
    /// </remarks>
    /// <param name="property">属性；null 直接不可翻译 / The property; null is untranslatable outright</param>
    /// <returns>翻译元数据；不可翻译返回 null / The metadata, or null when untranslatable</returns>
    private static JsonExtractMemberTranslation ResolveJsonMemberTranslation(PropertyInfo property)
    {
        if (property == null)
        {
            return null;
        }

        var cached = MemberTranslationCache.GetValue(property, static member =>
        {
            var resolved = TryResolveJsonMember(member, out var metadata) ? metadata : UntranslatableMember;
            return resolved;
        });

        // 缓存哨兵只用于承载「不可翻译」（ConditionalWeakTable 不接受 null 值）；对外一律暴露 null，
        // 否则调用方会把不可翻译成员当作字符串成员下推，拼出非法 SQL 或恒假合取项。
        // The cache sentinel only carries "untranslatable" (ConditionalWeakTable rejects null values);
        // null is exposed to callers, otherwise an untranslatable member would be pushed down as a string
        // member, producing invalid SQL or a constant-false conjunct.
        return ReferenceEquals(cached, UntranslatableMember) ? null : cached;
    }

    /// <summary>
    /// 解析单个属性是否可翻译（public 读写、无序列化排除特性、JSON 名标识符安全、类型白名单）。
    /// </summary>
    /// <remarks>
    /// Resolves whether a property is translatable (public read/write, no serialization-excluding
    /// attributes, identifier-safe JSON name, type whitelist).
    /// </remarks>
    /// <param name="property">属性 / The property</param>
    /// <param name="metadata">翻译元数据 / The metadata</param>
    /// <returns>是否可翻译 / Whether the property is translatable</returns>
    private static bool TryResolveJsonMember(PropertyInfo property, out JsonExtractMemberTranslation metadata)
    {
        metadata = null;
        if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length != 0)
        {
            return false;
        }

        var accessor = property.GetGetMethod();
        var mutator = property.GetSetMethod();
        if (accessor == null || mutator == null || accessor.IsStatic || mutator.IsStatic)
        {
            return false;
        }

        // Id 成员优先映射 id 列（写路径同步维护，见 C222 存储模型）。
        // The Id member maps to the id column first (kept in sync by the write path, per the C222 storage model).
        var propertyType = property.PropertyType;
        var underlyingType = Nullable.GetUnderlyingType(propertyType);
        var effectiveType = underlyingType ?? propertyType;
        if (property.Name == "Id" && effectiveType == typeof(long))
        {
            metadata = new JsonExtractMemberTranslation { Kind = JsonExtractMemberKind.IdColumn, IsNullable = false };
            return true;
        }

        if (property.GetCustomAttribute<JsonIgnoreAttribute>() != null
            || property.GetCustomAttribute<JsonConverterAttribute>() != null
            || property.GetCustomAttribute<JsonNumberHandlingAttribute>() != null)
        {
            return false;
        }

        var jsonName = property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name ?? property.Name;
        if (jsonName.Length == 0 || !jsonName.All(IsJsonIdentifierCharacter))
        {
            return false;
        }

        var isNullable = underlyingType != null || !propertyType.IsValueType;
        var extractExpression = JsonExtractPathPrefix + jsonName + JsonExtractPathSuffix;
        if (effectiveType == typeof(string))
        {
            metadata = new JsonExtractMemberTranslation { Kind = JsonExtractMemberKind.String, IsNullable = true, ExtractExpression = extractExpression };
            return true;
        }

        if (effectiveType == typeof(bool))
        {
            metadata = new JsonExtractMemberTranslation { Kind = JsonExtractMemberKind.Bool, IsNullable = isNullable, ExtractExpression = extractExpression };
            return true;
        }

        if (IsSupportedIntegralOrEnumType(effectiveType))
        {
            metadata = new JsonExtractMemberTranslation { Kind = JsonExtractMemberKind.Integer, IsNullable = isNullable, ExtractExpression = extractExpression };
            return true;
        }

        return false;
    }

    /// <summary>
    /// 可翻译整型/枚举白名单（JSON 按数值序列化；ulong 排除——checked 转 long 可能溢出；浮点/decimal 排除——文本往返不保证位精确）。
    /// </summary>
    /// <remarks>
    /// The translatable integral/enum whitelist (JSON serializes them numerically; ulong is excluded —
    /// the checked conversion to long may overflow; floating-point and decimal are excluded — the
    /// text round-trip is not guaranteed bit-exact).
    /// </remarks>
    /// <param name="type">成员有效类型（已剥 Nullable）/ The member's effective type (nullable unwrapped)</param>
    /// <returns>是否支持 / Whether the type is supported</returns>
    private static bool IsSupportedIntegralOrEnumType(Type type)
    {
        if (type.IsEnum)
        {
            return Enum.GetUnderlyingType(type) != typeof(ulong);
        }

        return type == typeof(sbyte) || type == typeof(byte)
               || type == typeof(short) || type == typeof(ushort)
               || type == typeof(int) || type == typeof(uint) || type == typeof(long);
    }

    /// <summary>
    /// JSON 键标识符字符（字母 / 数字 / 下划线；路径字面量仅此构成，S2077）。
    /// </summary>
    /// <remarks>
    /// A JSON-key identifier character (letter / digit / underscore; the path literal is composed of these only, S2077).
    /// </remarks>
    /// <param name="character">待检字符 / The character</param>
    /// <returns>是否标识符字符 / Whether it is an identifier character</returns>
    private static bool IsJsonIdentifierCharacter(char character)
    {
        return char.IsLetterOrDigit(character) || character == '_';
    }

    /// <summary>
    /// 求值常量侧表达式（常量 / 闭包捕获字段属性链 / Convert，纯求值无方法调用副作用；引用参数即不翻译）。
    /// </summary>
    /// <remarks>
    /// Evaluates the constant side (constants, closure-captured field/property chains and Converts —
    /// a pure evaluation with no method-call side effects); referencing the lambda parameter makes it untranslatable.
    /// </remarks>
    /// <param name="expression">常量侧表达式 / The constant-side expression</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <param name="value">求值结果 / The evaluated value</param>
    /// <returns>是否可求值 / Whether it evaluated</returns>
    private static bool TryEvaluateClosureConstant(Expression expression, ParameterExpression parameter, out object value)
    {
        value = null;
        if (ContainsParameter(expression, parameter))
        {
            return false;
        }

        try
        {
            return TryEvaluatePureExpression(expression, out value);
        }
        catch
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// 递归纯求值（Constant / 静态或闭包实例字段属性 / Convert 数值转换；方法调用等不纯节点返回 false 交回内存过滤）。
    /// </summary>
    /// <remarks>
    /// Recursive pure evaluation (Constant / static or closure-instance fields and properties /
    /// numeric Converts); impure nodes such as method calls return false and fall back to memory filtering.
    /// </remarks>
    /// <param name="expression">待求值表达式 / The expression to evaluate</param>
    /// <param name="value">求值结果 / The evaluated value</param>
    /// <returns>是否纯可求值 / Whether it evaluated purely</returns>
    private static bool TryEvaluatePureExpression(Expression expression, out object value)
    {
        value = null;
        switch (expression)
        {
            case ConstantExpression constant:
                value = constant.Value;
                return true;
            case MemberExpression member:
                object target;
                if (member.Expression == null)
                {
                    target = null;
                }
                else if (!TryEvaluatePureExpression(member.Expression, out target))
                {
                    return false;
                }

                switch (member.Member)
                {
                    case FieldInfo field:
                        value = field.GetValue(target);
                        return true;
                    case PropertyInfo property:
                        value = property.GetValue(target);
                        return true;
                    default:
                        return false;
                }

            case UnaryExpression { NodeType: ExpressionType.Convert or ExpressionType.ConvertChecked } convert:
                if (!TryEvaluatePureExpression(convert.Operand, out var operandValue))
                {
                    return false;
                }

                value = ConvertValue(operandValue, convert.Type);
                return value != null;
            default:
                return false;
        }
    }

    /// <summary>
    /// 复刻 C# Convert 转换语义（剥 Nullable 后 Convert.ChangeType；枚举目标 Enum.ToObject）。
    /// </summary>
    /// <remarks>
    /// Replicates the C# Convert semantics (Convert.ChangeType after nullable unwrapping; Enum.ToObject for enum targets).
    /// </remarks>
    /// <param name="value">原始值 / The raw value</param>
    /// <param name="targetType">目标类型 / The target type</param>
    /// <returns>转换结果；null 表示转换失败 / The converted value, or null on failure</returns>
    private static object ConvertValue(object value, Type targetType)
    {
        try
        {
            // 枚举源先归一到底层类型（Convert.ChangeType 不接受枚举目标/源）。
            // An enum source normalizes to its underlying type first (Convert.ChangeType accepts neither enum sources nor enum targets).
            if (value != null && value.GetType().IsEnum)
            {
                value = Convert.ChangeType(value, Enum.GetUnderlyingType(value.GetType()), CultureInfo.InvariantCulture);
            }

            var effectiveTarget = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (effectiveTarget.IsEnum)
            {
                return Enum.ToObject(effectiveTarget, value);
            }

            if (value is IConvertible)
            {
                return Convert.ChangeType(value, effectiveTarget, CultureInfo.InvariantCulture);
            }

            return null;
        }
        catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// 判定表达式是否引用 lambda 参数。
    /// </summary>
    /// <remarks>
    /// Determines whether the expression references the lambda parameter.
    /// </remarks>
    /// <param name="expression">待检表达式 / The expression to inspect</param>
    /// <param name="parameter">lambda 参数 / The lambda parameter</param>
    /// <returns>是否引用参数 / Whether the parameter is referenced</returns>
    private static bool ContainsParameter(Expression expression, ParameterExpression parameter)
    {
        var detector = new ParameterReferenceDetector(parameter);
        detector.Visit(expression);
        return detector.Found;
    }

    /// <summary>
    /// lambda 参数引用探测器。
    /// </summary>
    /// <remarks>
    /// The lambda-parameter reference detector.
    /// </remarks>
    private sealed class ParameterReferenceDetector : ExpressionVisitor
    {
        /// <summary>
        /// 目标参数。
        /// </summary>
        /// <remarks>
        /// The target parameter.
        /// </remarks>
        private readonly ParameterExpression _parameter;

        /// <summary>
        /// 是否发现目标参数引用。
        /// </summary>
        /// <remarks>
        /// Whether the target parameter was referenced.
        /// </remarks>
        public bool Found;

        /// <summary>
        /// 构造探测器。
        /// </summary>
        /// <remarks>
        /// Creates the detector.
        /// </remarks>
        /// <param name="parameter">目标参数 / The target parameter</param>
        public ParameterReferenceDetector(ParameterExpression parameter)
        {
            _parameter = parameter;
        }

        /// <summary>
        /// 访问参数节点，命中目标参数时置位。
        /// </summary>
        /// <remarks>
        /// Visits a parameter node and flags the target parameter when found.
        /// </remarks>
        /// <param name="node">参数节点 / The parameter node</param>
        /// <returns>原节点 / The original node</returns>
        protected override Expression VisitParameter(ParameterExpression node)
        {
            if (node == _parameter)
            {
                Found = true;
            }

            return base.VisitParameter(node);
        }
    }

    /// <summary>
    /// 翻译排序键（整型/枚举/bool/id 列；string 因 CultureInfo 排序口径与 BINARY 不一致不回推）。
    /// </summary>
    /// <remarks>
    /// Translates the sort key (integral / enum / bool / the id column; strings are never pushed down
    /// because the CultureInfo comparer order differs from SQLite's BINARY collation). The trailing
    /// <c>id ASC</c> tiebreaker replicates the in-memory stable sort's read (rowid) order, and SQLite's
    /// default NULL placement (first ascending / last descending) matches the Mongo-aligned comparers.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="sortExpression">排序表达式 / The sort expression</param>
    /// <param name="descending">是否降序 / Whether descending</param>
    /// <param name="orderBySql">ORDER BY 子句 / The ORDER BY clause</param>
    /// <returns>是否可翻译 / Whether the sort key translated</returns>
    private static bool TryTranslateSortMember<TState>(Expression<Func<TState, object>> sortExpression, bool descending, out string orderBySql) where TState : BaseCacheState, new()
    {
        orderBySql = null;
        if (!IsJsonPushdownSerializerCompatible() || sortExpression == null)
        {
            return false;
        }

        if (!TryGetParameterMember(sortExpression.Body, sortExpression.Parameters[0], out var member))
        {
            return false;
        }

        var metadata = ResolveJsonMemberTranslation(member.Member as PropertyInfo);
        if (metadata == null || metadata.Kind == JsonExtractMemberKind.String)
        {
            return false;
        }

        var keyExpression = metadata.Kind == JsonExtractMemberKind.IdColumn
            ? "id"
            : metadata.IsNullable ? metadata.ExtractExpression : "COALESCE(" + metadata.ExtractExpression + ", 0)";
        orderBySql = keyExpression + (descending ? " DESC" : " ASC") + ", id ASC";
        return true;
    }

    /// <summary>
    /// 构建裁剪读命令（WHERE = 翻译合取项 + ORDER BY id 复刻原行序；调用方须在闸内创建命令）。
    /// </summary>
    /// <remarks>
    /// Builds the pruned-read command (WHERE = the translated conjuncts, plus ORDER BY id replicating
    /// the previous scan order); the caller must create the command inside the gate.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="translation">翻译结果 / The translation</param>
    /// <returns>查询命令 / The query command</returns>
    private SqliteCommand BuildFilteredSelectCommand<TState>(JsonExtractWhereTranslation translation) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildFilteredSelectCommandText<TState>(translation.WhereSql);
        BindJsonExtractParameters(command, translation);
        return command;
    }

    /// <summary>
    /// 构建裁剪读 SQL 文本（表名 = 编译期封闭类型名双引号转义；WHERE 主体由翻译器以标识符字符 + $parameterN 占位符构成，值全绑定）。
    /// </summary>
    /// <remarks>
    /// Builds the pruned-read SQL text. The table name is the quote-escaped compile-time closed type
    /// name; the WHERE body is composed by the translator from identifier characters and $parameterN
    /// placeholders only — every value is bound (Sonar S2077).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="whereSql">WHERE 主体（非空）/ The WHERE body (non-empty)</param>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildFilteredSelectCommandText<TState>(string whereSql) where TState : BaseCacheState, new()
    {
        return "SELECT id, doc FROM " + QuoteTableName<TState>() + " WHERE " + whereSql + " ORDER BY id;";
    }

    /// <summary>
    /// 构建 COUNT 命令文本（WHERE 可为空——全表计数）。
    /// </summary>
    /// <remarks>
    /// Builds the COUNT command text (the WHERE may be empty — a whole-table count).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="whereSql">WHERE 主体（可为空串）/ The WHERE body (may be empty)</param>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildCountCommandText<TState>(string whereSql) where TState : BaseCacheState, new()
    {
        return "SELECT COUNT(*) FROM " + QuoteTableName<TState>() + BuildWhereClause(whereSql) + ";";
    }

    /// <summary>
    /// 构建 EXISTS 命令文本。
    /// </summary>
    /// <remarks>
    /// Builds the EXISTS command text.
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="whereSql">WHERE 主体（可为空串）/ The WHERE body (may be empty)</param>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildExistsCommandText<TState>(string whereSql) where TState : BaseCacheState, new()
    {
        return "SELECT EXISTS(SELECT 1 FROM " + QuoteTableName<TState>() + BuildWhereClause(whereSql) + ");";
    }

    /// <summary>
    /// 构建分页读命令（WHERE + ORDER BY + LIMIT/OFFSET，OFFSET 走 long 防溢出）。
    /// </summary>
    /// <remarks>
    /// Builds the paged-read command (WHERE + ORDER BY + LIMIT/OFFSET; the offset binds as long against overflow).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="translation">翻译结果 / The translation</param>
    /// <param name="orderBySql">ORDER BY 子句 / The ORDER BY clause</param>
    /// <param name="pageIndex">页索引（从 0 开始）/ The page index (0-based)</param>
    /// <param name="pageSize">页大小 / The page size</param>
    /// <returns>查询命令 / The query command</returns>
    private SqliteCommand BuildPagedSelectCommand<TState>(JsonExtractWhereTranslation translation, string orderBySql, int pageIndex, int pageSize) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildPagedSelectCommandText<TState>(translation.WhereSql, orderBySql);
        BindJsonExtractParameters(command, translation);
        command.Parameters.AddWithValue("$pageSize", (long)pageSize);
        command.Parameters.AddWithValue("$pageOffset", (long)pageIndex * pageSize);
        return command;
    }

    /// <summary>
    /// 构建分页读 SQL 文本（表名不变量同裁剪读；ORDER BY 子句由排序翻译器构成，LIMIT/OFFSET 值绑定）。
    /// </summary>
    /// <remarks>
    /// Builds the paged-read SQL text (same table-name invariant as the pruned read; the ORDER BY
    /// clause comes from the sort translator; LIMIT/OFFSET values are bound).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="whereSql">WHERE 主体（可为空串）/ The WHERE body (may be empty)</param>
    /// <param name="orderBySql">ORDER BY 子句 / The ORDER BY clause</param>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildPagedSelectCommandText<TState>(string whereSql, string orderBySql) where TState : BaseCacheState, new()
    {
        return "SELECT id, doc FROM " + QuoteTableName<TState>() + BuildWhereClause(whereSql) + " ORDER BY " + orderBySql + " LIMIT $pageSize OFFSET $pageOffset;";
    }

    /// <summary>
    /// 构建首匹配读命令（WHERE + ORDER BY + LIMIT 1）。
    /// </summary>
    /// <remarks>
    /// Builds the first-match read command (WHERE + ORDER BY + LIMIT 1).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="translation">翻译结果 / The translation</param>
    /// <param name="orderBySql">ORDER BY 子句 / The ORDER BY clause</param>
    /// <returns>查询命令 / The query command</returns>
    private SqliteCommand BuildFirstMatchSelectCommand<TState>(JsonExtractWhereTranslation translation, string orderBySql) where TState : BaseCacheState, new()
    {
        var command = _connection.CreateCommand();
        command.CommandText = BuildFirstMatchSelectCommandText<TState>(translation.WhereSql, orderBySql);
        BindJsonExtractParameters(command, translation);
        return command;
    }

    /// <summary>
    /// 构建首匹配读 SQL 文本（不变量同分页读，无 OFFSET）。
    /// </summary>
    /// <remarks>
    /// Builds the first-match read SQL text (same invariants as the paged read, no OFFSET).
    /// </remarks>
    /// <typeparam name="TState">缓存状态类型 / The state type</typeparam>
    /// <param name="whereSql">WHERE 主体（可为空串）/ The WHERE body (may be empty)</param>
    /// <param name="orderBySql">ORDER BY 子句 / The ORDER BY clause</param>
    /// <returns>SQL 文本 / The SQL text</returns>
    private static string BuildFirstMatchSelectCommandText<TState>(string whereSql, string orderBySql) where TState : BaseCacheState, new()
    {
        return "SELECT id, doc FROM " + QuoteTableName<TState>() + BuildWhereClause(whereSql) + " ORDER BY " + orderBySql + " LIMIT 1;";
    }

    /// <summary>
    /// 组装 WHERE 子句（空主体返回空串，避免悬空 WHERE）。
    /// </summary>
    /// <remarks>
    /// Composes the WHERE clause (an empty body yields an empty string, avoiding a dangling WHERE).
    /// </remarks>
    /// <param name="whereSql">WHERE 主体 / The WHERE body</param>
    /// <returns>带前缀的子句或空串 / The prefixed clause, or an empty string</returns>
    private static string BuildWhereClause(string whereSql)
    {
        return whereSql.Length == 0 ? string.Empty : " WHERE " + whereSql;
    }

    /// <summary>
    /// 绑定翻译参数（$parameter0.. 顺序与占位符一致）。
    /// </summary>
    /// <remarks>
    /// Binds the translation parameters ($parameter0.. in placeholder order).
    /// </remarks>
    /// <param name="command">命令 / The command</param>
    /// <param name="translation">翻译结果 / The translation</param>
    private static void BindJsonExtractParameters(SqliteCommand command, JsonExtractWhereTranslation translation)
    {
        for (var index = 0; index < translation.ParameterValues.Count; index++)
        {
            command.Parameters.AddWithValue("$parameter" + index.ToString(CultureInfo.InvariantCulture), translation.ParameterValues[index]);
        }
    }
}
