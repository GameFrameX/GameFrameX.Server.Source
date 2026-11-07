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
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Timer;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Utility;
using GameFrameX.Launcher.Composition;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Message;

namespace GameFrameX.Launcher.StartUp;

/// <summary>单进程应用宿主，持有监听、业务装配和完整停止任务。</summary>
internal abstract class AppStartUpGameServerBase : AppStartUpBase, ILegacyRuntimeHostOwner
{
    private readonly RuntimeCompositionRoot _compositionRoot = new RuntimeCompositionRoot();
    private readonly object _lifecycleLock = new object();
    private Capability.Hosting.Core.CapabilityRuntime _composedRuntime;
    private Task<Capability.Hosting.Core.CapabilityRuntime> _compositionTask;
    private Task _stopTask;
    private Task _runtimeStopTask;
    private Task _networkStopTask;
    private string _stopMessage;
    private int _offlinePublished;

    public sealed override async Task StartAsync()
    {
        string exitMessage = null;
        try
        {
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Launcher.ServerStartBegin, Setting.ServerType));
            await PrepareAsync();
            if (!Enum.TryParse<ActorLimit.RuleType>(Setting.ActorLimitRule, true, out var actorLimitRule))
            {
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.ActorLimit.UnsupportedRuleType, Setting.ActorLimitRule));
                actorLimitRule = ActorLimit.RuleType.None;
            }

            ActorLimit.Init(actorLimitRule);
            _compositionTask = _compositionRoot.ComposeAndStartAsync(this);
            _composedRuntime = await _compositionTask;
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

    protected virtual Task PrepareAsync()
    {
        return Task.CompletedTask;
    }

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

    private async Task StopCoreAsync(string message)
    {
        _stopMessage = message;
        if (_compositionTask != null)
        {
            try
            {
                _composedRuntime = await _compositionTask;
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

        await AttemptAsync(() => ((ILegacyRuntimeHostOwner)this).StopNetworkAndMarkStoppedAsync(message));
        await AttemptAsync(() => ((ILegacyRuntimeHostOwner)this).OnStoppingAsync(message));
        var runtime = _composedRuntime;
        _composedRuntime = null;
        if (runtime != null)
        {
            await AttemptAsync(() => runtime.StopAsync());
        }

        await AttemptAsync(() => ((ILegacyRuntimeHostOwner)this).StopRuntimeAsync());
        if (failures.Count > 0)
        {
            throw new AggregateException(failures);
        }
    }

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

    protected override void Init()
    {
        Setting ??= CreateDefaultSetting();
        base.Init();
    }

    protected abstract AppSetting CreateDefaultSetting();

    AppSetting ILegacyRuntimeHostOwner.Setting => Setting;
    string ILegacyRuntimeHostOwner.CurrentStopMessage => _stopMessage;

    async Task ILegacyRuntimeHostOwner.LoadHotfixModuleAsync()
    {
        if (!await HotfixRuntime.ProcessDefault.LoadHotfixModuleAsync(Setting))
        {
            throw new InvalidOperationException("Hotfix module initialization failed.");
        }
    }

    Task ILegacyRuntimeHostOwner.RegisterComponentsAsync()
    {
        return ComponentRegister.InitAsync(typeof(AppsHandler).Assembly);
    }

    Task ILegacyRuntimeHostOwner.StopRuntimeAsync()
    {
        lock (_lifecycleLock)
        {
            return _runtimeStopTask ??= StopRuntimeCoreAsync();
        }
    }

    private async Task StopRuntimeCoreAsync()
    {
        if (HotfixRuntime.ProcessDefault.HotfixAssembly != null)
        {
            await HotfixRuntime.ProcessDefault.StopAsync(_stopMessage ?? string.Empty);
        }
        else if (GameDatabase.ProcessDefault.Contains(Setting.DataBaseName))
        {
            // 数据库已打开而热更尚未装载时，仍由同一个最终回存/关库入口清理。
            await GlobalTimer.StopAsync();
        }
    }

    Task ILegacyRuntimeHostOwner.InitializeBusinessDatabaseAsync()
    {
        return InitializeBusinessDatabaseAsync();
    }

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

    Task ILegacyRuntimeHostOwner.StartNetworkListenersAsync()
    {
        var runtime = HotfixRuntime.ProcessDefault;
        return StartServerAsync<DefaultMessageDecoderHandler, DefaultMessageEncoderHandler>(
            new DefaultMessageCompressHandler(), new DefaultMessageDecompressHandler(),
            runtime.GetListHttpHandler(), runtime.GetHttpHandler);
    }

    Task ILegacyRuntimeHostOwner.OnStartupCompletedAsync()
    {
        return OnStartupCompletedAsync();
    }

    void ILegacyRuntimeHostOwner.MarkStartUpReadyBarrier()
    {
        MarkStartUpReady();
    }

    Task ILegacyRuntimeHostOwner.OnStoppingAsync(string message)
    {
        if (Interlocked.Exchange(ref _offlinePublished, 1) != 0)
        {
            return Task.CompletedTask;
        }

        return OnStoppingAsync(message);
    }

    Task ILegacyRuntimeHostOwner.StopNetworkAndMarkStoppedAsync(string message)
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
