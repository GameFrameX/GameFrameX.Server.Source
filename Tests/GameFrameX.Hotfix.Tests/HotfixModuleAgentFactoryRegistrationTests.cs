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
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Hotfix.Tests.Probes;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 热更注册期构造校验与代理实例工厂回归护栏。
/// </summary>
/// <remarks>
/// 组件代理的无参构造缺陷必须在热更程序集装载期报出（异常带实现类型与服务类型定位），
/// 而不是推迟到组件首次获取代理时才以反射异常爆出；代理实例经注册期构建的强类型工厂创建，
/// 启用包装且存在对应包装类型时工厂实现类型必须是包装类型，否则回落代理类型本身。
/// 探针类型集中于独立探针程序集，注册入口经反射直驱，不依赖类型枚举顺序（确定性）。
/// </remarks>
public sealed class HotfixModuleAgentFactoryRegistrationTests
{
    [Fact]
    public async Task HotfixModule_Construction_ThrowsFast_WhenRegisteredAgentLacksParameterlessConstructor()
    {
        await InitializeEnvironmentAsync();

        // 干净程序集模块（无任何代理/监听器/处理器类型）+ 反射直驱注册入口：
        // 注册探针代理后统一构建工厂，注册期即抛契约异常（顺序无关，确定性）
        var module = CreateCleanAssemblyModule();
        var probeType = typeof(HotfixRegistrationProbes.MissingParameterlessConstructorComponentAgent);
        Assert.True((bool)InvokeRegistration(module, "AddAgent", probeType));

        var invocationException = Assert.Throws<TargetInvocationException>(() => InvokeRegistration(module, "BuildAgentFactories"));

        // 异常必须携带实现类型与服务类型定位，装载日志可直接定位问题注册项
        var exception = Assert.IsType<InvalidOperationException>(invocationException.InnerException);
        Assert.Contains(probeType.FullName, exception.Message);
        Assert.Contains(nameof(GameFrameX.Core.Abstractions.Agent.IComponentAgent), exception.Message);
    }

    [Fact]
    public async Task HotfixModule_AgentFactoryMap_CoversRegistration_AndSelectsWrapperImplementation()
    {
        await InitializeEnvironmentAsync();

        var module = new HotfixModule(typeof(LoginComponentAgent).Assembly);
        var factoryMap = ReadPrivateDictionary(module, "_agentFactoryMap");
        var componentAgentMap = ReadPrivateDictionary(module, "_compAgentMap");
        var wrapperMap = ReadPrivateDictionary(module, "_agentAgentWrapperMap");

        Assert.NotEmpty(factoryMap);
        Assert.Equal(componentAgentMap.Count, factoryMap.Count);

        foreach (DictionaryEntry entry in factoryMap)
        {
            var componentType = Assert.IsAssignableFrom<Type>(entry.Key);
            var agentType = Assert.IsAssignableFrom<Type>(componentAgentMap[componentType]);
            var agentFactory = Assert.IsAssignableFrom<Func<GameFrameX.Core.Abstractions.Agent.IComponentAgent>>(entry.Value);
            var agentInstance = agentFactory();

            Assert.NotNull(agentInstance);
            Assert.IsAssignableFrom<GameFrameX.Core.Abstractions.Agent.IComponentAgent>(agentInstance);

            // 启用包装且存在对应包装类型时，注册期工厂必须已选定包装类型为实现类型；否则回落代理类型
            var expectedImplementationType = wrapperMap.Contains(agentType) ? Assert.IsAssignableFrom<Type>(wrapperMap[agentType]) : agentType;
            Assert.Same(expectedImplementationType, agentInstance.GetType());
        }
    }

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
