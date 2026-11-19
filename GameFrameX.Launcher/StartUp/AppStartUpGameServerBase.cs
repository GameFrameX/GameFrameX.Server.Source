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


using GameFrameX.Apps.Common.EventData;
using GameFrameX.Core.Timer;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using GameFrameX.Launcher.Composition;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Message;

namespace GameFrameX.Launcher.StartUp;

/// <summary>
/// 单进程应用宿主，持有监听、业务装配和完整停止任务。
/// <para>
/// 固定启停序列由本模板直排：启动 = 业务库 → 组件注册 → 热更装载 → 能力组合 → 监听开放 →
/// ServiceOnline 分发 → 启动就绪屏障；停机 = admin 运行时 → 网络停机 → ServiceOffline 分发 →
/// 能力运行时 → 热更回存关库，严格逆序。
/// </para>
/// </summary>
internal abstract class AppStartUpGameServerBase : AppStartUpBase
{
    private readonly RuntimeCompositionRoot _compositionRoot = new RuntimeCompositionRoot();
    private readonly object _lifecycleLock = new object();
    private Capability.Hosting.Core.CapabilityRuntime _composedRuntime;
    private Task _startupTask;
    private Task _stopTask;
    private Task _runtimeStopTask;
    private Task _networkStopTask;
    private string _stopMessage;
    private int _offlinePublished;

    /// <summary>
    /// 启动应用：等待已登记的启动序列屏障任务（未初始化直启形态在锁内兜底登记），随后阻塞等待应用退出令牌；
    /// 任一步失败时记录致命日志并重抛，由 finally 的停机路径对已启动部分逆序尽力清理（清理失败逐项收集，不中断其余清理步骤）。
    /// </summary>
    public sealed override async Task StartAsync()
    {
        string exitMessage = null;
        try
        {
            // 屏障任务在初始化阶段已登记（见 Init）；此处兜底覆盖测试等未走 Init 的直启形态。
            await RegisterStartupBarrierAsync();

            await _startupTask;
            exitMessage = await AppExitToken;
        }
        catch (Exception exception)
        {
            LogHelper.Fatal(exception);
            throw;
        }
        finally
        {
            await ShutdownAsync(exitMessage);
        }
    }

    /// <summary>
    /// 幂等登记启动序屏障任务：首个调用者胜出，整条启动序（含启动日志到就绪屏障）作为并发停机的等待屏障。
    /// </summary>
    private Task RegisterStartupBarrierAsync()
    {
        lock (_lifecycleLock)
        {
            return _startupTask ??= RunStartupSequenceAsync();
        }
    }


    /// <summary>
    /// 启动主序直排：启动日志 → PrepareAsync → ActorLimit → 业务库 → 组件注册 → 热更装载 →
    /// 能力组合 → 监听开放 → ServiceOnline 分发 → 启动结束日志 → 就绪屏障。
    /// 能力运行时在序列内先存入 <c>_composedRuntime</c> 再继续，保证停机序在屏障任务收尾后能摘到已启动的运行时。
    /// </summary>
    private async Task RunStartupSequenceAsync()
    {
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartBegin, Setting.ServerType));
        await PrepareAsync();
        if (!Enum.TryParse<ActorLimit.RuleType>(Setting.ActorLimitRule, true, out var actorLimitRule))
        {
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.ActorLimit.UnsupportedRuleType, Setting.ActorLimitRule));
            actorLimitRule = ActorLimit.RuleType.None;
        }

        ActorLimit.Init(actorLimitRule);
        await InitializeBusinessDatabaseAsync();
        await RegisterComponentsAsync();
        await LoadHotfixModuleAsync();
        _composedRuntime = await ComposeCapabilityRuntimeAsync();
        await StartNetworkListenersAsync();
        await OnStartupCompletedAsync();
        LogHelper.DebugConsole(LocalizationService.GetString(Localization.Keys.Launcher.EnterMainLoop));
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartEnd, Setting.ServerType));
        MarkStartUpReady();
    }

    protected virtual Task PrepareAsync()
    {
        return Task.CompletedTask;
    }

    /// <summary>启动完成分发（ServiceOnline 事件；晚于监听开放、早于就绪屏障）。</summary>
    protected virtual Task OnStartupCompletedAsync()
    {
        GameFrameX.Core.Events.GameEventDispatcher.ProcessDefault.Dispatch(0,
            new ServiceOnlineEventArgs(Setting.ServerType, Setting.ServerInstanceId, DateTime.UtcNow));
        return Task.CompletedTask;
    }

    public sealed override Task StopAsync(string message = "")
    {
        lock (_lifecycleLock)
        {
            return _stopTask ??= StopCoreAsync(message);
        }
    }

    /// <summary>
    /// 停机核心序（各步尽力执行，单步失败只收集不中断其余步骤）：admin 运行时 → 网络停机 →
    /// ServiceOffline 分发 → 能力运行时 → 热更回存关库。
    /// </summary>
    private async Task StopCoreAsync(string message)
    {
        _stopMessage = message;
        if (_startupTask != null)
        {
            try
            {
                await _startupTask;
            }
            catch
            {
                // 启动异常由 StartAsync 传播，仍完成失败清理。
            }
        }

        var failures = new List<Exception>();
        async Task AttemptAsync(Func<Task> action)
        {
            try
            {
                await action();
            }
            catch (Exception exception)
            {
                failures.Add(exception);
            }
        }

        // 先停管理面与客户端监听，再逆序停能力，最终热更清理负责回存关库。
        var adminRuntime = _compositionRoot.DetachAdminOnlyFamilyRuntime(Setting.ServerType);
        if (adminRuntime != null)
        {
            await AttemptAsync(() => adminRuntime.StopAsync());
        }

        await AttemptAsync(() => StopNetworkAndMarkStoppedAsync(message));
        await AttemptAsync(() => PublishServiceOfflineAsync(message));
        var runtime = _composedRuntime;
        _composedRuntime = null;
        if (runtime != null)
        {
            await AttemptAsync(() => runtime.StopAsync());
        }

        await AttemptAsync(() => StopRuntimeAsync());
        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

    /// <summary>ServiceOffline 事件分发（停机序中晚于网络停机、早于能力停止）。</summary>
    protected virtual Task OnStoppingAsync(string message)
    {
        GameFrameX.Core.Events.GameEventDispatcher.ProcessDefault.Dispatch(0,
            new ServiceOfflineEventArgs(Setting.ServerType, Setting.ServerInstanceId, "Stopped", DateTime.UtcNow));
        return Task.CompletedTask;
    }

    protected virtual Task ShutdownAsync(string exitMessage)
    {
        return StopAsync(exitMessage ?? string.Empty);
    }

    /// <summary>
    /// 初始化：缺省配置兜底 + 基类初始化 + 启动序屏障登记。
    /// 屏障在此登记（早于退出处理器武装）：武装后任意时点到达的停机都等待启动序列收尾后统一清理，
    /// 完整消除「停机对空状态提前完成、启动序列继续跑完」的窗口；启动序列随初始化开始执行。
    /// </summary>
    protected override void Init()
    {
        Setting ??= CreateDefaultSetting();
        base.Init();
        RegisterStartupBarrierAsync();
    }

    protected abstract AppSetting CreateDefaultSetting();

    /// <summary>装载热更模块（晚于组件注册、早于能力组合；装载失败抛出并触发停机清理）。</summary>
    protected virtual async Task LoadHotfixModuleAsync()
    {
        if (!await HotfixRuntime.ProcessDefault.LoadHotfixModuleAsync(Setting))
        {
            throw new InvalidOperationException("Hotfix module initialization failed.");
        }
    }

    /// <summary>注册应用层组件（业务库就绪后、热更装载前执行一次）。</summary>
    protected virtual Task RegisterComponentsAsync()
    {
        return ComponentRegister.InitAsync(typeof(AppsHandler).Assembly);
    }

    /// <summary>停止热更与数据库运行时（幂等缓存首次停止任务；停机序最后一步）。</summary>
    private Task StopRuntimeAsync()
    {
        lock (_lifecycleLock)
        {
            return _runtimeStopTask ??= StopRuntimeCoreAsync();
        }
    }

    /// <summary>热更与数据库最终清理：热更已装载时停热更；仅数据库打开时由全局定时器回存并关库。</summary>
    protected virtual Task StopRuntimeCoreAsync()
    {
        if (HotfixRuntime.ProcessDefault.HotfixAssembly != null)
        {
            return HotfixRuntime.ProcessDefault.StopAsync(_stopMessage ?? string.Empty);
        }

        if (GameDatabase.ProcessDefault.Contains(Setting.DataBaseName))
        {
            // 数据库已打开而热更尚未装载时，仍由同一个最终回存/关库入口清理。
            return GlobalTimer.StopAsync();
        }

        return Task.CompletedTask;
    }

    /// <summary>初始化业务数据库（固定启动序第一步）。</summary>
    protected virtual async Task InitializeBusinessDatabaseAsync()
    {
        var gameDatabase = GameDatabase.ProcessDefault;
        if (!gameDatabase.Contains(Setting.DataBaseName))
        {
            var initialized = await gameDatabase.InitAsync(new DbOptions
            {
                Provider = Setting.DatabaseProvider,
                ConnectionString = Setting.DataBaseUrl,
                Name = Setting.DataBaseName,
                IsUseTimeZone = Setting.IsUseTimeZone,
            });
            if (!initialized)
            {
                throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.Launcher.DatabaseServiceStartFailed));
            }
        }
    }

    /// <summary>开放网络监听（能力就绪后、ServiceOnline 分发前）。</summary>
    protected virtual Task StartNetworkListenersAsync()
    {
        var runtime = HotfixRuntime.ProcessDefault;
        return StartServerAsync<DefaultMessageDecoderHandler, DefaultMessageEncoderHandler>(
            new DefaultMessageCompressHandler(), new DefaultMessageDecompressHandler(),
            runtime.GetListHttpHandler(), runtime.GetHttpHandler);
    }

    /// <summary>
    /// 组合并启动能力运行时（族运行时或 admin-only 运行时；双闸全关时返回 null）。
    /// 停机序负责停止已交付的运行时；组合失败时组合根自行回滚已启动部分。
    /// </summary>
    protected virtual Task<Capability.Hosting.Core.CapabilityRuntime> ComposeCapabilityRuntimeAsync()
    {
        return _compositionRoot.ComposeAndStartAsync(Setting);
    }

    /// <summary>分发 ServiceOffline（幂等：首个停机路径分发一次，后续调用直接完成）。</summary>
    private Task PublishServiceOfflineAsync(string message)
    {
        if (Interlocked.Exchange(ref _offlinePublished, 1) != 0)
        {
            return Task.CompletedTask;
        }

        return OnStoppingAsync(message);
    }

    /// <summary>停止网络监听并标记进程停止（停机序中先于 ServiceOffline 分发；幂等缓存首次停止任务）。</summary>
    protected virtual Task StopNetworkAndMarkStoppedAsync(string message)
    {
        lock (_lifecycleLock)
        {
            return _networkStopTask ??= base.StopAsync(message);
        }
    }

    protected override IMessageHandler ResolveMessageHandler(int messageId)
    {
        return HotfixRuntime.ProcessDefault.GetMessageHandler(messageId);
    }

    /// <summary>
    /// 回复心跳消息（统一形态，全 Role 生效）：回显请求 UniqueId 的池化 NotifyHeartBeat 保活回包。
    /// </summary>
    /// <remarks>
    /// The keepalive reply (the unified shape, effective for every role): echoes the request's
    /// UniqueId in a pooled <c>NotifyHeartBeat</c> carrying the server timestamp. The receive-time
    /// stamping happens in the base funnel before this hook.
    /// </remarks>
    /// <param name="networkChannel">会话网络通道 / The session network channel</param>
    /// <param name="messagePackage">心跳消息包 / The heartbeat message package</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected override async ValueTask ReplyHeartBeatAsync(INetworkChannel networkChannel, INetworkMessagePackage messagePackage)
    {
        if (messagePackage.DeserializeMessageObject() is ReqHeartBeat request)
        {
            var notifyHeartBeat = MessageObjectPoolHelper.Get<NotifyHeartBeat>();
            try
            {
                notifyHeartBeat.Timestamp = TimerHelper.UnixTimeMilliseconds();
                notifyHeartBeat.UniqueId = request.UniqueId;
                await networkChannel.WriteAsync(notifyHeartBeat);
            }
            finally
            {
                // 池化消息用毕归还（WriteAsync 在首个 await 前完成编码消费，归还安全）
                MessageObjectPoolHelper.Return(notifyHeartBeat);
            }
        }
    }
}
