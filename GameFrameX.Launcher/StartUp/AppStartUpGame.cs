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


using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo.Discovery;
using GameFrameX.DataBase.PostgreSql.Discovery;
using GameFrameX.Discovery;
using GameFrameX.Discovery.Routing;
using GameFrameX.Foundation.Utility;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Online.Runtime;
using GameFrameX.Online.Runtime.AdminApi;
using GameFrameX.Utility.Runtime;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 游戏服务器
/// </summary>
// 显式优先级修复与 Social 同为缺省 1000 的冲突——Game 为主服务先起（值越小优先级越高）
[StartUpTag(GameServerConst.Game.Name, 100)]
internal sealed class AppStartUpGame : AppStartUpBase
{
    /// <summary>
    /// Online Runtime 宿主（IsEnableOnlineAdmin 开启时装配；进程退出时随主流程停止）。
    /// </summary>
    private OnlineRuntimeHost _onlineRuntime;

    /// <summary>
    /// Online admin HTTP 服务（与协议端口独立的 Kestrel 监听）。
    /// </summary>
    private OnlineAdminApiServer _onlineAdminApi;

    public override async Task StartAsync()
    {
        string exitMessage = null;
        try
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartBegin, Setting.ServerType));
            var hotfixPath = Directory.GetCurrentDirectory() + "/hotfix";
            if (!Directory.Exists(hotfixPath))
            {
                Directory.CreateDirectory(hotfixPath);
            }

            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigBegin));
            ActorLimit.Init(ActorLimit.RuleType.None);
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigEnd));

            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartBegin));
            await InitializeControlDatabaseAsync();
            ActivateDiscoveryLayer();
            await InitializeBusinessDatabaseAsync();

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartEnd));

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.ComponentRegisterBegin));
            await ComponentRegister.Init(typeof(AppsHandler).Assembly);
            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.ComponentRegisterEnd));

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.HotfixModuleLoadBegin));
            await HotfixManager.LoadHotfixModule(Setting);
            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.HotfixModuleLoadEnd));

            await StartOnlineAdminIfEnabledAsync();

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.EnterMainLoop));
            GameAppRuntime.MarkStarted(TimerHelper.GetNowWithUtc());
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartEnd, Setting.ServerType));
            // 启动阶段完成（DB/组件/热-fix/在线管理均已就绪），放行下一个 Role 的启动屏障
            MarkStartUpReady();
            MarkServiceActive();
            exitMessage = await AppExitToken;
        }
        catch (Exception e)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExecutionException, e));
            LogHelper.Fatal(e);
        }

        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExitBegin));
        await ShutdownOnlineAdminAsync();
        await HotfixManager.Stop(exitMessage);
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExitSuccess));
    }

    /// <summary>
    /// 控制库先行于业务库（D-Single 缺省回落：与业务库共用同一实例连接串），
    /// 经 GameDb 统一入口判重/命名；实现类型按 DbOptions.Provider 由 GameDb 约定解析
    ///（PG 路径下 DbOptions.Name 仅作注册名，数据库由连接串 Database 决定）。
    /// 控制库必须显式 IsDefault = false——门面默认库由业务库声明式提名（缺省 true），
    /// 漏写会在业务库注册时抛 set-once 冲突（启动期 fail-fast）。
    /// </summary>
    private async Task InitializeControlDatabaseAsync()
    {
        if (!GameDb.Contains(GameDb.ControlDatabaseName))
        {
            var controlDatabaseInitResult = await GameDb.Init(new DbOptions { Provider = Setting.DatabaseProvider, ConnectionString = Setting.DataBaseUrl, Name = GameDb.ControlDatabaseName, IsDefault = false, IsUseTimeZone = Setting.IsUseTimeZone, });
            if (controlDatabaseInitResult == false)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
            }
        }
    }

    /// <summary>
    /// 控制库就绪后激活发现层——读侧 watcher + 写侧心跳（未配置广播端口时自动跳过）
    /// 并以真实 case 2/3 转发器重装跨 Role 路由缝。幂等：多 Role 进程首个调用生效。
    /// 再激活玩家路由层（建 player_route 索引 + 装 SyncTarget），Tier 1 fast-path 注入 SessionManager 适配器。
    /// 控制库句柄解析下沉到发现层内部，收敛为激活参数对象。
    /// Provider 装配分支——PostgreSql 时激活 PG 平行发现层（控制库 NpgsqlDataSource 经 GameDb 按名解析）。
    /// 路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集）。
    /// </summary>
    private void ActivateDiscoveryLayer()
    {
        if (Setting.DatabaseProvider == DatabaseProviderType.PostgreSql)
        {
            PostgreSqlDiscoveryRuntime.Activate(new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = RoleSet.Current,
                PlayerRouteFastPath = GameFrameX.Apps.Common.Session.SessionManagerFastPathAdapter.Instance,
            });
            GameFrameX.Apps.Common.Session.SessionManager.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
        }
        else
        {
            MongoDiscoveryRuntime.Activate(new DiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = RoleSet.Current,
                PlayerRouteFastPath = GameFrameX.Apps.Common.Session.SessionManagerFastPathAdapter.Instance,
            });
            GameFrameX.Apps.Common.Session.SessionManager.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
        }
        GameFrameX.NetWork.RemoteMessaging.Routing.DiscoveryRoutingWire.Initialize(RoleSet.Current, Setting.DatabaseProvider == DatabaseProviderType.PostgreSql ? PostgreSqlDiscoveryRuntime.TableProvider : MongoDiscoveryRuntime.TableProvider);
    }

    /// <summary>
    /// 业务库 Init，实现类型按 DbOptions.Provider 由 GameDb 约定解析；缺省 IsDefault = true——
    /// 注册成功即声明式提名门面默认库，全部经 GameDb 门面的业务读写必须落在业务库
    ///（控制库已显式 IsDefault = false，先注册不会抢占门面）；失败抛 <see cref="InvalidOperationException"/>。
    /// </summary>
    private async Task InitializeBusinessDatabaseAsync()
    {
        var initResult = await GameDb.Init(new DbOptions { Provider = Setting.DatabaseProvider, ConnectionString = Setting.DataBaseUrl, Name = Setting.DataBaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
        if (initResult == false)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
        }
    }

    /// <summary>
    /// Online admin 可选启动——仅在 <see cref="AppSetting.IsEnableOnlineAdmin"/> 为 true 时装配
    /// Online runtime 宿主与 Online admin HTTP 服务。
    /// </summary>
    private async Task StartOnlineAdminIfEnabledAsync()
    {
        if (Setting.IsEnableOnlineAdmin)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineAdminStartBegin, Setting.OnlineAdminPort));
            _onlineRuntime = new OnlineRuntimeHost(new OnlineRuntimeOptions
            {
                TenantId = Setting.OnlineTenantId,
                AppId = Setting.OnlineAppId,
                ServerId = Setting.ServerId,
                AdminPort = Setting.OnlineAdminPort,
                AdminApiPrefix = Setting.OnlineAdminApiPrefix,
            });
            await _onlineRuntime.StartAsync();
            _onlineAdminApi = new OnlineAdminApiServer(_onlineRuntime);
            await _onlineAdminApi.StartAsync();
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.OnlineAdminStartEnd, Setting.OnlineTenantId, Setting.OnlineAppId, Setting.ServerId));
        }
    }

    /// <summary>
    /// 启动阶段真正完成（DB/组件/热-fix/在线管理均已就绪）后才把心跳从 Booting 切到 Active，
    /// 避免其他进程在服务就绪前发现本实例并投递流量；未激活发现层或无广播身份时为无害 no-op。
    /// </summary>
    private void MarkServiceActive()
    {
        if (Setting.DatabaseProvider == DatabaseProviderType.PostgreSql)
        {
            PostgreSqlDiscoveryRuntime.MarkActive();
        }
        else
        {
            MongoDiscoveryRuntime.MarkActive();
        }
    }

    /// <summary>
    /// 退出期按反向顺序停掉 Online admin HTTP 服务与 Online runtime 宿主；
    /// 未启动（<see cref="AppSetting.IsEnableOnlineAdmin"/> 为 false）时为空判跳过。
    /// </summary>
    private async Task ShutdownOnlineAdminAsync()
    {
        if (_onlineAdminApi != null)
        {
            await _onlineAdminApi.StopAsync();
        }

        if (_onlineRuntime != null)
        {
            await _onlineRuntime.StopAsync();
        }
    }

    protected override void Init()
    {
        if (Setting == null)
        {
            Setting = new AppSetting
            {
                ServerId = GameServerConst.Game.Id,
                ServerType = GameServerConst.Game.Name,
                InnerPort = 29100,
                MetricsPort = 29090,
                HttpPort = 28080,
                WsPort = 29110,
                MinModuleId = 10,
                HttpIsDevelopment = true,
                MaxModuleId = 9999,
                TagName = "GameFrameX",
                // 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）
                DataBaseUrl = Environment.GetEnvironmentVariable("GAMEFRAMEX_GAME_DB_URL") ?? "mongodb://127.0.0.1:27017/?authSource=admin",
                DataBaseName = "gameframex",
            };
        }

        base.Init();
    }
}
