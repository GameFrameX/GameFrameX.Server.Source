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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 能力装配过滤器（配置排除表与能力清单段的机制形态；
/// 组合根从配置解析出实例后注入装配器）。
/// <para>
/// 维护约束：<see cref="EnabledIds"/> 缺省（null）= 装配全部已发现能力；
/// <see cref="DisabledIds"/> 恒为排除语义且优先于清单段；过滤器引用未发现的能力 Id 时装配 fail-fast。
/// 构造期规整（Trim、丢弃空白条目）：规整后为空的清单段视为 null、排除表视为空——保证「空过滤器 = 全装配」红线
/// 在机制层成立，配置写成 <c>"Capabilities": []</c> 或含空白条目时不会静默装配零能力，也不会把空白条目误报为未发现能力。
/// </para>
/// </summary>
public sealed class CapabilityAssemblyFilter
{
    /// <summary>
    /// 初始化 <see cref="CapabilityAssemblyFilter"/>。
    /// </summary>
    /// <param name="enabledIds">能力清单段（null、空或仅含空白条目 = 全部）。</param>
    /// <param name="disabledIds">配置排除表（null、空或仅含空白条目 = 无排除）。</param>
    public CapabilityAssemblyFilter(IReadOnlyList<string> enabledIds = null, IReadOnlyList<string> disabledIds = null)
    {
        EnabledIds = NormalizeIds(enabledIds);
        DisabledIds = NormalizeIds(disabledIds) ?? Array.Empty<string>();
    }

    /// <summary>
    /// 规整能力 Id 列表（Trim、丢弃空白条目）；无有效条目时返回 null（该维度不过滤）。
    /// </summary>
    /// <param name="source">原始列表（可为 null）。</param>
    /// <returns>规整后的数组；无有效条目时为 null。</returns>
    private static string[] NormalizeIds(IReadOnlyList<string> source)
    {
        if (source == null)
        {
            return null;
        }

        var ids = source.Where(id => !string.IsNullOrWhiteSpace(id)).Select(id => id.Trim()).ToArray();
        return ids.Length == 0 ? null : ids;
    }

    /// <summary>
    /// 获取能力清单段（null = 装配全部已发现能力）。
    /// </summary>
    public IReadOnlyList<string> EnabledIds { get; }

    /// <summary>
    /// 获取配置排除表。
    /// </summary>
    public IReadOnlyList<string> DisabledIds { get; }

    /// <summary>
    /// 判定能力的装配去向。
    /// </summary>
    /// <param name="manifest">能力清单。</param>
    /// <returns>装配判定（启用 / 清单段未收录 / 排除表命中）。</returns>
    public CapabilityFilterState Match(CapabilityManifest manifest)
    {
        if (manifest == null)
        {
            throw new ArgumentNullException(nameof(manifest));
        }

        if (DisabledIds.Contains(manifest.Id, StringComparer.Ordinal))
        {
            return CapabilityFilterState.ExcludedByDisableList;
        }

        if (EnabledIds != null && !EnabledIds.Contains(manifest.Id, StringComparer.Ordinal))
        {
            return CapabilityFilterState.NotInEnableList;
        }

        return CapabilityFilterState.Enabled;
    }
}

/// <summary>
/// 能力装配判定结果。
/// </summary>
public enum CapabilityFilterState
{
    /// <summary>
    /// 启用装配。
    /// </summary>
    Enabled = 0,

    /// <summary>
    /// 能力清单段未收录（不装配）。
    /// </summary>
    NotInEnableList = 1,

    /// <summary>
    /// 配置排除表命中（不装配）。
    /// </summary>
    ExcludedByDisableList = 2,

    /// <summary>
    /// 未装配（能力目录条目：本进程未装配该能力）。
    /// </summary>
    NotAssembled = 3,
}
