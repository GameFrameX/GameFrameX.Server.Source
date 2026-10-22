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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.Core.Session;
using GameFrameX.Network.RemoteMessaging;
using GameFrameX.Discovery.Routing;
using GameFrameX.Network.RemoteMessaging.Routing;
using GameFrameX.Hotfix.Logic.Server.Unified;
using GameFrameX.Network.RemoteMessaging.Unified;

namespace GameFrameX.Hotfix.Lifecycle;

/// <summary>
/// 热更模块生命周期桥接（原名 <c>AppStartUpHotfixGame</c>，C285 去启动基类继承并更名迁移）：
/// 热更域只持业务生命周期——装载成功激活配置表/定时器/全局组件，停止只清 Actor/定时器/会话；
/// 网络监听与运行至退出周期归宿主（<c>AppStartUpGameServerBase</c> 模板）。
/// 本类是 <see cref="IHotfixBridge"/> 的唯一实现，由 <c>HotfixModule.ConvertHotfixAssembly</c> 按接口定位并创建，
/// 不属于启动 Role 体系（不进 <c>StartUpTypeRegistry</c> / <c>AppEnter</c>，不继承任何启动基类）。
/// </summary>
/// <remarks>
/// The hotfix-module lifecycle bridge (formerly <c>AppStartUpHotfixGame</c>; C285 dropped the
/// startup-base inheritance, renamed and relocated the type): the hotfix domain holds only the
/// business lifecycle — a successful load activates the config tables, timers and global components
/// while the stop clears actors, timers and sessions; the network listeners and the run-until-exit
/// cycle stay with the host (<c>AppStartUpGameServerBase</c> template).
/// This class is the sole implementation of <see cref="IHotfixBridge"/>, located and instantiated
/// by <c>HotfixModule.ConvertHotfixAssembly</c> via the interface; it does not belong to the startup-role
/// system (never enters <c>StartUpTypeRegistry</c> / <c>AppEnter</c> and inherits no startup base).
/// </remarks>
internal sealed class HotfixModuleLifecycleBridge : IHotfixBridge
{
    /// <summary>
    /// 热更模块装载成功回调：纯业务生命周期激活（CurrentSetting 钉住 + 统一消息发送器装配 +
    /// 配置表 + 定时器 + 全局组件）。热更重载（<paramref name="reload"/> = true）只清空代理缓存，
    /// 不重复激活。
    /// </summary>
    /// <remarks>
    /// The hotfix-module load-success callback: pure business-lifecycle activation (the
    /// CurrentSetting pin, the unified-message-sender wiring, config tables, timers and global
    /// components). A hot reload (<paramref name="reload"/> = true) only clears the agent cache
    /// without re-activating.
    /// C284 起不再起网络监听（监听所有权归宿主），也不分发 <c>ServiceOnline</c>（宿主
    /// <c>OnStartupCompletedAsync</c> 是唯一分发点，避免与宿主缺省分发双派发）。
    /// Since C284 this callback no longer starts network listeners (listener ownership stays with
    /// the host) and no longer dispatches <c>ServiceOnline</c> (the host's
    /// <c>OnStartupCompletedAsync</c> is the single dispatch point, avoiding a double dispatch
    /// against the host default).
    /// </remarks>
    /// <param name="setting">应用程序配置 / The application setting.</param>
    /// <param name="reload">是否为热更重载 / Whether this is a hot reload.</param>
    /// <returns>一个任务，表示异步操作的结果，并返回加载是否成功的布尔值 / A task that completes with whether the load succeeded</returns>
    public async Task<bool> OnLoadSuccessAsync(AppSetting setting, bool reload)
    {
        if (reload)
        {
            ActorManager.ClearAgent();
            return true;
        }

        // 多 Role 进程（--AllInOne）中钉住 CurrentSetting（C285 自 AppStartUpBase.Init 显式化）：
        // Launcher 的创建循环先对全部 Role 逐个 Init，SetCurrentSetting 对 Role 级字段接受最新值，
        // 循环结束时 CurrentSetting 指向最后创建的 Role；热更首次装载由首个启动的 Role 触发，
        // 这里把 CurrentSetting 钉回该首启动 Role，供路由/会话等调用期读取方取到稳定身份
        // （发送端身份自 C286 起改为构造注入，不再依赖本钉住的时序与值）。
        // 单 Role 形态无差异；直接删除会让调用期读取方的服务器身份漂移。

        GlobalSettings.SetCurrentSetting(setting);
        // 初始化统一消息发送器：resolver 按发现层可用性选择——
        // 启动流程先 Activate 发现层（含 player_route 读侧），后加载 Hotfix，故此处
        // Bootstrap.Resolver 必然已就绪；未激活发现层的形态回落 DefaultPlayerRouteResolver。
        // localSender 单实例同时供 UnifiedMessageSenderHolder 与 LocalEnvelopeDispatcher，保证一条本地复投语义。
        // 发送端本服身份（C286）构造注入自本回调收到的 setting，不依赖 CurrentSetting 的钉住时序
        // （钉住仍服务 PlayerSessionManager 等调用期读取方）。
        if (!UnifiedMessageSenderHolder.IsInitialized)
        {
            var remoteClient = RemoteMessagingBuilder.BuildFromEnvironment();
            var localSender = new DefaultPlayerLocalSender();
            // 发现层存储适配归一——Resolver/SyncTarget 单例由通用 Bootstrap 提供（不再按 Provider 分立）。
            GameFrameX.Discovery.Routing.IPlayerRouteResolver discoveredResolver = PlayerRouteResolverBootstrap.Resolver;
            var routeResolver = PlayerRouteWiring.SelectRouteResolver(discoveredResolver);
            UnifiedMessageSenderHolder.InitializeWithDefaults(
                routeResolver,
                localSender,
                remoteClient,
                setting.ServerType,
                setting.ServerId);

            // holder 初始化后补装路由缝 case 1 的本地投递槽（幂等；时序前提见 AttachLocalDispatcher 注释）。
            // 本地投递槽装配统一走组合侧 DiscoveryRoutingWire（两 Provider 共用，不再按实现分支）。
            GameFrameX.Network.RemoteMessaging.Routing.DiscoveryRoutingWire.AttachLocalDispatcher(new LocalEnvelopeDispatcher(localSender));
        }

        // 不管是不是重启服务器，都要加载配置（原 RunServer 首段；C284 起监听由宿主持有，热更只保纯生命周期激活）
        await ConfigComponent.Instance.LoadConfigAsync();
        // 启动定时器
        await QuartzTimer.StartAsync();
        GlobalTimer.Start();
        await ComponentRegister.ActiveGlobalComponentsAsync();
        return true;
    }

    /// <summary>
    /// 热更模块停止回调：纯业务清理——断开全部会话、停定时器、排空并回收 Actor。
    /// </summary>
    /// <remarks>
    /// The hotfix-module stop callback: pure business cleanup — disconnects every session, stops
    /// the timers, then drains and removes the actors. C284 起不再分发 <c>ServiceOffline</c>、
    /// 不再触碰网络监听与运行时状态（宿主 <c>StopAsync</c> 是停机与 <c>ServiceOffline</c>
    /// 分发的唯一入口；本回调只清热更域内业务状态）。
    /// Since C284 this callback no longer dispatches <c>ServiceOffline</c> and no longer touches
    /// the network listeners or the runtime state (the host <c>StopAsync</c> is the single entry
    /// for the shutdown flow and the <c>ServiceOffline</c> dispatch; this callback only clears the
    /// hotfix-domain business state).
    /// </remarks>
    /// <param name="message">停止原因 / The stop reason.</param>
    /// <returns>一个任务，表示异步操作的结果 / A task that completes when the cleanup is done</returns>
    public async Task StopAsync(string message = "")
    {
        // 断开所有连接
        await PlayerSessionManager.Instance.RemoveAllAsync();
        // 取消所有未执行定时器
        await QuartzTimer.StopAsync();
        // 保证actor之前的任务都执行完毕
        await ActorManager.AllFinishAsync();
        // 存储所有数据
        await GlobalTimer.StopAsync();
        // 删除所有actor
        await ActorManager.RemoveAllAsync();
    }

}
