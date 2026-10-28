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
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Core.Hotfix.Agent;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Localization.Providers;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Utility;
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
/// </remarks>
public sealed class HotfixModuleAgentFactoryRegistrationTests
{
    [Fact]
    public async Task HotfixModule_Construction_ThrowsFast_WhenRegisteredAgentLacksParameterlessConstructor()
    {
        await InitializeEnvironmentAsync();

        var exception = Assert.Throws<InvalidOperationException>(() => new HotfixModule(typeof(MissingParameterlessConstructorComponentAgent).Assembly));

        // 异常必须携带实现类型与服务类型定位，装载日志可直接定位问题注册项
        Assert.Contains(typeof(MissingParameterlessConstructorComponentAgent).FullName, exception.Message);
        Assert.Contains(nameof(IComponentAgent), exception.Message);
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
            var agentFactory = Assert.IsAssignableFrom<Func<IComponentAgent>>(entry.Value);
            var agentInstance = agentFactory();

            Assert.NotNull(agentInstance);
            Assert.IsAssignableFrom<IComponentAgent>(agentInstance);

            // 启用包装且存在对应包装类型时，注册期工厂必须已选定包装类型为实现类型；否则回落代理类型
            var expectedImplementationType = wrapperMap.Contains(agentType) ? Assert.IsAssignableFrom<Type>(wrapperMap[agentType]) : agentType;
            Assert.Same(expectedImplementationType, agentInstance.GetType());
        }
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

        await ComponentRegister.InitAsync(typeof(GameFrameX.Apps.Player.Player.Component.PlayerComponent).Assembly);

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
    /// 注册期构造校验探针：组件代理仅提供带参构造，故意不提供公共无参构造函数。
    /// </summary>
    internal sealed class MissingParameterlessConstructorComponentAgent : BaseComponentAgent<PlayerComponent>
    {
        /// <summary>
        /// 唯一带参构造：使本类型在注册期构造校验中失败（无公共无参构造函数）。
        /// </summary>
        /// <param name="constructorValue">占位构造参数（无实际用途）。</param>
        public MissingParameterlessConstructorComponentAgent(int constructorValue)
        {
        }
    }
}
