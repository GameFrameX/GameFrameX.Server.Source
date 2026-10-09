// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Utility.Setting;

namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// 数据库选项（连接串、提供者与门面默认库提名的唯一载体，C183）。
/// </summary>
/// <remarks>
/// Database configuration options: the single source of truth for the connection string,
/// the provider enum and the facade default-database nomination (C183).
/// </remarks>
public sealed record DbOptions
{
    /// <summary>
    /// 数据库实现提供者（非泛型 <c>GameDb.Init(DbOptions)</c> 的约定解析依据；泛型 <c>Init&lt;T&gt;</c> 忽略此字段——泛型实参即提供者）。
    /// </summary>
    /// <remarks>
    /// Database implementation provider, resolved by convention in the non-generic
    /// <c>GameDb.Init(DbOptions)</c>; ignored by the generic <c>Init&lt;T&gt;</c> overload where the
    /// type argument is itself the provider.
    /// </remarks>
    /// <value>提供者枚举，缺省 <see cref="DatabaseProviderType.Mongo"/> / Provider enum, defaults to <see cref="DatabaseProviderType.Mongo"/></value>
    public DatabaseProviderType Provider { get; init; } = DatabaseProviderType.Mongo;

    /// <summary>
    /// 连接字符串（唯一来源；未配置在调用方即产生编译错误）。
    /// </summary>
    /// <remarks>
    /// Connection string, the single source of truth; the <c>required</c> modifier turns a missing
    /// value into a compile-time error at the call site instead of a runtime failure during Init.
    /// </remarks>
    /// <value>连接字符串 / Connection string</value>
    public required string ConnectionString { get; init; }

    /// <summary>
    /// 数据库名称（同时作为 <see cref="MultiDbRegistry"/> 的注册名）。
    /// </summary>
    /// <remarks>
    /// Database name, also used as the registry name in <see cref="MultiDbRegistry"/>.
    /// </remarks>
    /// <value>数据库名称，缺省 <c>"default"</c> / Database name, defaults to <c>"default"</c></value>
    public string Name { get; init; } = "default";

    /// <summary>
    /// 是否将该库提名为静态门面的默认库（注册成功后生效，set-once：进程内至多一个库为 <c>true</c>，后注册异名抛 <see cref="InvalidOperationException"/>）。
    /// </summary>
    /// <remarks>
    /// Nominates this database as the facade default right after its successful registration
    /// (set-once: at most one database per process may register with <c>true</c>; a later conflicting
    /// nomination throws <see cref="InvalidOperationException"/> naming both databases). Non-default
    /// databases (e.g. the control database registered first by the launch flow) MUST explicitly set
    /// <c>false</c> — omitting it fails fast at startup instead of silently routing business
    /// reads/writes to the wrong database. When no database is nominated the facade falls back to the
    /// first registered database with a one-shot warning.
    /// </remarks>
    /// <value>是否提名门面默认库，缺省 <c>true</c> / Whether to nominate as facade default, defaults to <c>true</c></value>
    public bool IsDefault { get; init; } = true;

    /// <summary>
    /// 是否使用时区时间记录。
    /// </summary>
    /// <remarks>
    /// Whether to use time zone for time recording.
    /// </remarks>
    /// <value>是否使用时区时间记录 / Whether to use time zone time recording</value>
    public bool IsUseTimeZone { get; init; } = false;

    /// <summary>
    /// 运行时配置选项。
    /// </summary>
    /// <value>默认值由 <see cref="DatabaseRuntimeOptions"/> 提供。</value>
    public DatabaseRuntimeOptions RuntimeOptions { get; init; } = new();
}
