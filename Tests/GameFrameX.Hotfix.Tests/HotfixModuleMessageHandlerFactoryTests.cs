// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections;
using System.Reflection;
using GameFrameX.Apps.Player.Player.Component;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Hotfix.Tests.Probes;
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
/// 探针类型集中于独立探针程序集，注册入口经反射直驱，不依赖类型枚举顺序（确定性）。
/// </remarks>
public sealed class HotfixModuleMessageHandlerFactoryTests
{
    [Fact]
    public async Task HotfixModule_Construction_ThrowsFast_WhenMessageHandlerTypeViolatesInstantiationContract()
    {
        await InitializeEnvironmentAsync();

        // 干净程序集模块 + 反射直驱注册入口：探针处理器带 [MessageMapping] 但不实现 IMessageHandler，
        // 注册期即抛契约异常（语义等价整程序集装载 fail-fast，且顺序无关、确定性）
        var module = CreateCleanAssemblyModule();
        var probeType = typeof(HotfixRegistrationProbes.NotMessageHandlerProbeHandler);

        var invocationException = Assert.Throws<TargetInvocationException>(() => InvokeRegistration(module, "AddMessageHandler", probeType));

        // 异常必须携带服务类型定位，装载日志可直接定位问题注册项
        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(probeType.FullName, exception.Message);
        Assert.Contains(nameof(GameFrameX.Network.Abstractions.IMessageHandler), exception.Message);
    }

    [Fact]
    public async Task CreateFactory_RejectsMessageHandlerTypeNotImplementingIMessageHandler()
    {
        await RunCreateFactoryRejectsProbeAsync(typeof(HotfixRegistrationProbes.NotMessageHandlerProbeHandler));
    }

    [Fact]
    public async Task CreateFactory_RejectsMessageHandlerWithoutPublicParameterlessConstructor()
    {
        await RunCreateFactoryRejectsProbeAsync(typeof(HotfixRegistrationProbes.MissingParameterlessConstructorMessageHandler));
    }

    [Fact]
    public async Task HotfixModule_MessageHandlerMap_StoresFactories_AndGetMessageHandlerCreatesFreshInstances()
    {
        await InitializeEnvironmentAsync();

        var module = new HotfixModule(typeof(Hotfix.Logic.Player.Login.LoginComponentAgent).Assembly);
        var handlerMap = ReadPrivateDictionary(module, "_messageHandlerMap");
        Assert.NotEmpty(handlerMap);

        int? probeMessageId = null;
        foreach (DictionaryEntry entry in handlerMap)
        {
            // 注册期构建的必须是强类型工厂委托，而非处理器 Type（旧实现存 Type，此断言即 A/B 判别点）
            var messageHandlerFactory = Assert.IsAssignableFrom<Func<GameFrameX.Network.Abstractions.IMessageHandler>>(entry.Value);
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
        var closedMethod = createFactoryMethod.MakeGenericMethod(typeof(GameFrameX.Network.Abstractions.IMessageHandler));
        var invocationException = Assert.Throws<TargetInvocationException>(() => closedMethod.Invoke(null, new object[] { probeType }));

        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(probeType.FullName, exception.Message);
        Assert.Contains(nameof(GameFrameX.Network.Abstractions.IMessageHandler), exception.Message);
    }

    /// <summary>
    /// 生产工厂方法名（反射按名定位，与生产方法名保持单一事实源）。
    /// The production factory method name (located via reflection, single source of truth with the production method name).
    /// </summary>
    private const string CreateFactoryMethodName = "CreateFactory";

    /// <summary>
    /// 构造无任何注册类型的干净程序集模块（本地化程序集仅含资源键，无代理/监听器/处理器）。
    /// </summary>
    private static HotfixModule CreateCleanAssemblyModule()
    {
        return new HotfixModule(typeof(GameFrameX.Localization.Keys.CoreExceptions.HotfixModule).Assembly);
    }

    /// <summary>
    /// 反射直驱私有注册入口（探针类型不进入任何整集扫描面）。
    /// </summary>
    private static object InvokeRegistration(HotfixModule module, string methodName, params object[] arguments)
    {
        var method = typeof(HotfixModule).GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return method.Invoke(module, arguments);
    }

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
}
