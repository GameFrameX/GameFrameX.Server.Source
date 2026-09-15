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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
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

using System.Collections.Generic;

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计跨域检索条件（vault:C9 S8.2「跨域检索」：一次查询跨 支付/奖励/邮件/兑换码/远程配置/处罚/受控操作
/// 多域联查；租户/App 由 <c>OnlineScope</c> 锚定，其余维度全部为可选过滤）。
/// <para>
/// 维护约束：全部过滤条件为**合取**（AND）；<see cref="Domains"/> 为空表示不限域（全部七域），
/// 非空时行命中集合内任一域即保留（域间为析取——跨域联查语义）；时间窗为闭区间 [StartTime, EndTime]；
/// 过滤在全序对齐**之前**生效、游标筛选在全序**之上**生效（对齐 C101 时间线口径）。
/// </para>
/// </summary>
public sealed class OnlineAuditQuery
{
    /// <summary>
    /// 获取或设置审计域过滤集合（跨域联查：命中任一域即保留；空集合或 <see langword="null"/> 表示不限域）。
    /// </summary>
    public IReadOnlyList<string> Domains
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家标识过滤（0 或 <see langword="null"/> 表示不限；系统级审计 PlayerId=0 不会被玩家过滤命中）。
    /// </summary>
    public long? PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者标识过滤（空表示不限；按操作者定位其全部受控操作审计，VC-8.3）。
    /// </summary>
    public string OperatorId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件/操作类型过滤（空表示不限；大小写敏感原值匹配）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联链路键过滤（空表示不限；VC-8.5 按链路定位）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服过滤（0 或 <see langword="null"/> 表示不限——含 App 级审计 ServerId=0；
    /// 审计是跨区服检索域，区服默认不过滤，避免 App 级审计被割裂出跨域联查）。
    /// </summary>
    public long? ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置时间窗起点（UTC 毫秒，闭区间；<see langword="null"/> 表示不限）。
    /// </summary>
    public long? StartTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置时间窗终点（UTC 毫秒，闭区间；<see langword="null"/> 表示不限）。
    /// </summary>
    public long? EndTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分页游标（服务端编码的不透明令牌，只回传不构造；首页传空）。
    /// </summary>
    public string Cursor
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置页大小（0 使用默认值；超过上限被拒绝，防止消费方一次拉走全量审计）。
    /// </summary>
    public int PageSize
    {
        get;
        set;
    }
}
