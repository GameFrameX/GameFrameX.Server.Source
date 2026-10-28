// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他权利
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//  ==========================================================================================

using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Hotfix.Logic.Player.Login;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Wrapper 源码生成器装配回归护栏（C292）。
/// </summary>
/// <remarks>
/// Roslyn 引用版本高于宿主 SDK 编译器时生成器会被 CS9057 静默跳过（C291 期间的真实事故），
/// 代理方法随之失去 Actor 串行化包装。本护栏在装配层断言生成器产物真实生效，
/// 使该类静默回归在测试期即可暴露。
/// </remarks>
public sealed class AgentWrapperAssemblyGuardTests
{
    [Fact]
    public async Task HotfixModule_AgentWrapperMap_IsPopulatedFromMirroredWrapperNamespace()
    {
        if (GlobalSettings.CurrentSetting == null)
        {
            GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
        }

        await ComponentRegister.InitAsync(typeof(GameFrameX.Apps.Player.Player.Component.PlayerComponent).Assembly);
        var module = new HotfixModule(typeof(LoginComponentAgent).Assembly);
        var mapField = typeof(HotfixModule).GetField("_agentAgentWrapperMap", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        Assert.NotNull(mapField);
        var map = Assert.IsAssignableFrom<System.Collections.IDictionary>(mapField.GetValue(module));
        Assert.NotEmpty(map);

        var mirroredNamespaces = new HashSet<string>();
        var mappedAgentNamespaces = new HashSet<string>();
        foreach (System.Collections.DictionaryEntry entry in map)
        {
            var wrapperType = Assert.IsAssignableFrom<Type>(entry.Value);
            var wrapperFullName = wrapperType.FullName;
            Assert.NotNull(wrapperFullName);
            Assert.StartsWith("GameFrameX.Hotfix.Wrapper.Agent.", wrapperFullName);

            // 键必须是 wrapper 的直接基类（代理类型），防止错误登记
            var agentType = Assert.IsAssignableFrom<Type>(entry.Key);
            Assert.Equal(agentType, wrapperType.BaseType);
            mappedAgentNamespaces.Add(agentType.Namespace ?? string.Empty);

            // 镜像契约逐项校验：wrapper 命名空间段 = 代理源命名空间相对 GameFrameX.Hotfix. 的段（C292）
            var wrapperNamespace = wrapperFullName.Substring(0, wrapperFullName.LastIndexOf('.'));
            mirroredNamespaces.Add(wrapperNamespace);
            var agentNamespace = agentType.Namespace ?? string.Empty;
            var relativeAgentNamespace = agentNamespace.StartsWith("GameFrameX.Hotfix.", System.StringComparison.Ordinal)
                ? agentNamespace.Substring("GameFrameX.Hotfix.".Length)
                : agentNamespace;
            var relativeWrapperNamespace = wrapperNamespace.Substring("GameFrameX.Hotfix.Wrapper.Agent.".Length);
            Assert.Equal(relativeAgentNamespace, relativeWrapperNamespace);
        }

        // C357：空 wrapper 不再生成注册——无可包装方法的代理不得出现在 wrapper 映射中
        // （Player/ServerRole 的 LoginComponentAgent 与 ServerRole 的 MailComponentAgent 均无标记方法；
        // Player 的 MailComponentAgent 有标记方法，仍应注册——按命名空间区分同名代理）
        Assert.DoesNotContain(mappedAgentNamespaces, agentNamespace => agentNamespace.EndsWith(".Login"));
        Assert.DoesNotContain(mappedAgentNamespaces, agentNamespace => agentNamespace == "GameFrameX.Hotfix.Logic.ServerRole.Mail");
        // 有标记方法的代理仍正常产出镜像 wrapper
        Assert.Contains(mirroredNamespaces, namespaceName => namespaceName.EndsWith(".Player.Mail"));
    }
}
