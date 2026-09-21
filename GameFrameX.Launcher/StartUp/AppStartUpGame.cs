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
using GameFrameX.Foundation.Utility;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Online.Runtime;
using GameFrameX.Online.Runtime.AdminApi;
using GameFrameX.Utility.Runtime;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 游戏服务器
/// </summary>
// C143b：显式优先级修复与 Social 同为缺省 1000 的冲突——Game 为主服务先起（值越小优先级越高）
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
            // C143a D16：控制库先行于业务库（D-Single 缺省回落：与业务库共用同一 Mongo 实例连接串，库固定 gameframex_control）
            if (!MultiDbRegistry.Contains(MultiDbRegistry.ControlDatabaseName))
            {
                var controlDatabaseInitResult = await GameDb.Init<MongoDbService>(Setting.DataBaseUrl, new DbOptions { Name = MultiDbRegistry.ControlDatabaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
                if (controlDatabaseInitResult == false)
                {
                    throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
                }
            }

            var initResult = await GameDb.Init<MongoDbService>(Setting.DataBaseUrl, new DbOptions { Name = Setting.DataBaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
            if (initResult == false)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
            }

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartEnd));

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.ComponentRegisterBegin));
            await ComponentRegister.Init(typeof(AppsHandler).Assembly);
            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.ComponentRegisterEnd));

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.HotfixModuleLoadBegin));
            await HotfixManager.LoadHotfixModule(Setting);
            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.HotfixModuleLoadEnd));

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

            LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.EnterMainLoop));
            GameAppRuntime.MarkStarted(TimerHelper.GetNowWithUtc());
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartEnd, Setting.ServerType));
            exitMessage = await AppExitToken;
        }
        catch (Exception e)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExecutionException, e));
            LogHelper.Fatal(e);
        }

        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExitBegin));
        if (_onlineAdminApi != null)
        {
            await _onlineAdminApi.StopAsync();
        }

        if (_onlineRuntime != null)
        {
            await _onlineRuntime.StopAsync();
        }

        await HotfixManager.Stop(exitMessage);
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExitSuccess));
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
