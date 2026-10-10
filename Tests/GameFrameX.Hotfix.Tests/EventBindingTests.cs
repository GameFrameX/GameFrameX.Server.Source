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
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Reflection;
using System.Threading.Tasks;
using GameFrameX.Apps.Common.EventData;
using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Components;
using GameFrameX.Core.Events;
using GameFrameX.Core.Hotfix;
using GameFrameX.Hotfix.Logic.Player.Login;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 事件系统 Type 键绑定单测：同一 EventArgs 类型多监听器全部命中、按类型身份核对注册结果、
/// 未注册类型查找为空（派发走 NoListeners 告警路径）、null 载荷 fail fast。
/// </summary>
public sealed class EventBindingTests
{
    private static readonly object ModuleLock = new object();
    private static HotfixModule _module;
    private static bool _componentsRegistered;

    private static HotfixModule CreateModule()
    {
        if (_module != null)
        {
            return _module;
        }

        lock (ModuleLock)
        {
            _module ??= BuildModule();
        }

        return _module;
    }

    private static HotfixModule BuildModule()
    {
        // 组件 → Actor 类型映射先行（AddEvent 注册监听器时按其组件代理反查 actorType）；只初始化一次。
        if (!_componentsRegistered)
        {
            lock (ModuleLock)
            {
                if (!_componentsRegistered)
                {
                    ComponentRegister.Init(typeof(PlayerComponent).Assembly).GetAwaiter().GetResult();
                    _componentsRegistered = true;
                }
            }
        }
        return new HotfixModule(typeof(SessionRemoveEventListener).Assembly);
    }

    private static ushort PlayerActorType()
    {
        var actorType = ComponentRegister.GetActorType(typeof(PlayerComponent));
        Assert.NotEqual((ushort)0, actorType);
        return actorType;
    }

    [Fact]
    public void Dispatch_NullEventArgs_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => EventDispatcher.Dispatch(0, null));
    }

    [Fact]
    public void FindListeners_ByEventArgsType_ReturnsRegisteredListenerByTypeIdentity()
    {
        var module = CreateModule();
        var listeners = module.FindListeners(PlayerActorType(), typeof(SessionRemovedEventArgs));
        var listener = Assert.Single(listeners);
        Assert.Equal(typeof(SessionRemoveEventListener), listener.GetType());
    }

    [Fact]
    public void FindListeners_SameEventArgsType_MultipleListeners_AllRegistered()
    {
        var module = BuildModule();
        var addEvent = typeof(HotfixModule).GetMethod("AddEvent", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(addEvent);
        Assert.True((bool)addEvent.Invoke(module, new object[] { typeof(ExtraSessionRemovedEventListener) }));

        var listeners = module.FindListeners(PlayerActorType(), typeof(SessionRemovedEventArgs));
        Assert.Equal(2, listeners.Count);
        Assert.Contains(listeners, l => ReferenceEquals(l.GetType(), typeof(SessionRemoveEventListener)));
        Assert.Contains(listeners, l => ReferenceEquals(l.GetType(), typeof(ExtraSessionRemovedEventListener)));
    }

    [Fact]
    public void FindListeners_UnregisteredEventArgsType_ReturnsNull()
    {
        var module = CreateModule();
        Assert.Null(module.FindListeners(PlayerActorType(), typeof(UnregisteredProbeEventArgs)));
    }

    [Fact]
    public void FindListeners_GlobalOverload_AggregatesAcrossActorTypes()
    {
        var module = CreateModule();
        var listeners = module.FindListeners(typeof(SessionRemovedEventArgs));
        var listener = Assert.Single(listeners);
        Assert.Equal(typeof(SessionRemoveEventListener), listener.GetType());
    }

    /// <summary>
    /// 追加注册到 SessionRemovedEventArgs 的第二监听器（仅用于多监听器命中断言）。
    /// </summary>
    [Event(typeof(SessionRemovedEventArgs))]
    internal sealed class ExtraSessionRemovedEventListener : EventListener<PlayerComponentAgent>
    {
        protected override Task HandleEvent(PlayerComponentAgent agent, GameEventArgs gameEventArgs)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// 未注册事件参数类型探针（仅用于 NoListeners 空查找断言，不绑定任何监听器）。
    /// </summary>
    private sealed class UnregisteredProbeEventArgs : GameEventArgs
    {
    }
}
