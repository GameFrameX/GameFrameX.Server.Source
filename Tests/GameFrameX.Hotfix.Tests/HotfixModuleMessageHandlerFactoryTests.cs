// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他权利
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//  ==========================================================================================

using System.Collections;
using System.Reflection;
using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Network.Abstractions;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 消息处理器注册期工厂化回归护栏。
/// </summary>
/// <remarks>
/// 带 <c>[MessageMapping]</c> 的处理器类型必须在热更程序集装载期完成可实例化校验（实现 <c>IMessageHandler</c>
/// 且具有公共无参构造函数），而不是推迟到首条消息到达才以类型异常爆出；消息处理器映射表保存注册期构建的
/// 强类型工厂委托，<c>GetMessageHandler</c> 每次调用产出新实例（处理器有状态，禁止单例或池化）。
/// </remarks>
public sealed class HotfixModuleMessageHandlerFactoryTests
{
    /// <summary>
    /// 测试专用消息号一（不参与任何真实协议派发，仅保证两个探针映射到不同消息类型）。
    /// </summary>
    private const int FirstProbeMessageId = 990_001;

    /// <summary>
    /// 测试专用消息号二。
    /// </summary>
    private const int SecondProbeMessageId = 990_002;

    [Fact]
    public async Task HotfixModule_Construction_ThrowsFast_WhenMessageHandlerTypeViolatesInstantiationContract()
    {
        await InitializeEnvironmentAsync();

        // 测试程序集内只有本测试的两个探针携带 [MessageMapping]（已核对无其他来源），
        // 类型枚举顺序不确定，两个探针违例形态不同但装载即抛同一契约异常
        var exception = Assert.Throws<InvalidOperationException>(() => new HotfixModule(typeof(NotMessageHandlerProbeHandler).Assembly));

        // 异常必须携带服务类型定位，装载日志可直接定位问题注册项
        Assert.Contains(nameof(IMessageHandler), exception.Message);
        Assert.True(
            exception.Message.Contains(typeof(NotMessageHandlerProbeHandler).FullName!)
            || exception.Message.Contains(typeof(MissingParameterlessConstructorMessageHandler).FullName!),
            $"装载异常应指向其中一个探针类型，实际: {exception.Message}");
    }

    [Fact]
    public async Task CreateFactory_RejectsMessageHandlerTypeNotImplementingIMessageHandler()
    {
        await RunCreateFactoryRejectsProbeAsync(typeof(NotMessageHandlerProbeHandler));
    }

    [Fact]
    public async Task CreateFactory_RejectsMessageHandlerWithoutPublicParameterlessConstructor()
    {
        await RunCreateFactoryRejectsProbeAsync(typeof(MissingParameterlessConstructorMessageHandler));
    }

    [Fact]
    public async Task HotfixModule_MessageHandlerMap_StoresFactories_AndGetMessageHandlerCreatesFreshInstances()
    {
        await InitializeEnvironmentAsync();

        var module = new HotfixModule(typeof(LoginComponentAgent).Assembly);
        var handlerMap = ReadPrivateDictionary(module, "_messageHandlerMap");
        Assert.NotEmpty(handlerMap);

        int? probeMessageId = null;
        foreach (DictionaryEntry entry in handlerMap)
        {
            // 注册期构建的必须是强类型工厂委托，而非处理器 Type（旧实现存 Type，此断言即 A/B 判别点）
            var messageHandlerFactory = Assert.IsAssignableFrom<Func<IMessageHandler>>(entry.Value);
            var messageHandler = messageHandlerFactory();
            Assert.NotNull(messageHandler);

            probeMessageId ??= (int)entry.Key;
        }

        // 真实消息号走公开查询面：处理器有状态，同一消息号两次调用必须得到不同实例
        Assert.NotNull(probeMessageId);
        var firstHandler = module.GetMessageHandler(probeMessageId.Value);
        var secondHandler = module.GetMessageHandler(probeMessageId.Value);
        Assert.NotNull(firstHandler);
        Assert.NotNull(secondHandler);
        Assert.NotSame(firstHandler, secondHandler);

        // 未注册的消息号保持既有 null 语义
        Assert.Null(module.GetMessageHandler(int.MinValue));
    }

    /// <summary>
    /// 逐违例模式直接调用私有工厂构建入口，断言两类缺陷（未实现服务接口 / 缺公共无参构造）都抛注册期契约异常并带双类型定位。
    /// </summary>
    /// <param name="probeType">探针处理器类型。</param>
    private static async Task RunCreateFactoryRejectsProbeAsync(Type probeType)
    {
        await InitializeEnvironmentAsync();

        var createFactoryMethod = typeof(HotfixModule).GetMethod(CreateFactoryMethodName, BindingFlags.NonPublic | BindingFlags.Static)
                                  ?? throw new InvalidOperationException($"未找到 HotfixModule.{CreateFactoryMethodName}");
        var closedMethod = createFactoryMethod.MakeGenericMethod(typeof(IMessageHandler));
        var invocationException = Assert.Throws<TargetInvocationException>(() => closedMethod.Invoke(null, new object[] { probeType }));

        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(probeType.FullName, exception.Message);
        Assert.Contains(nameof(IMessageHandler), exception.Message);
    }

    /// <summary>
    /// 生产工厂方法名（反射按名定位，与生产方法名保持单一事实源）。
    /// The production factory method name (located via reflection, single source of truth with the production method name).
    /// </summary>
    private const string CreateFactoryMethodName = "CreateFactory";

    /// <summary>
    /// 初始化组件注册表与进程级设置（沿用装配护栏测试先例的缺省初始化）。
    /// </summary>
    private static async Task InitializeEnvironmentAsync()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        await ComponentRegister.InitAsync(typeof(PlayerComponent).Assembly);

        // 本地化键常量为编译期内联，测试进程可能始终未加载资源程序集；资源管理器的进程级单次
        // 程序集扫描会错过它，令消息回落键名而非格式化模板。显式注册资源提供者使命中确定化。
        LocalizationService.RegisterProvider(new AssemblyResourceProvider(typeof(GameFrameX.Localization.Keys.CoreExceptions.HotfixModule).Assembly));
    }

    /// <summary>
    /// 读取模块私有字典字段（沿装配护栏测试先例的反射访问方式）。
    /// </summary>
    private static IDictionary ReadPrivateDictionary(HotfixModule module, string fieldName)
    {
        var field = typeof(HotfixModule).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return Assert.IsAssignableFrom<IDictionary>(field.GetValue(module));
    }

    /// <summary>
    /// 探针消息类型一：携带消息号特性供映射注册读取。
    /// </summary>
    [MessageTypeHandler(FirstProbeMessageId)]
    public sealed class FirstFactoryProbeMessage
    {
    }

    /// <summary>
    /// 探针消息类型二。
    /// </summary>
    [MessageTypeHandler(SecondProbeMessageId)]
    public sealed class SecondFactoryProbeMessage
    {
    }

    /// <summary>
    /// 注册期校验探针一：带 [MessageMapping] 但不实现 IMessageHandler（旧实现推迟到首条消息才抛 WrongMessageHandlerType）。
    /// </summary>
    [MessageMapping(typeof(FirstFactoryProbeMessage))]
    internal sealed class NotMessageHandlerProbeHandler
    {
    }

    /// <summary>
    /// 注册期校验探针二：实现 IMessageHandler 但仅提供带参构造，故意不提供公共无参构造函数。
    /// </summary>
    [MessageMapping(typeof(SecondFactoryProbeMessage))]
    internal sealed class MissingParameterlessConstructorMessageHandler : IMessageHandler
    {
        /// <summary>
        /// 唯一带参构造：使本类型在注册期构造校验中失败（无公共无参构造函数）。
        /// </summary>
        /// <param name="constructorValue">占位构造参数（无实际用途）。</param>
        public MissingParameterlessConstructorMessageHandler(int constructorValue)
        {
        }

        /// <summary>
        /// 初始化探针：固定返回成功，不承载业务。
        /// Initializes the probe: always returns success, carries no business logic.
        /// </summary>
        /// <param name="message">消息对象 / Message object</param>
        /// <param name="networkChannel">网络渠道 / Network channel</param>
        /// <returns>固定返回 <c>true</c> / Always returns <c>true</c></returns>
        public Task<bool> InitAsync(INetworkMessage message, INetworkChannel networkChannel)
        {
            return Task.FromResult(true);
        }

        /// <summary>
        /// 内部执行探针：立即完成，不承载业务。
        /// Internal execution probe: completes immediately, carries no business logic.
        /// </summary>
        /// <param name="timeout">执行超时时间，单位毫秒 / Execution timeout in milliseconds</param>
        /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
        /// <returns>已完成的任务 / A completed task</returns>
        public Task InnerActionAsync(int timeout = 30000, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        /// <summary>
        /// 获取探针处理的消息类型。
        /// Gets the probe message type handled by this handler.
        /// </summary>
        /// <returns>探针消息类型 / The probe message type</returns>
        public Type GetMessageType()
        {
            return typeof(SecondFactoryProbeMessage);
        }

        /// <summary>
        /// 获取探针响应类型（与消息类型一致，无真实协议语义）。
        /// Gets the probe response type (identical to the message type, no real protocol semantics).
        /// </summary>
        /// <returns>探针消息类型 / The probe message type</returns>
        public Type GetResponseType()
        {
            return typeof(SecondFactoryProbeMessage);
        }
    }
}
