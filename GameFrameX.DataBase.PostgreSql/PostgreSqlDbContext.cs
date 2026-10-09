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


using System.Reflection;
using GameFrameX.DataBase;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// jsonb 文档表包装实体（C168）：每 <c>CacheState</c> 类型一张 <c>(id bigint PK, doc jsonb)</c> 文档表的 EF 映射载体。
/// </summary>
/// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
/// <remarks>
/// Wrapper entity for the per-state-type jsonb document table (C168): <c>Id</c> maps the <c>id</c> primary key
/// column and <c>Doc</c> maps the whole state object as an EF owned JSON document on the <c>doc</c> jsonb column
/// (table / column names identical to the C166 storage model — zero data migration).
/// </remarks>
public sealed class StateRow<TState> where TState : BaseCacheState, new()
{
    /// <summary>
    /// 文档主键（映射 <c>id bigint PRIMARY KEY</c>，与状态对象 <c>Id</c> 同值）。
    /// </summary>
    /// <remarks>
    /// The document primary key (mapped to <c>id bigint PRIMARY KEY</c>, same value as the state's <c>Id</c>).
    /// </remarks>
    public long Id { get; set; }

    /// <summary>
    /// 整文档状态对象（owned JSON 映射到 <c>doc</c> jsonb 列）。
    /// </summary>
    /// <remarks>
    /// The whole-document state object (owned JSON mapped onto the <c>doc</c> jsonb column).
    /// </remarks>
    public TState Doc { get; set; }
}

/// <summary>
/// 每 <c>TState</c> 一个闭式泛型 <c>DbContext</c>（C168 D1）：模型静态可缓存，避免运行时类型注册带来的模型失效抖动。
/// </summary>
/// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
/// <remarks>
/// One closed-generic context per <c>TState</c> (C168 D1): each closed generic gets its own compiled model, so
/// models are statically cacheable and no runtime type registration can invalidate an already-built model
/// (an unacceptable steady-state jitter source for game servers). Contexts are short-lived — one per database
/// operation (C168 D6: after a failed EF operation the context is not reusable).
/// <para>
/// 软删默认过滤 = <c>Doc.IsDeleted != true</c>（null 与 false 均可见，对齐 Mongo <c>IsDeleted == null || IsDeleted == false</c>
/// 语义；EF 按 C# null 语义补偿展开为 SQL 时 null 行被保留）。需要包含软删数据的路径（includeDeleted / 物理删除 / 恢复）
/// 必须显式 <c>IgnoreQueryFilters()</c>。
/// </para>
/// </remarks>
public sealed class PostgreSqlDbContext<TState> : DbContext where TState : BaseCacheState, new()
{
    /// <summary>
    /// 初始化文档表上下文。
    /// </summary>
    /// <remarks>
    /// Initializes the document-table context with externally pooled data-source-bound options.
    /// </remarks>
    /// <param name="options">数据源绑定的上下文选项 / The data-source-bound context options</param>
    public PostgreSqlDbContext(DbContextOptions<PostgreSqlDbContext<TState>> options) : base(options)
    {
    }

    /// <summary>
    /// 文档表行集。
    /// </summary>
    /// <remarks>
    /// The document table row set.
    /// </remarks>
    public DbSet<StateRow<TState>> Rows { get; set; }

    /// <summary>
    /// 配置文档表映射（表名 / 列名与 C166 存储模型逐字一致）与软删全局查询过滤器。
    /// </summary>
    /// <remarks>
    /// Configures the document-table mapping (table and column names byte-identical to the C166 storage model)
    /// and the soft-delete global query filter. jsonb 表达式索引 EF 模型 API 不可表达，由索引引导（白名单 DDL）另行创建。
    /// </remarks>
    /// <param name="modelBuilder">模型构建器 / The model builder</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var entity = modelBuilder.Entity<StateRow<TState>>();
        entity.ToTable(typeof(TState).Name);
        entity.HasKey(static row => row.Id);
        entity.Property(static row => row.Id).HasColumnName("id");
        entity.OwnsOne(
            static row => row.Doc,
            static owned =>
            {
                owned.ToJson("doc");
                ConfigureOwnedDocumentShape(owned, typeof(TState));
            });
        // null 语义对齐 Mongo/C166：IsDeleted 为 null（未设置）或 false 均可见；仅 true 被过滤。
        // Aligned with Mongo/C166: IsDeleted null (unset) or false stays visible; only true is filtered out.
        entity.HasQueryFilter(static row => row.Doc.IsDeleted != true);
    }

    /// <summary>
    /// 递归把文档内引用类型 / 引用类型集合配置为嵌套 owned 类型，使整文档单 jsonb 列可映射（EF 官方语义：未配置的类型映射失败即显式失败）。
    /// </summary>
    /// <remarks>
    /// Recursively configures reference-typed members (and collections thereof) as nested owned types so the
    /// whole document maps onto a single jsonb column. Scalar / primitive-collection members are left to EF's
    /// JSON-native mapping. Members whose shape EF cannot express (e.g. class-valued dictionaries) surface as an
    /// explicit model-building error instead of silent data loss.
    /// </remarks>
    /// <param name="builder">当前 owned 类型构建器 / The current owned-type builder</param>
    /// <param name="clrType">当前文档 CLR 类型 / The current document CLR type</param>
    private static void ConfigureOwnedDocumentShape(OwnedNavigationBuilder builder, Type clrType)
    {
        foreach (var property in clrType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
        {
            if (!property.CanRead || !property.CanWrite || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            if (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Dictionary<,>))
            {
                // EF owned JSON 不支持字典成员（C168 实测：值转换器在 owned JSON 读路径崩溃 / 双重编码破坏 C166 存量形态）。
                // 显式抛错指名成员，避免 EF 关系推断的隐晦报错；绝不静默丢字段。
                // EF owned JSON cannot express dictionary members (C168 verified: value converters crash the JSON
                // read shaper or double-encode, breaking C166-stored shapes). Fail explicitly naming the member;
                // never silently drop data.
                throw new NotSupportedException($"PostgreSqlDbContext: document type '{clrType.Name}' declares dictionary property '{property.Name}' ({property.PropertyType.Name}). EF owned JSON columns cannot map dictionaries in a C166-compatible shape; restructure it as a collection of owned entries or a scalar payload.");
            }

            if (Nullable.GetUnderlyingType(property.PropertyType) != null && property.PropertyType.IsValueType)
            {
                // C166 序列化语义：可空值类型写 JSON null（如 "IsDeleted": null），EF 默认会写 CLR 默认值（false/0）。
                // 显式置为可选，使 EF JSON 写入器输出 null，读取侧两种形态等价。
                // C166 serialization semantics: nullable value types must be written as JSON null; EF would otherwise
                // coerce to the CLR default (false/0). Explicitly optional so the EF JSON writer emits null.
                builder.Property(property.Name).IsRequired(false);
            }

            var propertyType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (IsScalarDocumentMember(propertyType))
            {
                continue;
            }

            if (propertyType == typeof(object))
            {
                // object 成员无 owned 映射形态，交由 EF 模型校验显式失败。
                // object members have no owned mapping shape; left to EF model validation to fail explicitly.
                continue;
            }

            if (typeof(System.Collections.IEnumerable).IsAssignableFrom(propertyType))
            {
                var elementType = ResolveElementType(propertyType);
                if (elementType == null || IsScalarDocumentMember(elementType))
                {
                    // 基元集合由 EF JSON 原生映射（JSON 数组）。
                    // Primitive collections map natively by EF as JSON arrays.
                    continue;
                }

                var collectionNavigation = builder.OwnsMany(elementType, property.Name);
                ConfigureOwnedDocumentShape(collectionNavigation, elementType);
                continue;
            }

            var ownedNavigation = builder.OwnsOne(propertyType, property.Name);
            ConfigureOwnedDocumentShape(ownedNavigation, propertyType);
        }
    }

    /// <summary>
    /// 解析集合元素类型（数组 / 实现 <c>IEnumerable&lt;T&gt;</c> 的集合）。
    /// </summary>
    /// <remarks>
    /// Resolves the element type of a collection (arrays or <c>IEnumerable&lt;T&gt;</c> implementations).
    /// </remarks>
    /// <param name="collectionType">集合类型 / The collection type</param>
    /// <returns>元素类型；无法解析时为 null / The element type, or null when unresolvable</returns>
    private static Type ResolveElementType(Type collectionType)
    {
        if (collectionType.IsArray)
        {
            return collectionType.GetElementType();
        }

        foreach (var interfaceType in collectionType.GetInterfaces())
        {
            if (interfaceType.IsGenericType && interfaceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                return interfaceType.GetGenericArguments()[0];
            }
        }

        return null;
    }

    /// <summary>
    /// 判断文档成员是否为标量（EF JSON 原生映射，无需 owned 配置）。
    /// </summary>
    /// <remarks>
    /// Determines whether a document member is scalar (mapped natively by EF JSON, no owned configuration needed).
    /// 基元/枚举/string/decimal/时间/Guid/byte[]/Uri/基元值字典由 EF 直接映射为 JSON 标量或 JSON 数组。
    /// </remarks>
    /// <param name="type">成员类型 / The member type</param>
    /// <returns>是否标量 / Whether scalar</returns>
    private static bool IsScalarDocumentMember(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsPrimitive || underlying.IsEnum)
        {
            return true;
        }

        if (underlying == typeof(string) || underlying == typeof(decimal) || underlying == typeof(DateTime) ||
            underlying == typeof(DateTimeOffset) || underlying == typeof(TimeSpan) || underlying == typeof(Guid) ||
            underlying == typeof(byte[]) || underlying == typeof(Uri))
        {
            return true;
        }



        return false;
    }
}
