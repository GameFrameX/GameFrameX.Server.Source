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

namespace GameFrameX.Online.Social;

/// <summary>
/// 举报案件（vault:C7 S6.3：举报的完整证据链，Admin 侧处置的输入，VC-6.16 断言对象）。
/// <para>
/// 维护约束：证据字段（<see cref="MatchId"/> / <see cref="ChatMessageId"/> / <see cref="ChannelId"/> /
/// <see cref="Evidence"/>）是**照单全收的原始上下文**，服务层只校验、不改写、不解析——
/// Admin 需要靠这些标识回到原始场景取证，任何「顺手清洗」都会打断证据链。
/// 案件状态迁移一律经 <see cref="OnlineReportStateMachine"/>；
/// 处置结果与状态的正交约束见 <see cref="OnlineReportResolution"/>。
/// </para>
/// </summary>
public sealed class OnlineReportCase
{
    /// <summary>
    /// 获取或设置案件标识。
    /// </summary>
    public string ReportId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置举报人。
    /// </summary>
    public long ReporterId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被举报人。
    /// </summary>
    public long ReportedPlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置举报场景。
    /// </summary>
    public OnlineReportScene Scene
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置举报原因分类。
    /// </summary>
    public OnlineReportReason Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局标识（对局场景的证据；无则空字符串）。
    /// </summary>
    public string MatchId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被举报消息标识（聊天场景的证据；无则空字符串）。
    /// </summary>
    public string ChatMessageId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频道标识（聊天场景的证据；无则空字符串）。
    /// </summary>
    public string ChannelId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置补充说明（玩家自述；选择「其他」原因时必填）。
    /// </summary>
    public string Evidence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置案件状态。
    /// </summary>
    public OnlineReportState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置处置结果。
    /// </summary>
    public OnlineReportResolution Resolution
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置受理该案件的 Admin 标识（未受理为 <c>0</c>）。
    /// </summary>
    public long HandlerAdminId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 Admin 侧案件关联键（Admin 仓案件编号；联调轮 GFX-771 对齐，未关联为空字符串）。
    /// </summary>
    public string AdminCaseId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置提交时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最后变更时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置结案时刻（UTC 毫秒；未结案为 <c>0</c>）。
    /// </summary>
    public long ClosedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 构造深拷贝（存储层与调用方不得共享同一实例）。
    /// </summary>
    /// <returns>字段级拷贝。</returns>
    public OnlineReportCase Copy()
    {
        return new OnlineReportCase
        {
            ReportId = ReportId,
            TenantId = TenantId,
            AppId = AppId,
            ReporterId = ReporterId,
            ReportedPlayerId = ReportedPlayerId,
            Scene = Scene,
            Reason = Reason,
            MatchId = MatchId,
            ChatMessageId = ChatMessageId,
            ChannelId = ChannelId,
            Evidence = Evidence,
            State = State,
            Resolution = Resolution,
            HandlerAdminId = HandlerAdminId,
            AdminCaseId = AdminCaseId,
            CreatedAtTime = CreatedAtTime,
            UpdatedAtTime = UpdatedAtTime,
            ClosedAtTime = ClosedAtTime,
        };
    }
}
