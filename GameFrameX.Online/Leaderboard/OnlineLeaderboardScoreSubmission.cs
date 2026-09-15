// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜分数提交（单玩家单笔；只由可信写入链路构造，不暴露给客户端）。
/// <para>
/// 维护约束（红线）：本类型是存储临界区的唯一写入载体——分数与来源由投影器从结算结果逐字段抄录，
/// 调用链上任何一层都不接受外部传入的「裸分数」；<see cref="IncomingScore"/> 仍须过防刷上限校验
/// （可信链路也可能被上游缺陷 / 被攻陷结点灌入异常分数，防线不能省，VC-7.4）。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardScoreSubmission
{
    /// <summary>
    /// 获取或设置玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本笔提交分数（进入累计策略裁决前的原始值）。
    /// </summary>
    public long IncomingScore
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分数来源。
    /// </summary>
    public OnlineLeaderboardScoreSource SourceKind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源结算结果标识（追溯键）。
    /// </summary>
    public string SourceMatchResultId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置提交时刻（UTC 毫秒；写入条目的 <c>LastUpdateTime</c>）。
    /// </summary>
    public long SubmittedTime
    {
        get;
        set;
    }
}
