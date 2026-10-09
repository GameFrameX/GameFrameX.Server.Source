// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Text;

namespace GameFrameX.Discovery;

/// <summary>
/// 发现层存储命名的统一规则集：每类名字一条推导规则，全程序集零名称字面量。
/// </summary>
/// <remarks>
/// The unified naming rules of the discovery-layer storage: one derivation rule per
/// name kind, so neither provider assembly carries a single name literal. Table and
/// collection names come from the entity base class name (strip the
/// <c>Entity</c> suffix, snake_case); PostgreSql column names are the snake_case of
/// the property names; the Mongo element names are the camelCase of the property
/// names (rendered by the driver's camelCase convention — keep
/// <see cref="CamelCase"/> aligned with it); constraint and index names compose
/// from the table / element / kind parts. Renaming a class or property therefore
/// renames its storage footprint by construction.
/// </remarks>
public static class DiscoveryStorageNaming
{
    /// <summary>
    /// 实体基类名的后缀约定（剥离后蛇形即表 / 集合名）。
    /// </summary>
    /// <remarks>
    /// The entity-base name suffix convention (stripped before the snake_case fold).
    /// </remarks>
    private const string EntitySuffix = "Entity";

    /// <summary>
    /// 表 / 集合名规则：实体基类名去 <c>Entity</c> 后缀转 snake_case（ServerHeartbeatEntity → server_heartbeat）。
    /// </summary>
    /// <typeparam name="TEntity">实体基类 / The entity base</typeparam>
    /// <returns>表 / 集合名 / The table / collection name</returns>
    public static string TableName<TEntity>()
    {
        var name = typeof(TEntity).Name;
        return SnakeCase(name.EndsWith(EntitySuffix, StringComparison.Ordinal) ? name[..^EntitySuffix.Length] : name);
    }

    /// <summary>
    /// snake_case 规则（PascalCase → snake_case，InstanceId → instance_id）。
    /// </summary>
    /// <param name="name">PascalCase 名 / The PascalCase name</param>
    /// <returns>snake_case 名 / The snake_case name</returns>
    public static string SnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        foreach (var character in name)
        {
            if (char.IsUpper(character))
            {
                if (builder.Length > 0)
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(character));
            }
            else
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// camelCase 规则（PascalCase → camelCase，PlayerId → playerId；与 Mongo 驱动的 camelCase 元素名约定对齐）。
    /// </summary>
    /// <param name="name">PascalCase 名 / The PascalCase name</param>
    /// <returns>camelCase 名 / The camelCase name</returns>
    public static string CamelCase(string name)
    {
        return string.IsNullOrEmpty(name) ? name : char.ToLowerInvariant(name[0]) + name[1..];
    }

    /// <summary>
    /// PostgreSql 主键约束名规则（{table}_pkey）。
    /// </summary>
    /// <param name="tableName">表名 / The table name</param>
    /// <returns>约束名 / The constraint name</returns>
    public static string PrimaryKeyName(string tableName)
    {
        return $"{tableName}_pkey";
    }

    /// <summary>
    /// PostgreSql 表索引名规则（ix_{table}_{column}）。
    /// </summary>
    /// <param name="tableName">表名 / The table name</param>
    /// <param name="columnName">列名 / The column name</param>
    /// <returns>索引名 / The index name</returns>
    public static string TableIndexName(string tableName, string columnName)
    {
        return $"ix_{tableName}_{columnName}";
    }

    /// <summary>
    /// Mongo 唯一索引名规则（{element}_unique）。
    /// </summary>
    /// <param name="elementName">元素名（camelCase）/ The element name (camelCase)</param>
    /// <returns>索引名 / The index name</returns>
    public static string UniqueIndexName(string elementName)
    {
        return $"{elementName}_unique";
    }

    /// <summary>
    /// Mongo 普通索引名规则（{element}_idx）。
    /// </summary>
    /// <param name="elementName">元素名（camelCase）/ The element name (camelCase)</param>
    /// <returns>索引名 / The index name</returns>
    public static string PlainIndexName(string elementName)
    {
        return $"{elementName}_idx";
    }
}
