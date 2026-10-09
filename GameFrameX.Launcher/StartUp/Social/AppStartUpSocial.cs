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


using GameFrameX.Core.Components;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.PostgreSql.Discovery;
using GameFrameX.Discovery.Routing;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.HTTP;
using GameFrameX.NetWork.Message;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Apps.Common.Event;
using GameFrameX.Apps.Common.EventData;
using GameFrameX.Core.Events;

namespace GameFrameX.Launcher.StartUp.Social;

/// <summary>
/// 游戏服务器
/// </summary>
// C143b：显式优先级修复与 Game 同为缺省 1000 的冲突——Social 在主服务 Game 之后启动（值越小优先级越高）
[StartUpTag(GameServerConst.Social.Name, 200)]
internal sealed partial class AppStartUpSocial : AppStartUpBase
{
    public override async Task StartAsync()
    {
        try
        {
            var aopHandlerTypes = AssemblyHelper.GetRuntimeImplementTypeNamesInstance<IHttpAopHandler>();
            aopHandlerTypes.Sort((handlerX, handlerY) => handlerX.Priority.CompareTo(handlerY.Priority));
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartBegin));
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigBegin));
            ActorLimit.Init(ActorLimit.RuleType.None);
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.Launcher.ActorLimitConfigEnd));
            // C143a D16：控制库先行于业务库；C159：经 GameDb 统一入口判重/命名；
            // C166：Provider 装配分支——PostgreSql 时控制库走 PostgreSqlDbService（Name 仅注册名，库由连接串决定）。
            if (!GameDb.Contains(GameDb.ControlDatabaseName))
            {
                var controlDatabaseInitResult = await GameDb.Init<PostgreSqlDbService>(Setting.DataBaseUrl, new DbOptions { Name = GameDb.ControlDatabaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
                if (controlDatabaseInitResult == false)
                {
                    throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
                }
            }

            // C143d D11-D15：控制库就绪后激活发现层——读侧 watcher + 写侧心跳（未配置广播端口时自动跳过）
            // 并以真实 case 2/3 转发器重装跨 Role 路由缝。幂等：多 Role 进程首个调用生效。
            // C143e D21：再激活玩家路由层（建 player_route 索引 + 装 SyncTarget），Tier 1 fast-path 注入 SessionManager 适配器。
            // C159：控制库句柄解析下沉到发现层内部；C154：收敛为激活参数对象。
            // C166：Provider 装配分支——PostgreSql 时激活 PG 平行发现层并接 PG SyncTarget。
            PostgreSqlDiscoveryRuntime.Activate(new PostgreSqlDiscoveryActivationOptions
            {
                ConnectionName = GameDb.ControlDatabaseName,
                HostedRoleNames = RoleSet.Current,
                PlayerRouteFastPath = GameFrameX.Apps.Common.Session.SessionManagerFastPathAdapter.Instance,
            });
            GameFrameX.Apps.Common.Session.SessionManager.PlayerRouteSyncTarget = GameFrameX.Discovery.Routing.PlayerRouteResolverBootstrap.SyncTarget;
            // C166 依赖纠偏第二轮：路由胶水装配自 Runtime 拆至组合侧 DiscoveryRoutingWire（发现层 Runtime 不再引用消息胶水程序集）。
            GameFrameX.NetWork.RemoteMessaging.Routing.DiscoveryRoutingWire.Initialize(RoleSet.Current, PostgreSqlDiscoveryRuntime.TableProvider);

            var initResult = await GameDb.Init<PostgreSqlDbService>(Setting.DataBaseUrl, new DbOptions { Name = Setting.DataBaseName, IsUseTimeZone = Setting.IsUseTimeZone, });
            if (initResult == false)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
            }

            // C159：业务库 Init 成功后显式指定门面默认库——控制库先注册会使 set-once 门面静默指向控制库，
            // 全部经 GameDb 门面的业务读写必须落在业务库（根缺陷修复点）。
            GameDb.SetDefault(Setting.DataBaseName);

            await ComponentRegister.Init(typeof(AppsHandler).Assembly);
            HotfixManager.LoadHotfix(Setting);
            await StartServerAsync<DefaultMessageDecoderHandler, DefaultMessageEncoderHandler>(new DefaultMessageCompressHandler(), new DefaultMessageDecompressHandler(), HotfixManager.GetListHttpHandler(), HotfixManager.GetHttpHandler, aopHandlerTypes);
            EventDispatcher.Dispatch(0, (int)EventId.ServiceOnline, new ServiceOnlineEventArgs(Setting.ServerType, Setting.ServerInstanceId, DateTime.UtcNow));

            // C143b D7：启动阶段完成（DB/组件/网络监听均已就绪），放行下一个 Role 的启动屏障
            MarkStartUpReady();
            // C143d D15：启动阶段真正完成（DB/组件/网络监听均已就绪）后才把心跳从 Booting 切到 Active，
            // 避免其他进程在 Social TCP listener 就绪前发现本实例并投递流量。
            PostgreSqlDiscoveryRuntime.MarkActive();

            await AppExitToken;
        }
        catch (Exception e)
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerExecutionException, e));
            LogHelper.Fatal(e);
        }

        await StopAsync();
    }

    protected override async ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        await base.PackageHandler(session, message);
        if (message is INetworkMessagePackage messageObject)
        {
            if (Setting.IsDebug && Setting.IsDebugReceive)
            {
                // 当需要打印心跳，或当前非心跳消息时才输出日志
                // if (Setting.IsDebugReceiveHeartBeat || messageObject.Header.OperationType != (byte)MessageOperationType.HeartBeat)
                // {
                //    
                //     LogHelper.Debug($"---收到[{from} To {ServerType}]  {message.ToFormatMessageString()}");
                // }
            }

            var handler = HotfixManager.GetTcpHandler(messageObject.Header.MessageId);
            await InvokeMessageHandler(handler, messageObject.DeserializeMessageObject(), new DefaultNetWorkChannel(session, Setting));
        }
    }

    public override async Task StopAsync(string message = "")
    {
        EventDispatcher.Dispatch(0, (int)EventId.ServiceOffline, new ServiceOfflineEventArgs(Setting.ServerType, Setting.ServerInstanceId, "Stopped", DateTime.UtcNow));
        await base.StopAsync(message);
    }

    protected override void Init()
    {
        if (Setting == null)
        {
            Setting = new AppSetting
            {
                ServerType = GameServerConst.Social.Name,
                ServerId = GameServerConst.Social.Id,
                InnerPort = 29400,
                HttpIsDevelopment = true,
                IsDebug = true,
                IsDebugSend = true,
                IsDebugReceive = true,
                IsDebugReceiveHeartBeat = false,
                IsDebugSendHeartBeat = false,
                // 数据库连接地址优先从环境变量读取，避免在源码中硬编码凭证（消除 Sonar csharpsquid:S2068）
                DataBaseUrl = Environment.GetEnvironmentVariable("GAMEFRAMEX_SOCIAL_DB_URL") ?? "mongodb://127.0.0.1:27017/?authSource=admin",
                DataBaseName = "gameframex",
            };
        }

        base.Init();
    }
}
