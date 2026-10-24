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
/// 启动能力报告条目（一个能力的装配结果：清单 + 判定 + 依赖状态 + 缺失功能 + Options 字段生效值）。
/// </summary>
public sealed class CapabilityReportEntry
{
    /// <summary>
    /// 初始化 <see cref="CapabilityReportEntry"/>。
    /// </summary>
    /// <param name="manifest">能力清单。</param>
    /// <param name="sourceAssemblyName">来源程序集名。</param>
    /// <param name="state">装配判定。</param>
    /// <param name="dependencies">依赖状态条目（无则为空集合）。</param>
    /// <param name="absentFeatures">可选依赖缺失的功能文案集合（无则为空集合）。</param>
    /// <param name="settingsFields">Options 字段生效值条目（无参数能力为空集合）。</param>
    /// <param name="settingsConfiguredButNotAssembled">配置段为本能力写了 Settings 但其未被装配（忽略并在报告标注）。</param>
    public CapabilityReportEntry(CapabilityManifest manifest, string sourceAssemblyName, CapabilityFilterState state, IReadOnlyList<CapabilityDependencyReport> dependencies = null, IReadOnlyList<string> absentFeatures = null, IReadOnlyList<CapabilitySettingsField> settingsFields = null, bool settingsConfiguredButNotAssembled = false)
    {
        Manifest = manifest;
        SourceAssemblyName = sourceAssemblyName;
        State = state;
        Dependencies = dependencies ?? Array.Empty<CapabilityDependencyReport>();
        AbsentFeatures = absentFeatures ?? Array.Empty<string>();
        SettingsFields = settingsFields ?? Array.Empty<CapabilitySettingsField>();
        SettingsConfiguredButNotAssembled = settingsConfiguredButNotAssembled;
    }

    /// <summary>
    /// 获取能力清单。
    /// </summary>
    public CapabilityManifest Manifest { get; }

    /// <summary>
    /// 获取来源程序集名。
    /// </summary>
    public string SourceAssemblyName { get; }

    /// <summary>
    /// 获取装配判定（启用才参与拓扑定序）。
    /// </summary>
    public CapabilityFilterState State { get; }

    /// <summary>
    /// 获取依赖状态条目（必选 + 可选；可选缺失含缺席原因）。
    /// </summary>
    public IReadOnlyList<CapabilityDependencyReport> Dependencies { get; }

    /// <summary>
    /// 获取可选依赖缺失的功能文案集合（本地化后的功能描述；运维不依赖日志也能查到少了什么）。
    /// </summary>
    public IReadOnlyList<string> AbsentFeatures { get; }

    /// <summary>
    /// 获取 Options 字段生效值条目（字段路径 + 生效值 + 来源）。
    /// </summary>
    public IReadOnlyList<CapabilitySettingsField> SettingsFields { get; }

    /// <summary>
    /// 获取是否「已配置但未装配」（配置段为本能力写了 Settings 但其被过滤器排除；该段被忽略并在此标注）。
    /// </summary>
    public bool SettingsConfiguredButNotAssembled { get; }
}

/// <summary>
/// 启动能力报告（装配完成后的全量能力快照：启用清单 / 装配顺序 / 禁用项 / 依赖状态 / Options 生效值；
/// 装配日志与 <c>list_capabilities</c> 共用同一份数据面，两边不会对不上）。
/// </summary>
public sealed class CapabilityReport
{
    /// <summary>
    /// 初始化 <see cref="CapabilityReport"/>。
    /// </summary>
    /// <param name="entries">全部能力条目（含禁用）。</param>
    /// <param name="enabledIdsInOrder">启用能力 Id（拓扑定序后）。</param>
    public CapabilityReport(IReadOnlyList<CapabilityReportEntry> entries, IReadOnlyList<string> enabledIdsInOrder)
    {
        Entries = entries;
        EnabledIdsInOrder = enabledIdsInOrder;
    }

    /// <summary>
    /// 获取全部能力条目（含禁用）。
    /// </summary>
    public IReadOnlyList<CapabilityReportEntry> Entries { get; }

    /// <summary>
    /// 获取启用能力 Id 集合（拓扑定序后的装配顺序）。
    /// </summary>
    public IReadOnlyList<string> EnabledIdsInOrder { get; }

    /// <summary>
    /// 查找指定 action 是否被某个已禁用能力声明（未安装判定的数据面）。
    /// </summary>
    /// <param name="action">action 名。</param>
    /// <returns>命中返回所属条目；否则 null。</returns>
    public CapabilityReportEntry FindDisabledOwnerOfAction(string action)
    {
        if (string.IsNullOrEmpty(action))
        {
            return null;
        }

        for (var i = 0; i < Entries.Count; i++)
        {
            var entry = Entries[i];
            if (entry.State == CapabilityFilterState.Enabled)
            {
                continue;
            }

            for (var j = 0; j < entry.Manifest.Actions.Count; j++)
            {
                if (string.Equals(entry.Manifest.Actions[j], action, StringComparison.Ordinal))
                {
                    return entry;
                }
            }
        }

        return null;
    }
}
