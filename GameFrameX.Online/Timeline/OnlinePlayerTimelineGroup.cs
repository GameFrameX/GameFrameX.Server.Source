//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//  ==========================================================================================

namespace GameFrameX.Online.Timeline;

/// <summary>
/// 玩家时间线分组词表（六值；与消费方 Admin C344 <c>OnlinePlayerTimelineEventGroup</c> 同词表）。
/// <para>
/// 维护约束（红线）：分组值是**线上原值**，消费方 Admin 只做透传——请求侧把枚举 <c>ToString()</c> 后作为过滤参数传入，
/// 响应侧把本值原样回显给前端。因此本词表必须是字符串常量而不是枚举：换成枚举即引入一层映射，
/// 映射一旦漂移，Admin 的分组过滤会静默命中为空列表（无报错、无日志），是难以发现的一致性缺陷。
/// </para>
/// <para>
/// 六值固定：新增或重命名分组属**跨仓契约变更**，须先改 vault:C9 契约与 Admin 侧枚举，再改本词表；
/// 本仓不得单方面扩展（扩展值在消费方会被当作未知分组兜底展示）。
/// </para>
/// </summary>
public static class OnlinePlayerTimelineGroup
{
    /// <summary>
    /// 会话与身份分组（身份实体、绑定关系、玩家侧会话）。
    /// </summary>
    public const string Session = "Session";

    /// <summary>
    /// 对局分组（该玩家作为成员的对局）。
    /// </summary>
    public const string Match = "Match";

    /// <summary>
    /// 资产分组（玩家资产流水）。
    /// </summary>
    public const string Asset = "Asset";

    /// <summary>
    /// 社交分组（好友/群组/聊天等关系型事件；vault:C9 保留分组，本仓当前不落腿，命中该过滤即空列表）。
    /// </summary>
    public const string Social = "Social";

    /// <summary>
    /// 处罚分组（该玩家历史上全部处罚及其撤销）。
    /// </summary>
    public const string Penalty = "Penalty";

    /// <summary>
    /// 运营配置分组（配置命中记录；数据源为可空探针，未装配时为空槽）。
    /// </summary>
    public const string LiveOps = "LiveOps";
}
