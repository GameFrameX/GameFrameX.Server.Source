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
/// 玩家时间线事件行（字段集逐一对齐消费方 Admin C344 冻结契约 <c>OnlinePlayerTimelineEntryResponse</c>）。
/// <para>
/// 维护约束（红线）：
/// ① 消费方对行只做**原值透传**——不构造行、不推断分组、不补齐空位，故本类型增删字段等同跨仓契约变更；
/// ② <see cref="OccurredAt"/> 的单位是 **Unix 秒**（消费方契约单位），与 Online 各域内部毫秒时间戳不同，
/// 由 <see cref="OnlinePlayerTimelineService"/> 在投影时换算——拼装新腿时若直接落毫秒，消费方时间轴会整体偏移 1000 倍；
/// ③ 同一秒内的多行按 <see cref="EventId"/> 序数排序，因此 <see cref="EventId"/> 必须**跨腿全局唯一**
/// （各腿以来源域前缀保证），否则分页游标会漏项或重复。
/// </para>
/// </summary>
public sealed class OnlinePlayerTimelineEntry
{
    /// <summary>
    /// 获取或设置行标识（跨腿全局唯一，形如 <c>session:&lt;会话标识&gt;:&lt;状态&gt;</c>）。
    /// </summary>
    public string EventId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件类型（事件型行复用各域既有事件常量；记录型行取值见 <see cref="OnlinePlayerTimelineEventTypes"/>）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件分组（取值限于 <see cref="OnlinePlayerTimelineGroup"/> 六值，与消费方枚举同词表）。
    /// </summary>
    public string Group
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发生时刻（Unix 秒；0 表示来源记录时间戳缺失）。
    /// </summary>
    public long OccurredAt
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源域标识（事件型行取各域既有 Source 常量；记录型行见 <see cref="OnlinePlayerTimelineEventTypes"/>）。
    /// </summary>
    public string Source
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联标识（指向来源域原始记录，供按行定位到原始记录）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置载荷摘要（由非敏感字段拼装：标识 / 状态 / 金额 / 原因；不含令牌、指纹、设备标识等敏感值）。
    /// </summary>
    public string PayloadSummary
    {
        get;
        set;
    }
}
