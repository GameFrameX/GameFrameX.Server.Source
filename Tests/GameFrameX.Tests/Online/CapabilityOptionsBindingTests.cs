// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project,
//   本组织与贡献者概不承担任何责任。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository:     https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Docs:      https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 能力 Options 绑定机制测试（C294：三层合并「包内默认 &lt; 族配置段 Settings.&lt;能力Id&gt; &lt; 代码 Configure&lt;T&gt;」、
/// 报告逐字段来源、装配期 fail-fast（未发现能力 / 未知字段 / 类型不符 / 嵌套路径 / Validate / Configure 目标缺失）、
/// 被过滤能力的设置段忽略并标注）。
/// </summary>
[Collection(CapabilityRuntimeCollection.Name)]
public class CapabilityOptionsBindingTests
{
    /// <summary>参与绑定的槽位能力 Id（槽位一）。</summary>
    private const string BoundCapabilityId = "mech-first";

    /// <summary>被过滤槽位能力 Id（槽位二）。</summary>
    private const string FilteredCapabilityId = "mech-second";

    /// <summary>错误信息中的配置段路径前缀（族配置段名 + Settings + 能力 Id）。</summary>
    private const string ErrorPathPrefix = "MechanismTests.Settings.mech-first";

    /// <summary>槽位 Register 期捕获的合并校验后 Options 实例。</summary>
    private static CapabilityBindingTestOptions _capturedOptions;

    /// <summary>
    /// 解析族配置段 JSON（作为 <see cref="CapabilityCompositionOptions"/> 构造首参）。
    /// </summary>
    /// <param name="json">族配置段 JSON。</param>
    /// <returns>独立于文档生命周期的配置段节点。</returns>
    private static JsonElement FamilySection(string json)
    {
        using (var document = JsonDocument.Parse(json))
        {
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// 构造组合选项（族配置段 + 收集器日志 + 合法作用域 + 白名单）。
    /// </summary>
    /// <param name="familySectionJson">族配置段 JSON（null = 无配置段）。</param>
    /// <param name="enabledIds">参与组合的槽位能力 Id。</param>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateOptions(string familySectionJson, params string[] enabledIds)
    {
        var section = familySectionJson == null ? default : FamilySection(familySectionJson);
        return new CapabilityCompositionOptions(section, new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = 1, AppId = 1, ServerId = 1 },
            Filter = new CapabilityAssemblyFilter(enabledIds),
        };
    }

    /// <summary>
    /// 槽位一声明测试 Options 根类型，并在 Register 期把合并结果写入静态捕获位。
    /// </summary>
    private static void DeclareBoundOptionsOnFirstSlot()
    {
        MechanismScenario.First.OptionsType = typeof(CapabilityBindingTestOptions);
        MechanismScenario.First.Register = context => _capturedOptions = context.GetOptions<CapabilityBindingTestOptions>();
    }

    /// <summary>
    /// 组合机制测试族。
    /// </summary>
    /// <param name="options">组合选项。</param>
    /// <returns>组合产物。</returns>
    private static CapabilityRuntime Compose(CapabilityCompositionOptions options)
    {
        return CapabilityComposition.Compose(new MechanismTestFamily(), options);
    }

    /// <summary>
    /// 复位用例静态状态（槽位 + 捕获位）。
    /// </summary>
    private static void ResetScenario()
    {
        MechanismScenario.Reset();
        _capturedOptions = null;
    }

    /// <summary>
    /// 查找报告条目。
    /// </summary>
    /// <param name="runtime">组合产物。</param>
    /// <param name="capabilityId">能力 Id。</param>
    /// <returns>报告条目。</returns>
    private static CapabilityReportEntry FindEntry(CapabilityRuntime runtime, string capabilityId)
    {
        return runtime.Report.Entries.Single(entry => entry.Manifest.Id == capabilityId);
    }

    /// <summary>
    /// 三层合并优先级：未写字段保持包内默认（Alpha）；配置段覆盖默认（Beta）；代码 Configure 覆盖默认（Gamma 之外）
    /// 且在同一字段上胜过配置段（Gamma：配置段 25 → 代码 30）；嵌套段未写字段保持默认。
    /// </summary>
    [Fact]
    public async Task Compose_ThreeLayerPrecedence_DefaultThenSectionThenCode()
    {
        CapabilityRuntime runtime = null;
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-first\":{\"Beta\":20,\"Gamma\":25}}}", BoundCapabilityId);
            options.Configure<CapabilityBindingTestOptions>(bound => bound.Gamma = 30);

            runtime = Compose(options);

            Assert.NotNull(_capturedOptions);
            Assert.Equal(1, _capturedOptions.Alpha);
            Assert.Equal(20, _capturedOptions.Beta);
            Assert.Equal(30, _capturedOptions.Gamma);
            Assert.Equal(4, _capturedOptions.Nested.Depth);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            ResetScenario();
        }
    }

    /// <summary>
    /// 报告逐字段来源：未改写 = Default、配置段改写（含嵌套字段）= ConfigurationSection、代码改写 = Code；
    /// 生效值为合并校验后的最终值。
    /// </summary>
    [Fact]
    public async Task Compose_ReportSettingsFields_CarryPerFieldSource()
    {
        CapabilityRuntime runtime = null;
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-first\":{\"Beta\":20,\"Nested\":{\"Depth\":8}}}}", BoundCapabilityId);
            options.Configure<CapabilityBindingTestOptions>(bound => bound.Gamma = 30);

            runtime = Compose(options);

            var fields = FindEntry(runtime, BoundCapabilityId).SettingsFields.ToDictionary(field => field.FieldPath, StringComparer.Ordinal);
            Assert.Equal(4, fields.Count);

            Assert.Equal(CapabilitySettingsFieldSource.Default, fields["Alpha"].Source);
            Assert.Equal((object)1, fields["Alpha"].EffectiveValue);

            Assert.Equal(CapabilitySettingsFieldSource.ConfigurationSection, fields["Beta"].Source);
            Assert.Equal((object)20, fields["Beta"].EffectiveValue);

            Assert.Equal(CapabilitySettingsFieldSource.Code, fields["Gamma"].Source);
            Assert.Equal((object)30, fields["Gamma"].EffectiveValue);

            Assert.Equal(CapabilitySettingsFieldSource.ConfigurationSection, fields["Nested.Depth"].Source);
            Assert.Equal((object)8, fields["Nested.Depth"].EffectiveValue);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            ResetScenario();
        }
    }

    /// <summary>
    /// Settings 出现未发现的能力 Id：装配期 fail-fast，错误信息带该 Id（拼错能力名不会被静默忽略）。
    /// </summary>
    [Fact]
    public void Compose_SettingsForUndiscoveredCapability_FailsFast()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-undiscovered\":{\"Beta\":20}}}", BoundCapabilityId);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains("mech-undiscovered", exception.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// Settings 写了 Options 根类型不存在的字段：装配期 fail-fast，错误信息带完整配置段路径。
    /// </summary>
    [Fact]
    public void Compose_SettingsUnknownField_FailsFastWithFullPath()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-first\":{\"Delta\":1}}}", BoundCapabilityId);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains(ErrorPathPrefix + ".Delta", exception.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// Settings 字段类型不符（字符串写入 int 字段）：装配期 fail-fast，错误信息带完整路径与期望类型。
    /// </summary>
    [Fact]
    public void Compose_SettingsTypeMismatch_FailsFastWithFullPath()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-first\":{\"Alpha\":\"one\"}}}", BoundCapabilityId);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains(ErrorPathPrefix + ".Alpha", exception.Message);
            Assert.Contains(typeof(int).FullName, exception.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// 嵌套对象路径：嵌套段内未知字段与类型不符的错误信息都带「配置段前缀 + 父字段 + 子字段」完整路径。
    /// </summary>
    [Fact]
    public void Compose_SettingsNestedObjectErrors_CarryNestedPath()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var unknownNestedField = CreateOptions("{\"Settings\":{\"mech-first\":{\"Nested\":{\"Depht\":5}}}}", BoundCapabilityId);

            var unknownNestedFieldError = Assert.Throws<InvalidOperationException>(() => Compose(unknownNestedField));

            Assert.Contains(ErrorPathPrefix + ".Nested.Depht", unknownNestedFieldError.Message);

            var nestedTypeMismatch = CreateOptions("{\"Settings\":{\"mech-first\":{\"Nested\":{\"Depth\":\"deep\"}}}}", BoundCapabilityId);

            var nestedTypeMismatchError = Assert.Throws<InvalidOperationException>(() => Compose(nestedTypeMismatch));

            Assert.Contains(ErrorPathPrefix + ".Nested.Depth", nestedTypeMismatchError.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// Validate 不通过（RequirePositive）：校验的是合并后的最终值（配置段写 0 + 代码写负数），
    /// 装配期 fail-fast 且全部问题一次报全（带能力 Id 与字段路径）。
    /// </summary>
    [Fact]
    public void Compose_ValidateFailure_ReportsAllProblemsAtOnce()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-first\":{\"Beta\":0}}}", BoundCapabilityId);
            options.Configure<CapabilityBindingTestOptions>(bound => bound.Gamma = -5);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains(BoundCapabilityId, exception.Message);
            Assert.Contains("Beta: must be positive (actual: 0)", exception.Message);
            Assert.Contains("Gamma: must be positive (actual: -5)", exception.Message);
            Assert.DoesNotContain("Alpha:", exception.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// Configure&lt;T&gt; 指向没有任何已发现能力声明的 Options 类型：装配期 fail-fast，错误信息带类型全名。
    /// </summary>
    [Fact]
    public void Compose_ConfigureForUndeclaredOptionsType_FailsFast()
    {
        try
        {
            var options = CreateOptions(null, BoundCapabilityId);
            options.Configure<CapabilityBindingTestOptions>(bound => bound.Alpha = 10);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains(typeof(CapabilityBindingTestOptions).FullName, exception.Message);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// 被过滤能力的 Settings 与 Configure：设置段整体忽略（即便含未知字段也不校验）、Configure 回调不执行，
    /// 报告条目标注 SettingsConfiguredButNotAssembled 且无字段条目；启用能力不受影响且不标注。
    /// </summary>
    [Fact]
    public async Task Compose_SettingsForFilteredCapability_IgnoredAndMarked()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var configureInvoked = false;
            MechanismScenario.Second.OptionsType = typeof(CapabilityBindingTestOptions);
            MechanismScenario.Second.Register = context => _capturedOptions = context.GetOptions<CapabilityBindingTestOptions>();
            var options = CreateOptions("{\"Settings\":{\"mech-second\":{\"Beta\":99,\"NoSuchField\":1}}}", BoundCapabilityId);
            options.Configure<CapabilityBindingTestOptions>(bound => configureInvoked = true);

            runtime = Compose(options);

            Assert.False(configureInvoked);
            Assert.Null(_capturedOptions);

            var filteredEntry = FindEntry(runtime, FilteredCapabilityId);
            Assert.Equal(CapabilityFilterState.NotInEnableList, filteredEntry.State);
            Assert.True(filteredEntry.SettingsConfiguredButNotAssembled);
            Assert.Empty(filteredEntry.SettingsFields);

            var enabledEntry = FindEntry(runtime, BoundCapabilityId);
            Assert.Equal(CapabilityFilterState.Enabled, enabledEntry.State);
            Assert.False(enabledEntry.SettingsConfiguredButNotAssembled);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            ResetScenario();
        }
    }

    /// <summary>
    /// 嵌套 Options 子对象写 JSON null：按类型不符装配期 fail-fast（带完整路径与实际类型 null），
    /// 不再把子对象置空后让 Validate / 模块解引用到空引用。
    /// </summary>
    [Fact]
    public void Compose_SettingsNullForNestedObject_FailsFastWithTypeMismatch()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();
            var options = CreateOptions("{\"Settings\":{\"mech-first\":{\"Nested\":null}}}", BoundCapabilityId);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(options));

            Assert.Contains(ErrorPathPrefix + ".Nested", exception.Message);
            Assert.Contains(typeof(CapabilityBindingTestOptions.NestedSection).FullName, exception.Message);
            Assert.Contains("null", exception.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }

    /// <summary>
    /// 能力未声明 Options 根类型却写了带字段的配置段：装配期 fail-fast，错误信息带「前缀.首个字段名」；
    /// 空对象 {} 视为未配置，组合照常成功。
    /// </summary>
    [Fact]
    public async Task Compose_SettingsForCapabilityWithoutOptionsType_FailsFastUnlessEmpty()
    {
        CapabilityRuntime runtime = null;
        try
        {
            var withField = CreateOptions("{\"Settings\":{\"mech-first\":{\"SomeKey\":1}}}", BoundCapabilityId);

            var exception = Assert.Throws<InvalidOperationException>(() => Compose(withField));

            Assert.Contains(ErrorPathPrefix + ".SomeKey", exception.Message);

            runtime = Compose(CreateOptions("{\"Settings\":{\"mech-first\":{}}}", BoundCapabilityId));

            Assert.Empty(FindEntry(runtime, BoundCapabilityId).SettingsFields);
        }
        finally
        {
            if (runtime != null)
            {
                await runtime.StopAsync();
            }

            ResetScenario();
        }
    }

    /// <summary>
    /// 族配置段的 Settings 节点不是对象（null / 数组）：装配期按类型不符 fail-fast，错误信息带「族段名.Settings」路径，
    /// 不再抛出无路径的 JSON 元素类型异常。
    /// </summary>
    [Fact]
    public void Compose_SettingsNodeNotObject_FailsFastWithSectionPath()
    {
        try
        {
            DeclareBoundOptionsOnFirstSlot();

            var nullSettings = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("{\"Settings\":null}", BoundCapabilityId)));
            var arraySettings = Assert.Throws<InvalidOperationException>(() => Compose(CreateOptions("{\"Settings\":[]}", BoundCapabilityId)));

            Assert.Contains("MechanismTests.Settings", nullSettings.Message);
            Assert.Contains("Null", nullSettings.Message);
            Assert.Contains("MechanismTests.Settings", arraySettings.Message);
            Assert.Contains("Array", arraySettings.Message);
            Assert.Null(_capturedOptions);
        }
        finally
        {
            ResetScenario();
        }
    }
}
