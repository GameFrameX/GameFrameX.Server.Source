// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Capability.Hosting.Core;

/// <summary>
/// Options 绑定器（合并管线：机制 new 默认实例 → 配置段按字段覆盖 → 代码 Configure 回调 → Validate）。
/// <para>
/// 维护约束（fail-fast 红线）：整对象注入不存在——没写到的字段保持默认值而不是 0 / null；
/// 字段名大小写不敏感；未知字段或类型不符 = 装配失败且错误信息带完整路径（如
/// <c>Online.Settings.online-social.Friend.FriendLimt</c>）；绑定器自行遍历 JSON 节点，不依赖反序列化选项发现未知字段；
/// 每字段来源（默认 / 配置段 / 代码）由三层快照比较得出；<c>Validate</c> 的全部问题一次报全。
/// </para>
/// </summary>
internal static class CapabilityOptionsBinder
{
    /// <summary>
    /// 绑定一个能力的 Options（按 manifest 声明的根类型；无配置段与代码 Configure 时仍产出默认实例与来源表）。
    /// </summary>
    /// <param name="manifest">能力清单。</param>
    /// <param name="settingsElement">本能力的配置段节点（无则为 <c>default(JsonElement)</c>）。</param>
    /// <param name="configureEntries">代码层 Configure 回调表。</param>
    /// <param name="errorPathPrefix">错误信息路径前缀（如 <c>Online.Settings.online-social</c>）。</param>
    /// <returns>绑定结果（合并校验后的实例 + 每字段生效值与来源）。</returns>
    public static BindingResult Bind(CapabilityManifest manifest, JsonElement settingsElement, IReadOnlyList<CapabilityCompositionOptions.ConfigureEntry> configureEntries, string errorPathPrefix)
    {
        var optionsType = manifest.OptionsType;
        if (optionsType == null)
        {
            // 能力未声明 Options 根类型 = 该能力不接受任何配置；带字段的配置段必须 fail-fast（否则配置被静默吞掉）。
            EnsureNoStraySettings(settingsElement, errorPathPrefix);
            return new BindingResult(null, new List<CapabilitySettingsField>());
        }

        EnsureOptionsRootTypeInstantiable(optionsType);

        var instance = Activator.CreateInstance(optionsType);
        var configuredPaths = ApplySettingsSection(instance, settingsElement, errorPathPrefix);

        var beforeConfigure = SnapshotLeafValues(instance, string.Empty);
        InvokeConfigureEntries(instance, optionsType, configureEntries);
        var afterConfigure = SnapshotLeafValues(instance, string.Empty);
        var fields = BuildSettingsFields(beforeConfigure, afterConfigure, configuredPaths, errorPathPrefix);

        Validate(manifest, instance);
        return new BindingResult(instance, fields);
    }

    /// <summary>
    /// 校验无 Options 根类型能力的配置段（空对象 <c>{}</c> 或缺省视为未配置、允许；其余 = fail-fast）。
    /// </summary>
    /// <param name="settingsElement">本能力的配置段节点。</param>
    /// <param name="errorPathPrefix">错误信息路径前缀。</param>
    private static void EnsureNoStraySettings(JsonElement settingsElement, string errorPathPrefix)
    {
        if (settingsElement.ValueKind == JsonValueKind.Object)
        {
            foreach (var jsonProperty in settingsElement.EnumerateObject())
            {
                // Localization: Capability.Hosting.Assembly.SettingsUnknownField - Options 字段不存在或不可配置。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsUnknownField), errorPathPrefix + "." + jsonProperty.Name));
            }
        }
        else if (settingsElement.ValueKind != JsonValueKind.Undefined)
        {
            // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), errorPathPrefix, "object", DescribeJsonKind(settingsElement)));
        }
    }

    /// <summary>
    /// 守卫 Options 根类型可实例化（非抽象类 + 公共无参构造）。
    /// </summary>
    /// <param name="optionsType">Options 根类型。</param>
    private static void EnsureOptionsRootTypeInstantiable(Type optionsType)
    {
        if (optionsType.IsAbstract || optionsType.GetConstructor(Type.EmptyTypes) == null)
        {
            throw new InvalidOperationException("Capability options root type must be a non-abstract class with a public parameterless constructor: " + optionsType.FullName);
        }
    }

    /// <summary>
    /// 应用配置段到实例（对象节点按字段覆盖；非对象节点 = fail-fast）。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="settingsElement">配置段节点。</param>
    /// <param name="errorPathPrefix">错误信息路径前缀。</param>
    /// <returns>被配置段触碰的字段路径集合（带错误路径前缀的完整路径）。</returns>
    private static HashSet<string> ApplySettingsSection(object instance, JsonElement settingsElement, string errorPathPrefix)
    {
        var configuredPaths = new HashSet<string>(StringComparer.Ordinal);
        if (settingsElement.ValueKind == JsonValueKind.Object)
        {
            ApplySection(instance, settingsElement, errorPathPrefix, configuredPaths);
        }
        else if (settingsElement.ValueKind != JsonValueKind.Undefined)
        {
            // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), errorPathPrefix, "object", settingsElement.ValueKind.ToString()));
        }

        return configuredPaths;
    }

    /// <summary>
    /// 执行匹配 Options 根类型的代码层 Configure 回调（注册序）。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="optionsType">Options 根类型。</param>
    /// <param name="configureEntries">代码层 Configure 回调表。</param>
    private static void InvokeConfigureEntries(object instance, Type optionsType, IReadOnlyList<CapabilityCompositionOptions.ConfigureEntry> configureEntries)
    {
        for (var i = 0; i < configureEntries.Count; i++)
        {
            if (configureEntries[i].OptionsType == optionsType)
            {
                configureEntries[i].Configure(instance);
            }
        }
    }

    /// <summary>
    /// 构建每字段生效值与来源表。
    /// </summary>
    /// <param name="beforeConfigure">Configure 回调执行前的叶子字段快照。</param>
    /// <param name="afterConfigure">Configure 回调执行后的叶子字段快照。</param>
    /// <param name="configuredPaths">被配置段触碰的字段路径集合。</param>
    /// <param name="errorPathPrefix">错误信息路径前缀。</param>
    /// <returns>每字段生效值与来源。</returns>
    private static List<CapabilitySettingsField> BuildSettingsFields(Dictionary<string, object> beforeConfigure, Dictionary<string, object> afterConfigure, HashSet<string> configuredPaths, string errorPathPrefix)
    {
        var fields = new List<CapabilitySettingsField>(afterConfigure.Count);
        foreach (var pair in afterConfigure)
        {
            fields.Add(new CapabilitySettingsField(pair.Key, pair.Value, ResolveFieldSource(pair, beforeConfigure, configuredPaths, errorPathPrefix)));
        }

        return fields;
    }

    /// <summary>
    /// 判定单字段来源（代码 Configure 改写优先于配置段触碰，其次默认值）。
    /// </summary>
    /// <param name="pair">Configure 回调执行后的字段键值。</param>
    /// <param name="beforeConfigure">Configure 回调执行前的叶子字段快照。</param>
    /// <param name="configuredPaths">被配置段触碰的字段路径集合。</param>
    /// <param name="errorPathPrefix">错误信息路径前缀。</param>
    /// <returns>字段来源。</returns>
    private static CapabilitySettingsFieldSource ResolveFieldSource(KeyValuePair<string, object> pair, Dictionary<string, object> beforeConfigure, HashSet<string> configuredPaths, string errorPathPrefix)
    {
        var source = CapabilitySettingsFieldSource.Default;
        // configuredPaths 记录带错误路径前缀的完整路径（错误信息复用同一路径）；快照键为相对路径，比较前补齐前缀。
        if (configuredPaths.Contains(errorPathPrefix + "." + pair.Key))
        {
            source = CapabilitySettingsFieldSource.ConfigurationSection;
        }

        if (beforeConfigure.TryGetValue(pair.Key, out var beforeValue) && !ValuesEqual(beforeValue, pair.Value))
        {
            source = CapabilitySettingsFieldSource.Code;
        }

        return source;
    }

    /// <summary>
    /// 应用配置段（按字段覆盖；未知字段 / 类型不符 / 只读属性 = fail-fast，错误带完整路径）。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="element">配置段节点。</param>
    /// <param name="path">当前路径（错误信息用）。</param>
    /// <param name="configuredPaths">被配置段触碰的字段路径集合。</param>
    private static void ApplySection(object instance, JsonElement element, string path, HashSet<string> configuredPaths)
    {
        foreach (var jsonProperty in element.EnumerateObject())
        {
            var property = FindWritableProperty(instance.GetType(), jsonProperty.Name);
            if (property == null)
            {
                // Localization: Capability.Hosting.Assembly.SettingsUnknownField - Options 字段不存在或不可配置。
                throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsUnknownField), path + "." + jsonProperty.Name));
            }

            var propertyPath = path + "." + property.Name;
            ApplyValue(instance, property, jsonProperty.Value, propertyPath, configuredPaths);
        }
    }

    /// <summary>
    /// 应用单个字段值（对象递归；基元 / 数组按类型转换）。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="property">目标属性。</param>
    /// <param name="value">JSON 值。</param>
    /// <param name="propertyPath">字段完整路径。</param>
    /// <param name="configuredPaths">被配置段触碰的字段路径集合。</param>
    private static void ApplyValue(object instance, System.Reflection.PropertyInfo property, JsonElement value, string propertyPath, HashSet<string> configuredPaths)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            if (property.PropertyType.IsClass)
            {
                var target = property.GetValue(instance);
                if (target == null)
                {
                    if (property.PropertyType.GetConstructor(Type.EmptyTypes) == null)
                    {
                        // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
                        throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), propertyPath, property.PropertyType.FullName, "object"));
                    }

                    target = Activator.CreateInstance(property.PropertyType);
                    property.SetValue(instance, target);
                }

                ApplySection(target, value, propertyPath, configuredPaths);
                configuredPaths.Add(propertyPath);
                return;
            }

            // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), propertyPath, property.PropertyType.FullName, "object"));
        }

        if (value.ValueKind == JsonValueKind.Array)
        {
            ApplyArrayValue(instance, property, value, propertyPath, configuredPaths);
            return;
        }

        property.SetValue(instance, ConvertScalar(value, property.PropertyType, propertyPath));
        configuredPaths.Add(propertyPath);
    }

    /// <summary>
    /// 应用数组字段值（支持基元一维数组：string / int / long / bool / double）。
    /// </summary>
    /// <param name="instance">目标实例。</param>
    /// <param name="property">目标属性。</param>
    /// <param name="value">JSON 数组值。</param>
    /// <param name="propertyPath">字段完整路径。</param>
    /// <param name="configuredPaths">被配置段触碰的字段路径集合。</param>
    private static void ApplyArrayValue(object instance, System.Reflection.PropertyInfo property, JsonElement value, string propertyPath, HashSet<string> configuredPaths)
    {
        if (!property.PropertyType.IsArray || property.PropertyType.GetArrayRank() != 1)
        {
            // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), propertyPath, property.PropertyType.FullName, "array"));
        }

        var elementType = property.PropertyType.GetElementType();
        var items = new List<object>();
        var index = 0;
        foreach (var item in value.EnumerateArray())
        {
            items.Add(ConvertScalar(item, elementType, propertyPath + "[" + index + "]"));
            index++;
        }

        var array = Array.CreateInstance(elementType, items.Count);
        for (var i = 0; i < items.Count; i++)
        {
            array.SetValue(items[i], i);
        }

        property.SetValue(instance, array);
        configuredPaths.Add(propertyPath);
    }

    /// <summary>
    /// 标量转换（JSON 基元 → 属性类型；类型不符 fail-fast 带完整路径与期望 / 实际类型）。
    /// </summary>
    /// <param name="value">JSON 标量值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="propertyPath">字段完整路径。</param>
    /// <returns>转换结果。</returns>
    private static object ConvertScalar(JsonElement value, Type targetType, string propertyPath)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            // 只有 string 字段允许显式 null；嵌套 Options 子对象 / 数组写 null 会让 Validate 与模块解引用到空引用，按类型不符 fail-fast。
            if (targetType == typeof(string))
            {
                return null;
            }

            // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), propertyPath, targetType.FullName, "null"));
        }

        object converted;
        if (TryConvertScalar(value, targetType, out converted))
        {
            return converted;
        }

        // Localization: Capability.Hosting.Assembly.SettingsFieldTypeMismatch - Options 字段类型不符。
        throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblySettingsFieldTypeMismatch), propertyPath, targetType.FullName, DescribeJsonKind(value)));
    }

    /// <summary>
    /// 按目标类型转换非 null JSON 标量（string / bool 段；数值类型转交 <see cref="TryConvertNumericScalar"/>）。
    /// </summary>
    /// <param name="value">JSON 标量值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="converted">转换结果（返回 true 时有值）。</param>
    /// <returns>类型受支持且 JSON 种类匹配返回 true；否则 false（类型不符由调用方 fail-fast）。</returns>
    private static bool TryConvertScalar(JsonElement value, Type targetType, out object converted)
    {
        if (targetType == typeof(string))
        {
            if (value.ValueKind == JsonValueKind.String)
            {
                converted = value.GetString();
                return true;
            }
        }
        else if (targetType == typeof(bool))
        {
            if (value.ValueKind == JsonValueKind.True)
            {
                converted = true;
                return true;
            }

            if (value.ValueKind == JsonValueKind.False)
            {
                converted = false;
                return true;
            }
        }
        else
        {
            return TryConvertNumericScalar(value, targetType, out converted);
        }

        converted = null;
        return false;
    }

    /// <summary>
    /// 按目标类型转换非 null 数值 JSON 标量（int / long / double 段）。
    /// </summary>
    /// <param name="value">JSON 标量值。</param>
    /// <param name="targetType">目标类型。</param>
    /// <param name="converted">转换结果（返回 true 时有值）。</param>
    /// <returns>类型受支持且 JSON 种类匹配返回 true；否则 false（类型不符由调用方 fail-fast）。</returns>
    private static bool TryConvertNumericScalar(JsonElement value, Type targetType, out object converted)
    {
        if (targetType == typeof(int))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var integerValue))
            {
                converted = integerValue;
                return true;
            }
        }
        else if (targetType == typeof(long))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var longValue))
            {
                converted = longValue;
                return true;
            }
        }
        else if (targetType == typeof(double))
        {
            if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var doubleValue))
            {
                converted = doubleValue;
                return true;
            }
        }

        converted = null;
        return false;
    }

    /// <summary>
    /// 校验合并后的最终值（实现 <see cref="ICapabilityOptions"/> 时调用；全部问题一次报全）。
    /// </summary>
    /// <param name="manifest">能力清单。</param>
    /// <param name="instance">合并后的实例。</param>
    private static void Validate(CapabilityManifest manifest, object instance)
    {
        if (!(instance is ICapabilityOptions options))
        {
            return;
        }

        var validation = new CapabilityOptionsValidation();
        options.Validate(validation);
        if (!validation.IsValid)
        {
            // Localization: Capability.Hosting.Assembly.OptionsValidationFailed - Options 校验失败。
            throw new InvalidOperationException(string.Format(LocalizationService.GetString(Localization.Keys.Capability.Hosting.Assembly.AssemblyOptionsValidationFailed), manifest.Id, string.Join("; ", validation.Problems)));
        }
    }

    /// <summary>
    /// 快照全部叶子字段值（字段路径 → 当前值；类字段递归、数组与基元为叶子）。
    /// </summary>
    /// <param name="instance">实例。</param>
    /// <param name="pathPrefix">路径前缀（根为空串）。</param>
    /// <returns>叶子字段值快照。</returns>
    private static Dictionary<string, object> SnapshotLeafValues(object instance, string pathPrefix)
    {
        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        CollectLeafValues(instance, pathPrefix, values);
        return values;
    }

    /// <summary>
    /// 递归收集叶子字段值。
    /// </summary>
    /// <param name="instance">实例。</param>
    /// <param name="pathPrefix">路径前缀。</param>
    /// <param name="values">收集目标。</param>
    private static void CollectLeafValues(object instance, string pathPrefix, Dictionary<string, object> values)
    {
        if (instance == null)
        {
            return;
        }

        var properties = instance.GetType().GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        for (var i = 0; i < properties.Length; i++)
        {
            var property = properties[i];
            if (!property.CanRead || property.GetIndexParameters().Length > 0)
            {
                continue;
            }

            var propertyPath = pathPrefix.Length == 0 ? property.Name : pathPrefix + "." + property.Name;
            var propertyValue = property.GetValue(instance);
            // 数组同为引用类型，但按叶子快照（与 ApplyArrayValue 的整字段覆盖、ValuesEqual 的逐元素比较对齐）。
            if (propertyValue != null && property.PropertyType.IsClass && property.PropertyType != typeof(string) && !property.PropertyType.IsArray)
            {
                CollectLeafValues(propertyValue, propertyPath, values);
                continue;
            }

            values[propertyPath] = propertyValue;
        }
    }

    /// <summary>
    /// 查找可写属性（字段名大小写不敏感；只读属性视为不可配置）。
    /// </summary>
    /// <param name="type">目标类型。</param>
    /// <param name="name">配置段字段名。</param>
    /// <returns>命中属性；未命中或不可写为 null。</returns>
    private static System.Reflection.PropertyInfo FindWritableProperty(Type type, string name)
    {
        var properties = type.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
        for (var i = 0; i < properties.Length; i++)
        {
            if (!string.Equals(properties[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (!properties[i].CanWrite || properties[i].GetIndexParameters().Length > 0)
            {
                return null;
            }

            return properties[i];
        }

        return null;
    }

    /// <summary>
    /// 值相等比较（基元 / 字符串 / 数组）。
    /// </summary>
    /// <param name="left">前值。</param>
    /// <param name="right">后值。</param>
    /// <returns>相等返回 true。</returns>
    private static bool ValuesEqual(object left, object right)
    {
        if (left is Array leftArray && right is Array rightArray)
        {
            if (leftArray.Length != rightArray.Length)
            {
                return false;
            }

            for (var i = 0; i < leftArray.Length; i++)
            {
                if (!Equals(leftArray.GetValue(i), rightArray.GetValue(i)))
                {
                    return false;
                }
            }

            return true;
        }

        return Equals(left, right);
    }

    /// <summary>
    /// 描述 JSON 值种类（错误信息用）。
    /// </summary>
    /// <param name="value">JSON 值。</param>
    /// <returns>种类描述。</returns>
    private static string DescribeJsonKind(JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.String:
                return "string";
            case JsonValueKind.Number:
                return "number";
            case JsonValueKind.True:
                return "true";
            case JsonValueKind.False:
                return "false";
            default:
                return value.ValueKind.ToString().ToLowerInvariant();
        }
    }

    /// <summary>
    /// 绑定结果（实例 + 每字段生效值与来源）。
    /// </summary>
    internal sealed class BindingResult
    {
        /// <summary>
        /// 初始化 <see cref="BindingResult"/>。
        /// </summary>
        /// <param name="instance">合并校验后的实例（无参数能力为 null）。</param>
        /// <param name="fields">每字段生效值与来源。</param>
        public BindingResult(object instance, List<CapabilitySettingsField> fields)
        {
            Instance = instance;
            Fields = fields;
        }

        /// <summary>
        /// 获取合并校验后的实例。
        /// </summary>
        public object Instance { get; }

        /// <summary>
        /// 获取每字段生效值与来源。
        /// </summary>
        public List<CapabilitySettingsField> Fields { get; }
    }
}
