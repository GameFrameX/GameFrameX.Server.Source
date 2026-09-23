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

using System.Collections.Concurrent;
using GameFrameX.DataBase.Abstractions;

namespace GameFrameX.DataBase;

/// <summary>
/// 进程级多数据库注册表（C143a D20#2：按 <see cref="DbOptions.Name"/> 注册与获取，替代单静态字段）。
/// </summary>
/// <remarks>
/// Process-level multi-database registry (C143a D20#2): registers and resolves database services by
/// <see cref="DbOptions.Name"/> instead of a single static field, so the control database and the
/// business database can coexist in one process without overwriting each other.
/// <para>注册名约定：业务库用主库名（如 <c>gameframex</c>），控制库固定为 <see cref="ControlDatabaseName"/>；</para>
/// <para>D-Single 形态下控制库回落业务库连接串（同实例不同 database）。</para>
/// </remarks>
public static class MultiDbRegistry
{
    /// <summary>
    /// 控制库的固定注册名与 Mongo 库名（C143a D16：主库内专用 database）。
    /// </summary>
    /// <remarks>
    /// Fixed registry name and Mongo database name of the control database (C143a D16).
    /// </remarks>
    public const string ControlDatabaseName = "gameframex_control";

    /// <summary>
    /// 缺省注册名（<see cref="DbOptions.Name"/> 未显式指定时使用）。
    /// </summary>
    /// <remarks>
    /// Default registry name used when <see cref="DbOptions.Name"/> is not explicitly specified.
    /// </remarks>
    public const string DefaultDatabaseName = "default";

    /// <summary>
    /// 多库字典（注册名 → 数据库服务实例）。
    /// </summary>
    /// <remarks>
    /// The multi-database dictionary (registry name to database service instance).
    /// </remarks>
    private static readonly ConcurrentDictionary<string, IDatabaseService> Databases = new(StringComparer.Ordinal);

    /// <summary>
    /// 已注册库数量（O(1) 维护，供门面隐式绑定告警做廉价判断）。
    /// </summary>
    /// <remarks>
    /// The number of registered databases (maintained O(1) so the facade implicit-binding
    /// warning can check it cheaply on every facade call, C159).
    /// </remarks>
    private static int _registeredCount;

    /// <summary>
    /// 已注册库数量（测试与同程序集内可见）。
    /// </summary>
    /// <remarks>
    /// The number of registered databases (visible to the test assembly and this assembly).
    /// </remarks>
    internal static int RegisteredCount
    {
        get { return Volatile.Read(ref _registeredCount); }
    }

    /// <summary>
    /// 注册数据库服务；同名库重复注册时抛出异常（fail fast，拒绝静默覆盖）。
    /// </summary>
    /// <remarks>
    /// Registers a database service; throws when the same name is already registered (fail fast, silent overwrite rejected).
    /// </remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <param name="databaseService">数据库服务实例 / Database service instance</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="databaseName"/> 或 <paramref name="databaseService"/> 为 null 时抛出 / Thrown when databaseName or databaseService is null</exception>
    /// <exception cref="InvalidOperationException">当同名库已注册时抛出 / Thrown when a database with the same name is already registered</exception>
    public static void Register(string databaseName, IDatabaseService databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseName, nameof(databaseName));
        ArgumentNullException.ThrowIfNull(databaseService, nameof(databaseService));
        if (!Databases.TryAdd(databaseName, databaseService))
        {
            throw new InvalidOperationException($"A database named '{databaseName}' is already registered. Registered names: [{string.Join(", ", Databases.Keys)}]");
        }

        Interlocked.Increment(ref _registeredCount);
    }
    /// <summary>
    /// 按注册名获取数据库服务。
    /// </summary>
    /// <remarks>
    /// Gets the database service by registry name.
    /// </remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <returns>数据库服务实例 / The database service instance</returns>
    /// <exception cref="ArgumentNullException">当 <paramref name="databaseName"/> 为 null 时抛出 / Thrown when databaseName is null</exception>
    /// <exception cref="InvalidOperationException">当注册名未注册时抛出 / Thrown when the name is not registered</exception>
    public static IDatabaseService Get(string databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName, nameof(databaseName));
        if (!Databases.TryGetValue(databaseName, out var databaseService))
        {
            throw new InvalidOperationException($"No database named '{databaseName}' is registered. Registered names: [{string.Join(", ", Databases.Keys)}]");
        }

        return databaseService;
    }

    /// <summary>
    /// 尝试按注册名获取数据库服务。
    /// </summary>
    /// <remarks>
    /// Tries to get the database service by registry name.
    /// </remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <param name="databaseService">数据库服务实例；未注册时为 null / Database service instance, or null when not registered</param>
    /// <returns>已注册返回 true；否则 false / true when registered; otherwise false</returns>
    public static bool TryGet(string databaseName, out IDatabaseService databaseService)
    {
        ArgumentNullException.ThrowIfNull(databaseName, nameof(databaseName));
        return Databases.TryGetValue(databaseName, out databaseService);
    }

    /// <summary>
    /// 判断指定注册名的库是否已注册（幂等初始化守卫用）。
    /// </summary>
    /// <remarks>
    /// Determines whether a database with the specified registry name is registered (idempotent initialization guard).
    /// </remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <returns>已注册返回 true；否则 false / true when registered; otherwise false</returns>
    public static bool Contains(string databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName, nameof(databaseName));
        return Databases.ContainsKey(databaseName);
    }

    /// <summary>
    /// 获取全部已注册库名的只读快照。
    /// </summary>
    /// <remarks>
    /// Gets a read-only snapshot of all registered database names.
    /// </remarks>
    /// <returns>已注册库名列表 / The list of registered database names</returns>
    public static IReadOnlyList<string> GetRegisteredDatabaseNames()
    {
        return Databases.Keys.ToArray();
    }

    /// <summary>
    /// 获取全部已注册数据库服务的只读快照。
    /// </summary>
    /// <remarks>
    /// Gets a read-only snapshot of all registered database services.
    /// </remarks>
    /// <returns>已注册数据库服务列表 / The list of registered database services</returns>
    public static IReadOnlyList<IDatabaseService> GetRegisteredDatabaseServices()
    {
        return Databases.Values.ToArray();
    }

    /// <summary>
    /// 清空注册表（仅供单元测试隔离静态状态使用）。
    /// </summary>
    /// <remarks>
    /// Clears the registry. For unit test isolation of static state only.
    /// </remarks>
    internal static void Clear()
    {
        Databases.Clear();
        Volatile.Write(ref _registeredCount, 0);
    }
}
