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


using System.Diagnostics.CodeAnalysis;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Localization;
using GameFrameX.Utility.Setting;

namespace GameFrameX.DataBase;

/// <summary>
/// 游戏数据库静态工具类,提供对数据库的基本操作封装。
/// </summary>
/// <remarks>
/// Static utility class for game database operations, providing basic database operation encapsulation.
/// The static facade targets the database explicitly nominated by <see cref="SetDefault"/> when present
/// (C159), and falls back to the first registered database otherwise (D20#2 semantics preserved for
/// single-database processes and legacy consumers); additional databases are registered in
/// <see cref="MultiDbRegistry"/> and resolved by name.
/// </remarks>
public static partial class GameDb
{
    /// <summary>
    /// 数据库服务实现实例（首个注册库的门面别名，C143a D20#2 修复：二次 Init 不再静默覆盖）。
    /// </summary>
    /// <remarks>
    /// Database service implementation instance (facade alias of the first registered database;
    /// a second Init no longer silently overwrites it).
    /// </remarks>
    private static IDatabaseService _dbServiceImplementation;

    /// <summary>
    /// 门面默认库的注册名（显式指定标记；null 表示未显式指定，门面回落首个注册库）。
    /// </summary>
    /// <remarks>
    /// The registry name explicitly nominated as the facade default database; null means no explicit
    /// nomination and the facade falls back to the first registered database (C159).
    /// </remarks>
    private static string _defaultDatabaseName;

    /// <summary>
    /// 门面隐式绑定告警的一次性标志（0 = 未告警，1 = 已告警）。
    /// </summary>
    /// <remarks>
    /// One-shot flag for the implicit facade binding warning (0 = not warned, 1 = warned).
    /// </remarks>
    private static int _implicitBindingWarningLogged;

    /// <summary>
    /// 门面目标库（全部静态 CRUD 与无参 <see cref="As{T}()"/> 的实际落点）。
    /// </summary>
    /// <remarks>
    /// The facade target database actually serving every static CRUD member and the parameterless
    /// <see cref="As{T}()"/>. Centralizes the default-binding semantics: explicit <see cref="SetDefault"/>
    /// nomination first, first-registered fallback otherwise; emits the one-shot implicit-binding
    /// warning when multiple databases are registered without an explicit nomination.
    /// </remarks>
    private static IDatabaseService FacadeService
    {
        get
        {
            ArgumentNullException.ThrowIfNull(_dbServiceImplementation, nameof(_dbServiceImplementation));
            if (Volatile.Read(ref _defaultDatabaseName) == null && MultiDbRegistry.RegisteredCount > 1)
            {
                WarnImplicitBindingOnce();
            }

            return _dbServiceImplementation;
        }
    }

    /// <summary>
    /// 多库注册但从未 <see cref="SetDefault"/> 时，首次门面调用打一条一次性 Warning（不 fail fast，
    /// 避免破坏单库与既有测试场景；P1-5 收敛）。
    /// </summary>
    /// <remarks>
    /// Logs a one-shot Warning on the first facade call when more than one database is registered and
    /// <see cref="SetDefault"/> was never called (P1-5: warn once instead of failing fast, so
    /// single-database processes and existing tests keep working unchanged).
    /// </remarks>
    private static void WarnImplicitBindingOnce()
    {
        if (Interlocked.CompareExchange(ref _implicitBindingWarningLogged, 1, 0) != 0)
        {
            return;
        }

        LogHelper.Warning<string>("GameDb.ImplicitDefaultBinding {message}", LocalizationService.GetString(Localization.Keys.Database.GameDbImplicitDefaultBindingWarning, string.Join(", ", MultiDbRegistry.GetRegisteredDatabaseNames())));
    }

    /// <summary>
    /// 初始化GameDb（显式实现类型逃生门：AOT/裁剪、自定义 <see cref="IDatabaseService"/> 实现与测试假实现）。
    /// </summary>
    /// <remarks>
    /// Initialize the GameDb instance (explicit-type escape hatch for AOT/trimming scenarios, custom
    /// <see cref="IDatabaseService"/> implementations and test fakes): registers by
    /// <see cref="DbOptions.Name"/> into <see cref="MultiDbRegistry"/> and, on success, nominates the
    /// facade default database when <see cref="DbOptions.IsDefault"/> is set (default). The generic
    /// argument is itself the provider — <see cref="DbOptions.Provider"/> is ignored on this path.
    /// Non-default databases (e.g. the control database registered first by the launch flow) must
    /// pass <c>IsDefault = false</c> explicitly, otherwise a later default registration fails fast
    /// with a set-once conflict. Startup composition roots resolving the provider from configuration
    /// should use the non-generic <see cref="Init(DbOptions)"/> overload instead.
    /// </remarks>
    /// <typeparam name="T">数据库服务的具体实现类型,必须实现IDatabaseService接口且有无参构造函数 / Database service implementation type, must implement IDatabaseService interface and have a parameterless constructor</typeparam>
    /// <param name="dbOptions">数据库配置选项（ConnectionString 唯一来源；Name 同时作为注册名；IsDefault 提名门面默认库） / Database configuration options (ConnectionString is the single source; Name doubles as the registry name; IsDefault nominates the facade default)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbOptions"/>、其 ConnectionString 或 Name 为 null 时抛出 / Thrown when dbOptions, its ConnectionString, or Name is null</exception>
    /// <exception cref="ArgumentException">当 ConnectionString 为空白时抛出 / Thrown when ConnectionString is blank</exception>
    /// <exception cref="InvalidOperationException">当同名库已注册，或 IsDefault 提名与既有默认库冲突时抛出（拒绝静默覆盖） / Thrown when a database with the same name is already registered, or the IsDefault nomination conflicts with the existing default (silent overwrite rejected)</exception>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    public static Task<bool> Init<T>(DbOptions dbOptions) where T : IDatabaseService, new()
    {
        return InitCore(new T(), dbOptions);
    }

    /// <summary>
    /// 初始化GameDb（推荐启动路径：按 <see cref="DbOptions.Provider"/> 经 <see cref="DbProviderResolver"/> 约定解析实现，C177/C183）。
    /// </summary>
    /// <remarks>
    /// Recommended launch path: resolves the implementation from <see cref="DbOptions.Provider"/> through
    /// the convention mapping table (the composition root stays free of provider-type generics and
    /// ternaries), then runs the exact same validation→Open→Register→rollback→nomination→facade path as
    /// the generic overload through the shared <c>InitCore</c>. The generic
    /// <see cref="Init{T}(DbOptions)"/> remains the explicit escape hatch for AOT/trimming scenarios,
    /// custom <see cref="IDatabaseService"/> implementations and test fakes.
    /// </remarks>
    /// <param name="dbOptions">数据库配置选项（Provider 约定解析依据；ConnectionString 唯一来源；Name 同时作为注册名；IsDefault 提名门面默认库） / Database configuration options (Provider drives convention resolution; ConnectionString is the single source; Name doubles as the registry name; IsDefault nominates the facade default)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbOptions"/>、其 ConnectionString 或 Name 为 null 时抛出 / Thrown when dbOptions, its ConnectionString, or Name is null</exception>
    /// <exception cref="ArgumentException">当 ConnectionString 为空白时抛出 / Thrown when ConnectionString is blank</exception>
    /// <exception cref="InvalidOperationException">当枚举未配映射、宿主缺 Provider 工程引用、实例化失败、同名库已注册，或 IsDefault 提名与既有默认库冲突时抛出 / Thrown when the enum is unmapped, the provider assembly is not referenced by the host, instantiation fails, a database with the same name is already registered, or the IsDefault nomination conflicts with the existing default</exception>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    [RequiresUnreferencedCode("按命名约定反射创建提供者；AOT/裁剪场景请改用 Init<T> 显式指定实现类型")]
    public static Task<bool> Init(DbOptions dbOptions)
    {
        ArgumentNullException.ThrowIfNull(dbOptions, nameof(dbOptions));
        return InitCore(DbProviderResolver.Create(dbOptions.Provider), dbOptions);
    }

    /// <summary>
    /// 两个 Init 重载的共享执行路径：参数校验→打开→注册（失败回滚关闭）→门面首绑→IsDefault 提名（C183：连接串/提名单一来源化）。
    /// </summary>
    /// <remarks>
    /// Shared execution path of both Init overloads: argument validation, Open, registration with
    /// close-on-failure rollback, first-registered facade binding, and the <see cref="DbOptions.IsDefault"/>
    /// nomination (C183: the connection string and the default nomination each have a single source —
    /// DbOptions). A nomination conflict propagates after the database has already been registered:
    /// startup is expected to abort on it (fail-fast) and the process tears down, mirroring the legacy
    /// behavior of SetDefault throwing after a successful Init.
    /// </remarks>
    /// <param name="service">已创建的数据库实现实例（泛型重载由 new() 创建，非泛型重载由 <see cref="DbProviderResolver"/> 反射创建） / The already created database service instance (new() in the generic overload, reflection in the non-generic overload)</param>
    /// <param name="dbOptions">数据库配置选项（Name 同时作为注册名） / Database configuration options (Name doubles as the registry name)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="dbOptions"/>、其 ConnectionString 或 Name 为 null 时抛出 / Thrown when dbOptions, its ConnectionString, or Name is null</exception>
    /// <exception cref="ArgumentException">当 ConnectionString 为空白时抛出 / Thrown when ConnectionString is blank</exception>
    /// <exception cref="InvalidOperationException">当同名库已注册，或 IsDefault 提名与既有默认库冲突时抛出（拒绝静默覆盖） / Thrown when a database with the same name is already registered, or the IsDefault nomination conflicts with the existing default (silent overwrite rejected)</exception>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    private static async Task<bool> InitCore(IDatabaseService service, DbOptions dbOptions)
    {
        ArgumentNullException.ThrowIfNull(dbOptions, nameof(dbOptions));
        ArgumentException.ThrowIfNullOrWhiteSpace(dbOptions.ConnectionString, nameof(DbOptions.ConnectionString));
        ArgumentNullException.ThrowIfNull(dbOptions.Name, nameof(dbOptions.Name));

        var isOpened = await service.Open(dbOptions);
        if (!isOpened)
        {
            return false;
        }

        try
        {
            MultiDbRegistry.Register(dbOptions.Name, service);
        }
        catch (InvalidOperationException)
        {
            // 注册失败（如同名库已注册）：先释放已打开的服务，再抛出原异常，避免泄漏已建立的连接
            // Registration failed (e.g. duplicate name): release the opened service before rethrowing to avoid leaking the established connection.
            await service.Close();
            throw;
        }

        if (_dbServiceImplementation == null)
        {
            _dbServiceImplementation = service;
        }

        if (dbOptions.IsDefault)
        {
            NominateDefaultDatabase(dbOptions.Name, service);
        }

        return true;
    }

    /// <summary>
    /// 显式指定静态门面的默认库（全部静态 CRUD 与无参 <see cref="As{T}()"/> 的目标库）。
    /// </summary>
    /// <remarks>
    /// Explicitly nominates the default database of the static facade (the target of every static CRUD
    /// member and the parameterless <see cref="As{T}()"/>). Declarative nomination via
    /// <see cref="DbOptions.IsDefault"/> is the primary launch path (the nomination then happens inside
    /// Init right after a successful registration); this method remains the explicit /
    /// post-registration entry point and shares the exact same set-once core. Set-once: a second call
    /// with a different name throws; the same name is idempotently accepted.
    /// </remarks>
    /// <param name="databaseName">注册名（须已通过 <c>GameDb.Init</c> 注册） / Registry name (must already be registered via <c>GameDb.Init</c>)</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="databaseName"/> 为 null 时抛出 / Thrown when databaseName is null</exception>
    /// <exception cref="InvalidOperationException">当注册名未注册，或默认库已显式指定为另一注册名时抛出 / Thrown when the name is not registered, or the default was already set to a different name</exception>
    public static void SetDefault(string databaseName)
    {
        ArgumentNullException.ThrowIfNull(databaseName, nameof(databaseName));
        if (!MultiDbRegistry.TryGet(databaseName, out var service))
        {
            // Localization: Database.Registry.NotRegistered - 没有名为“{0}”的数据库被注册。已注册名称：[{1}]
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.RegistryNotRegistered, databaseName, string.Join(", ", MultiDbRegistry.GetRegisteredDatabaseNames())));
        }

        NominateDefaultDatabase(databaseName, service);
    }

    /// <summary>
    /// 门面默认库提名的 set-once 核心（<see cref="SetDefault"/> 与 <see cref="DbOptions.IsDefault"/> 声明式提名共用，C183 抽取）。
    /// </summary>
    /// <remarks>
    /// Set-once core of the facade default nomination, shared by the public <see cref="SetDefault"/>
    /// and the declarative <see cref="DbOptions.IsDefault"/> path inside InitCore (C183): the first
    /// nomination wins; the same name is idempotently accepted; a different name throws naming both
    /// databases. The concurrent first-set race resolves via CAS — the loser re-reads the winner and
    /// applies the same same-name/different-name rule as the serial path.
    /// </remarks>
    /// <param name="databaseName">要提名的注册名 / Registry name to nominate</param>
    /// <param name="service">该注册名对应的服务实例（提名即绑定为门面目标） / The service registered under the name (the nomination binds it as the facade target)</param>
    /// <exception cref="InvalidOperationException">当默认库已显式指定为另一注册名时抛出 / Thrown when the default was already set to a different name</exception>
    private static void NominateDefaultDatabase(string databaseName, IDatabaseService service)
    {
        var current = Volatile.Read(ref _defaultDatabaseName);
        if (current != null)
        {
            if (current == databaseName)
            {
                return;
            }

            // Localization: Database.Registry.DefaultAlreadySet - 默认数据库已被设置为“{0}”（set-once）；不允许将其更改为“{1}”。已注册名称：[{2}]
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.RegistryDefaultAlreadySet, current, databaseName, string.Join(", ", MultiDbRegistry.GetRegisteredDatabaseNames())));
        }

        if (Interlocked.CompareExchange(ref _defaultDatabaseName, databaseName, null) != null)
        {
            // 并发首次指定：胜者生效；同名视为幂等，异名拒绝（与串行路径语义一致）
            var winner = Volatile.Read(ref _defaultDatabaseName);
            if (winner != databaseName)
            {
                // Localization: Database.Registry.DefaultAlreadySet - 默认数据库已被设置为“{0}”（set-once）；不允许将其更改为“{1}”。已注册名称：[{2}]
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Database.RegistryDefaultAlreadySet, winner, databaseName, string.Join(", ", MultiDbRegistry.GetRegisteredDatabaseNames())));
            }

            return;
        }

        _dbServiceImplementation = service;
    }

    /// <summary>
    /// 判断指定注册名的库是否已注册（转发 <see cref="MultiDbRegistry.Contains"/>，供启动链幂等守卫使用）。
    /// </summary>
    /// <remarks>
    /// Determines whether a database with the specified registry name is registered (forwards to
    /// <see cref="MultiDbRegistry.Contains"/> for idempotent startup guards, so callers no longer
    /// need to reference the registry type directly).
    /// </remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <returns>已注册返回 true；否则 false / true when registered; otherwise false</returns>
    public static bool Contains(string databaseName)
    {
        return MultiDbRegistry.Contains(databaseName);
    }

    /// <summary>
    /// 控制库的固定注册名（转发 <see cref="MultiDbRegistry.ControlDatabaseName"/>）。
    /// </summary>
    /// <remarks>
    /// The fixed registry name of the control database (forwards to
    /// <see cref="MultiDbRegistry.ControlDatabaseName"/> so call sites stay on the unified entry, C159).
    /// </remarks>
    public const string ControlDatabaseName = MultiDbRegistry.ControlDatabaseName;

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
        return (T)FacadeService;
    }

    /// <summary>
    /// 按注册名以指定类型获取数据库服务实例（多库获取）。
    /// </summary>
    /// <remarks>
    /// Get the database service instance as the specified type by registry name.
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
    /// Asynchronously closes every registered database connection.
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
        _defaultDatabaseName = null;
        Volatile.Write(ref _implicitBindingWarningLogged, 0);
        MultiDbRegistry.Clear();
    }

}
