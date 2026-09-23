// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

namespace GameFrameX.Online.Runtime;

/// <summary>
/// LiveOps 登记命令载荷（<see cref="OnlineLiveOpsRegistry.Publish"/>/<see cref="OnlineLiveOpsRegistry.Rollback"/>/
/// <see cref="OnlineLiveOpsRegistry.Sync"/> 的唯一提交形态；操作语义由 <see cref="OnlineLiveOpsOperation"/> 枚举承载）。
/// <para>
/// 维护约束：载荷原样登记（供查询端点回读），登记表 append-only——命令只追加不修改；
/// 六个字段历史上以相邻 string 摊排（位置互换零编译告警），收敛为命名字段后该风险消除。
/// <see cref="OnlineLiveOpsRegistry.Sync"/> 语义下 <see cref="Version"/> 不消费（实体 upsert 无版本概念）。
/// </para>
/// </summary>
public sealed class OnlineLiveOpsCommand
{
    /// <summary>
    /// 获取或设置配置/实体种类（如 <c>RemoteConfig</c> / <c>DeviceGroup</c> / <c>ScheduledTask</c> / <c>PlayerSegment</c> / <c>Announcement</c>）。
    /// </summary>
    /// <remarks>Gets or sets the kind (e.g. RemoteConfig / DeviceGroup / ScheduledTask / PlayerSegment / Announcement).</remarks>
    public string Kind { get; init; }

    /// <summary>
    /// 获取或设置种类内唯一键。
    /// </summary>
    /// <remarks>Gets or sets the kind-unique key.</remarks>
    public string Key { get; init; }

    /// <summary>
    /// 获取或设置版本号（Publish/Rollback 使用；Sync 不消费）。
    /// </summary>
    /// <remarks>Gets or sets the version (used by Publish/Rollback; not consumed by Sync).</remarks>
    public string Version { get; init; }

    /// <summary>
    /// 获取或设置载荷原文（原样登记，供查询端点回读）。
    /// </summary>
    /// <remarks>Gets or sets the raw payload text (registered as-is for read-back).</remarks>
    public string PayloadText { get; init; }

    /// <summary>
    /// 获取或设置操作者。
    /// </summary>
    /// <remarks>Gets or sets the operator id.</remarks>
    public string OperatorId { get; init; }

    /// <summary>
    /// 获取或设置关联标识。
    /// </summary>
    /// <remarks>Gets or sets the correlation id.</remarks>
    public string CorrelationId { get; init; }
}
