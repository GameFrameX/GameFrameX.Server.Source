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


using GameFrameX.DataBase.Abstractions;

namespace GameFrameX.DataBase;

/// <summary>
/// 游戏数据库静态工具类,提供对数据库的基本操作封装。
/// </summary>
/// <remarks>
/// Static utility class for game database operations, providing basic database operation encapsulation.
/// The static facade operates on the first registered database (D20#2 fix: no longer overwritten by later Init calls);
/// additional databases are registered in <see cref="MultiDbRegistry"/> and resolved by name.
/// </remarks>
public static partial class GameDb
{
    /// <summary>
    /// 数据库服务实现实例（首个注册库的门面别名，C143a D20#2 修复：二次 Init 不再静默覆盖）。
    /// </summary>
    /// <remarks>
    /// Database service implementation instance (facade alias of the first registered database;
    /// C143a D20#2 fix: a second Init no longer silently overwrites it).
    /// </remarks>
    private static IDatabaseService _dbServiceImplementation;

    /// <summary>
    /// 初始化GameDb（兼容旧签名：连接串取自 <paramref name="dbOptions"/>）。
    /// </summary>
    /// <remarks>
    /// Initialize the GameDb instance (legacy signature: connection string taken from <paramref name="dbOptions"/>).
    /// </remarks>
    /// <typeparam name="T">数据库服务的具体实现类型,必须实现IDatabaseService接口且有无参构造函数 / Database service implementation type, must implement IDatabaseService interface and have a parameterless constructor</typeparam>
    /// <param name="dbOptions">数据库配置选项 / Database configuration options</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbOptions"/> 的 ConnectionString 或 Name 为 null 时抛出 / Thrown when ConnectionString or Name of <paramref name="dbOptions"/> is null</exception>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    [Obsolete("Use Init<T>(string connectionString, DbOptions dbOptions) instead")]
    public static Task<bool> Init<T>(DbOptions dbOptions) where T : IDatabaseService, new()
    {
        ArgumentNullException.ThrowIfNull(dbOptions, nameof(dbOptions));
        return Init<T>(dbOptions.ConnectionString, dbOptions);
    }

    /// <summary>
    /// 初始化GameDb（多库签名：按 <see cref="DbOptions.Name"/> 注册进 <see cref="MultiDbRegistry"/>，C143a D20#2）。
    /// </summary>
    /// <remarks>
    /// Initialize the GameDb instance (multi-database signature: registers by <see cref="DbOptions.Name"/> into
    /// <see cref="MultiDbRegistry"/>, C143a D20#2). The connection string passed explicitly takes precedence;
    /// when empty it falls back to <see cref="DbOptions.ConnectionString"/> (control-database D-Single fallback:
    /// pass the business <c>DataBaseUrl</c> explicitly so the control database shares the Mongo instance).
    /// The static facade binds to the first registered database and is never overwritten by later calls.
    /// </remarks>
    /// <typeparam name="T">数据库服务的具体实现类型,必须实现IDatabaseService接口且有无参构造函数 / Database service implementation type, must implement IDatabaseService interface and have a parameterless constructor</typeparam>
    /// <param name="connectionString">连接字符串；为空时回落 <paramref name="dbOptions"/> 内连接串 / Connection string; falls back to the one in <paramref name="dbOptions"/> when empty</param>
    /// <param name="dbOptions">数据库配置选项（Name 同时作为注册名） / Database configuration options (Name doubles as the registry name)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbOptions"/>、<paramref name="connectionString"/> 或 <paramref name="dbOptions"/> 的 Name 为 null 时抛出 / Thrown when dbOptions, connectionString, or Name of dbOptions is null</exception>
    /// <exception cref="InvalidOperationException">当同名库已注册时抛出（拒绝静默覆盖） / Thrown when a database with the same name is already registered (silent overwrite rejected)</exception>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    public static async Task<bool> Init<T>(string connectionString, DbOptions dbOptions) where T : IDatabaseService, new()
    {
        ArgumentNullException.ThrowIfNull(dbOptions, nameof(dbOptions));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = dbOptions.ConnectionString;
        }

        ArgumentNullException.ThrowIfNull(connectionString, nameof(connectionString));
        ArgumentNullException.ThrowIfNull(dbOptions.Name, nameof(dbOptions.Name));

        var effectiveOptions = connectionString == dbOptions.ConnectionString ? dbOptions : dbOptions with { ConnectionString = connectionString };
        var service = new T();
        var isOpened = await service.Open(effectiveOptions);
        if (!isOpened)
        {
            return false;
        }

        MultiDbRegistry.Register(dbOptions.Name, service);
        if (_dbServiceImplementation == null)
        {
            _dbServiceImplementation = service;
        }

        return true;
    }

    /// <summary>
    /// 以指定类型获取数据库服务实例。
    /// </summary>
    /// <remarks>
    /// Get the database service instance as the specified type.
    /// </remarks>
    /// <typeparam name="T">要转换的数据库服务类型,必须实现IDatabaseService接口 / Database service type to convert to, must implement IDatabaseService interface</typeparam>
    /// <returns>转换后的数据库服务实例 / The converted database service instance</returns>
    /// <exception cref="InvalidCastException">当类型转换失败时抛出 / Thrown when type conversion fails</exception>
    public static T As<T>() where T : IDatabaseService
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation, nameof(_dbServiceImplementation));
        return (T)_dbServiceImplementation;
    }

    /// <summary>
    /// 按注册名以指定类型获取数据库服务实例（C143a D20#2 多库获取）。
    /// </summary>
    /// <remarks>
    /// Get the database service instance as the specified type by registry name (C143a D20#2).
    /// </remarks>
    /// <typeparam name="T">要转换的数据库服务类型,必须实现IDatabaseService接口 / Database service type to convert to, must implement IDatabaseService interface</typeparam>
    /// <param name="databaseName">注册名（如 <see cref="MultiDbRegistry.ControlDatabaseName"/>） / Registry name (e.g. <see cref="MultiDbRegistry.ControlDatabaseName"/>)</param>
    /// <returns>转换后的数据库服务实例 / The converted database service instance</returns>
    /// <exception cref="InvalidCastException">当类型转换失败时抛出 / Thrown when type conversion fails</exception>
    /// <exception cref="InvalidOperationException">当注册名未注册时抛出 / Thrown when the name is not registered</exception>
    public static T As<T>(string databaseName) where T : IDatabaseService
    {
        return (T)MultiDbRegistry.Get(databaseName);
    }

    /// <summary>
    /// 关闭数据库连接。
    /// </summary>
    /// <remarks>
    /// Close the database connection.
    /// </remarks>
    public static void Close()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation, nameof(_dbServiceImplementation));
        CloseAsync().GetAwaiter().GetResult();
    }

    /// <summary>
    /// 异步关闭数据库连接（关闭全部已注册数据库，C143a D20#2）。
    /// </summary>
    /// <remarks>
    /// Asynchronously closes every registered database connection (C143a D20#2).
    /// </remarks>
    /// <returns>表示异步关闭操作的任务 / Task representing the asynchronous close operation</returns>
    public static async Task CloseAsync()
    {
        ArgumentNullException.ThrowIfNull(_dbServiceImplementation, nameof(_dbServiceImplementation));
        foreach (var databaseService in MultiDbRegistry.GetRegisteredDatabaseServices())
        {
            await databaseService.Close();
        }
    }

    /// <summary>
    /// 重置数据库门面与多库注册表（仅供单元测试隔离静态状态使用）。
    /// </summary>
    /// <remarks>
    /// Resets the static facade and the multi-database registry. For unit test isolation of static state only.
    /// </remarks>
    internal static void ResetForTesting()
    {
        _dbServiceImplementation = null;
        MultiDbRegistry.Clear();
    }

}
