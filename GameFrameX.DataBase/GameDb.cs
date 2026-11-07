// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:    https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:      https://cnb.cool/GameFrameX
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
/// 游戏数据库静态便捷层（过渡保留：生命周期面转发 <see cref="GameDatabase.ProcessDefault"/>，已标记过时并指向注入面）。
/// </summary>
/// <remarks>
/// The static convenience layer for game database operations (transitional: the lifecycle surface
/// forwards to <see cref="GameDatabase.ProcessDefault"/> and is marked obsolete, pointing at the
/// injection seam). CRUD 静态成员暂无实例替代，保留不标注；后续收敛版本迁移调用点后移除本层。
/// The static CRUD members have no instance replacement yet and stay unmarked; the layer is removed
/// once later convergence versions migrate the call sites.
/// </remarks>
public static partial class GameDb
{
    /// <summary>
    /// 静态 CRUD partial 的空判守卫转发面（原始读，不触发空抛与隐式绑定告警；行为与旧私有静态字段一致）。
    /// </summary>
    /// <remarks>
    /// The null-guard forward for the static CRUD partials (raw read, no null-throw and no
    /// implicit-binding warning; behavior identical to the former private static field).
    /// </remarks>
    private static IDatabaseService _dbServiceImplementation
    {
        get { return GameDatabase.ProcessDefault.CurrentServiceImplementation; }
    }

    /// <summary>
    /// 门面目标库（静态 CRUD partial 的转发目标——静态层与实例核共享同一门面状态，不产生双门面分裂）。
    /// </summary>
    /// <remarks>
    /// The facade target database for the static CRUD partials — the static layer and the instance
    /// core share the same facade state, so the two layers never split.
    /// </remarks>
    private static IDatabaseService FacadeService
    {
        get { return GameDatabase.ProcessDefault.FacadeService; }
    }

    /// <summary>
    /// 初始化数据库（静态便捷层：转发 <see cref="GameDatabase.InitAsync{T}(DbOptions)"/>）。
    /// </summary>
    /// <remarks>Initialize the database (static convenience forward of <see cref="GameDatabase.InitAsync{T}(DbOptions)"/>).</remarks>
    /// <typeparam name="T">数据库服务的具体实现类型 / Database service implementation type</typeparam>
    /// <param name="dbOptions">数据库配置选项 / Database configuration options</param>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    public static Task<bool> InitAsync<T>(DbOptions dbOptions) where T : IDatabaseService, new()
    {
        return GameDatabase.ProcessDefault.InitAsync<T>(dbOptions);
    }

    /// <summary>
    /// 初始化数据库（静态便捷层：转发 <see cref="GameDatabase.InitAsync(DbOptions)"/>）。
    /// </summary>
    /// <remarks>Initialize the database (static convenience forward of <see cref="GameDatabase.InitAsync(DbOptions)"/>).</remarks>
    /// <param name="dbOptions">数据库配置选项 / Database configuration options</param>
    /// <returns>返回数据库是否初始化成功 / Returns whether the database was initialized successfully</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    [RequiresUnreferencedCode("按命名约定反射创建提供者；AOT/裁剪场景请改用 InitAsync<T> 显式指定实现类型")]
    public static Task<bool> InitAsync(DbOptions dbOptions)
    {
        return GameDatabase.ProcessDefault.InitAsync(dbOptions);
    }

    /// <summary>
    /// 显式指定静态门面的默认库（静态便捷层：转发 <see cref="GameDatabase.SetDefault"/>）。
    /// </summary>
    /// <remarks>Nominates the default database (static convenience forward of <see cref="GameDatabase.SetDefault"/>).</remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    public static void SetDefault(string databaseName)
    {
        GameDatabase.ProcessDefault.SetDefault(databaseName);
    }

    /// <summary>
    /// 判断指定注册名的库是否已注册（静态便捷层：转发 <see cref="GameDatabase.Contains"/>）。
    /// </summary>
    /// <remarks>Determines whether a database is registered (static convenience forward of <see cref="GameDatabase.Contains"/>).</remarks>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <returns>已注册返回 true；否则 false / true when registered; otherwise false</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    public static bool Contains(string databaseName)
    {
        return GameDatabase.ProcessDefault.Contains(databaseName);
    }

    /// <summary>
    /// 以指定类型获取数据库服务实例（静态便捷层：转发 <see cref="GameDatabase.As{T}()"/>）。
    /// </summary>
    /// <remarks>Gets the database service as the specified type (static convenience forward of <see cref="GameDatabase.As{T}()"/>).</remarks>
    /// <typeparam name="T">要转换的数据库服务类型 / Database service type to convert to</typeparam>
    /// <returns>转换后的数据库服务实例 / The converted database service instance</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    public static T As<T>() where T : IDatabaseService
    {
        return GameDatabase.ProcessDefault.As<T>();
    }

    /// <summary>
    /// 按注册名以指定类型获取数据库服务实例（静态便捷层：转发 <see cref="GameDatabase.As{T}(string)"/>）。
    /// </summary>
    /// <remarks>Gets the database service as the specified type by registry name (static convenience forward of <see cref="GameDatabase.As{T}(string)"/>).</remarks>
    /// <typeparam name="T">要转换的数据库服务类型 / Database service type to convert to</typeparam>
    /// <param name="databaseName">注册名 / Registry name</param>
    /// <returns>转换后的数据库服务实例 / The converted database service instance</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    public static T As<T>(string databaseName) where T : IDatabaseService
    {
        return GameDatabase.ProcessDefault.As<T>(databaseName);
    }

    /// <summary>
    /// 异步关闭数据库连接并卸载门面（静态便捷层：转发 <see cref="GameDatabase.CloseAsync"/>）。
    /// </summary>
    /// <remarks>Closes every registered database and tears the facade down (static convenience forward of <see cref="GameDatabase.CloseAsync"/>).</remarks>
    /// <returns>表示异步关闭操作的任务 / Task representing the asynchronous close operation</returns>
    [Obsolete("静态便捷层过渡保留：请改用注入的 GameDatabase 实例（进程缺省 GameDatabase.ProcessDefault）。Static convenience layer kept for transition; use the injected GameDatabase instance (process default GameDatabase.ProcessDefault) instead.", false)]
    public static Task CloseAsync()
    {
        return GameDatabase.ProcessDefault.CloseAsync();
    }

    /// <summary>
    /// 重置门面与多库注册表（静态便捷层测试隔离入口：与实例核 ResetForTesting 同核）。
    /// </summary>
    /// <remarks>Resets the facade and the registry (the static test-isolation entry; same core as the instance ResetForTesting).</remarks>
    internal static void ResetForTesting()
    {
        GameDatabase.ProcessDefault.ResetForTesting();
    }
}
