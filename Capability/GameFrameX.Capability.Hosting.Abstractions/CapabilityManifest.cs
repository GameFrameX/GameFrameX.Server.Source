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
/// 能力静态清单（能力包对外的自描述：标识 / 版本 / 暴露的 admin action / 依赖能力）。
/// <para>
/// 维护约束：Id 全宿主唯一（重复即装配失败）；<see cref="Requires"/> 只允许引用其它能力的 Id，
/// 装配期按其做拓扑排序与闭包校验；<see cref="Actions"/> 是声明面，
/// 实际注册的 admin action 集合必须在组合期与之对账（声明未注册 = 启动失败）。
/// </para>
/// </summary>
public sealed class CapabilityManifest
{
    /// <summary>
    /// 初始化 <see cref="CapabilityManifest"/>。
    /// </summary>
    /// <param name="id">能力标识（非空；建议小写连字符命名，如 <c>online-assets</c>）。</param>
    /// <param name="version">能力版本（非空）。</param>
    /// <param name="actions">暴露的 admin action 名集合（无则为空集合）。</param>
    /// <param name="requires">依赖的其它能力 Id 集合（无则为空集合）。</param>
    public CapabilityManifest(string id, string version, IReadOnlyList<string> actions = null, IReadOnlyList<string> requires = null)
    {
        Id = id;
        Version = version;
        Actions = actions ?? Array.Empty<string>();
        Requires = requires ?? Array.Empty<string>();
    }

    /// <summary>
    /// 获取能力标识（全宿主唯一）。
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// 获取能力版本。
    /// </summary>
    public string Version { get; }

    /// <summary>
    /// 获取暴露的 admin action 名集合。
    /// </summary>
    public IReadOnlyList<string> Actions { get; }

    /// <summary>
    /// 获取依赖的其它能力 Id 集合。
    /// </summary>
    public IReadOnlyList<string> Requires { get; }
}
