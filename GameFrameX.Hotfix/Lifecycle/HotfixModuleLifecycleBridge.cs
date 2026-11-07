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
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Hotfix.Logic.Server.Chat;

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
    /// 当前热更版本挂载的聊天审核钩子租约（装载成功挂载、停止 / 退役释放；
    /// 新版本重挂时 <see cref="GameFrameX.Capability.Hosting.Abstractions.CapabilityHook{TContract}.Attach"/>
    /// 原子替换并使本租约失效，能力侧不会钉住旧程序集）。
    /// </summary>
    /// <remarks>
    /// The chat-moderation hook lease mounted by the current hotfix version (attached on load success,
    /// released on stop / retirement; a re-attach by a newer version atomically replaces and invalidates
    /// this lease so the capability side never pins the old assembly).
    /// </remarks>
    private CapabilityHookLease<IOnlineChatModerationHook> _chatModerationHookLease;

    /// <summary>装载配置、定时器与全部应用组件；重载只更新热更逻辑与钩子。</summary>
    public async Task<bool> OnLoadSuccessAsync(AppSetting setting, bool reload)
    {
        // 聊天审核钩子挂载（每次装载成功都执行：新版本重挂原子替换旧挂载，旧租约自动失效）。
        _chatModerationHookLease?.Dispose();
        _chatModerationHookLease = CapabilityHook<IOnlineChatModerationHook>.Attach(new GameChatModerationHook());

        if (reload)
        {
            ActorManager.ClearAgent();
            return true;
        }


        GlobalSettings.SetCurrentSetting(setting);

        await ConfigComponent.Instance.LoadConfigAsync();
        // 启动定时器
        await QuartzTimer.StartAsync();
        GlobalTimer.Start();
        await ComponentRegister.ActiveGlobalComponentsAsync();
        return true;
    }

    private readonly object _stopLock = new object();
    private Task _stopTask;

    /// <summary>并发停止共享同一任务，最后回存和关库只执行一次。</summary>
    /// <remarks>
    /// 排空超时后的最终回存仅为终止过程中的尽力处理，不保证未完成事件已执行或持久化。
    /// 有界停机无法保证在执行业务完成；原始超时及清理异常共同上抛，缓存的停止任务保持失败。
    /// Final persistence after a drain timeout is best effort and does not guarantee that unfinished
    /// business events have completed or been persisted. Original and cleanup failures are propagated;
    /// the cached shutdown task remains faulted, including on repeated calls.
    /// </remarks>
    public Task StopAsync(string message = "")
    {
        lock (_stopLock)
        {
            return _stopTask ??= StopCoreAsync();
        }
    }

    private async Task StopCoreAsync()
    {
        var cleanupFailures = new List<Exception>();
        async Task AttemptCleanupAsync(Func<Task> cleanup)
        {
            try
            {
                await cleanup();
            }
            catch (Exception exception)
            {
                cleanupFailures.Add(exception);
            }
        }

        try
        {
            await AttemptCleanupAsync(() =>
            {
                _chatModerationHookLease?.Dispose();
                _chatModerationHookLease = null;
                return Task.CompletedTask;
            });

            // 停止生产者的一个步骤失败，也继续尝试其它生产者和会话的清理。
            await AttemptCleanupAsync(GlobalTimer.QuiesceAsync);
            await AttemptCleanupAsync(QuartzTimer.StopAsync);
            await AttemptCleanupAsync(PlayerSessionManager.Instance.RemoveAllAsync);

            if (!await GameEventDispatcher.ProcessDefault.WaitAllDispatchedAsync(TimeSpan.FromSeconds(20)))
            {
                throw new TimeoutException("Hotfix shutdown could not drain pending business events.");
            }

            await ActorManager.AllFinishAsync();
            await AttemptCleanupAsync(ActorManager.RemoveAllAsync);
            if (!await GameEventDispatcher.ProcessDefault.WaitAllDispatchedAsync(TimeSpan.FromSeconds(20)))
            {
                throw new TimeoutException("Hotfix shutdown could not drain actor cleanup events.");
            }

            await ActorManager.AllFinishAsync();
        }
        catch (Exception exception)
        {
            // 排空失败时不并发回收仍在执行的 Actor，保留失败并进入最终回存/关库兜底。
            cleanupFailures.Add(exception);
        }
        finally
        {
            // 成功、超时及异常路径均只进入一次；失败任务继续缓存，重复 Stop 不重复保存/关闭。
            await AttemptCleanupAsync(GlobalTimer.StopAsync);
        }

        if (cleanupFailures.Count > 0)
        {
            throw new AggregateException(cleanupFailures);
        }
    }
}
