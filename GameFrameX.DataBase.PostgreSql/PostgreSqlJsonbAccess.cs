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
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// jsonb 文档访问与 CLR 类型映射助手（查询谓词、排序与表达式索引共用的唯一事实源）。
/// </summary>
/// <remarks>
/// jsonb document access and CLR type mapping helper — the single source of truth shared by query
/// predicates, sorting, and expression indexes (C166). The accessor text produced here must stay
/// byte-for-byte identical everywhere it is used, otherwise PostgreSQL cannot match expression indexes.
/// </remarks>
internal static class PostgreSqlJsonbAccess
{
    /// <summary>
    /// 软删除默认过滤谓词（对齐 Mongo <c>IsDeleted == null || IsDeleted == false</c>）。
    /// </summary>
    /// <remarks>
    /// The default soft-delete filter predicate (mirroring Mongo's <c>IsDeleted == null || IsDeleted == false</c>).
    /// <para>jsonb 语义：<c>doc->>'K'</c> 对 JSON null 与缺失键均返回 SQL NULL，两种存储形态等价。</para>
    /// </remarks>
    public const string SoftDeleteFilter = "(doc->>'IsDeleted') IS NULL OR (doc->>'IsDeleted')::boolean = false";

    /// <summary>
    /// 获取 jsonb 文本访问器（不附加类型 cast）。
    /// </summary>
    /// <param name="propertyName">属性名 / Property name</param>
    /// <returns>访问器 SQL / Accessor SQL</returns>
    public static string GetAccessor(string propertyName)
    {
        return "doc->>'" + propertyName.Replace("'", "''", StringComparison.Ordinal) + "'";
    }

    /// <summary>
    /// 获取带 CLR 类型对齐 cast 的 jsonb 访问器。
    /// </summary>
    /// <remarks>
    /// Gets the jsonb accessor with the CLR-type-aligned cast. The cast list is bounded by design (C166 P1-8):
    /// bool → boolean、整数 → bigint、浮点 → double precision、decimal → numeric、DateTime → timestamptz、Guid → uuid、其余按文本比较。
    /// </remarks>
    /// <param name="propertyName">属性名 / Property name</param>
    /// <param name="clrType">属性 CLR 类型（可空类型自动解包）/ Property CLR type (nullable types unwrapped)</param>
    /// <returns>带 cast 的访问器 SQL / Casted accessor SQL</returns>
    public static string GetTypedAccessor(string propertyName, Type clrType)
    {
        var accessor = GetAccessor(propertyName);
        var cast = GetCastSuffix(clrType);
        return string.IsNullOrEmpty(cast) ? accessor : $"({accessor}){cast}";
    }

    /// <summary>
    /// 获取 CLR 类型对应的 SQL cast 后缀（空串表示按文本比较）。
    /// </summary>
    /// <param name="clrType">CLR 类型 / CLR type</param>
    /// <returns>cast 后缀 / Cast suffix</returns>
    public static string GetCastSuffix(Type clrType)
    {
        var underlying = Nullable.GetUnderlyingType(clrType) ?? clrType;
        if (underlying == typeof(bool))
        {
            return "::boolean";
        }

        if (underlying == typeof(byte) || underlying == typeof(sbyte) || underlying == typeof(short) || underlying == typeof(ushort) ||
            underlying == typeof(int) || underlying == typeof(uint) || underlying == typeof(long) || underlying == typeof(ulong))
        {
            return "::bigint";
        }

        if (underlying == typeof(float) || underlying == typeof(double))
        {
            return "::double precision";
        }

        if (underlying == typeof(decimal))
        {
            return "::numeric";
        }

        if (underlying == typeof(DateTime) || underlying == typeof(DateTimeOffset))
        {
            return "::timestamptz";
        }

        if (underlying == typeof(Guid))
        {
            return "::uuid";
        }

        return string.Empty;
    }
}
