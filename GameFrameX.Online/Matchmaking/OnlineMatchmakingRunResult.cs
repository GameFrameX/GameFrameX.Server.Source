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

namespace GameFrameX.Online.Matchmaking;

/// <summary>
/// 单轮匹配执行结果（vault:C5 S4.6：协调器每轮的可核对产出）。
/// <para>
/// 维护约束：结果只描述**本轮实际发生的事**——未成组的排队票据不计入任何计数，
/// 它们仍在队列中等待后续轮次。对账口径：<c>MatchedTicketCount</c> 必须等于各
/// <c>AssignmentIds</c> 对应分配消费的票据数之和（VC-4.12）。
/// </para>
/// </summary>
public sealed class OnlineMatchmakingRunResult
{
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
    /// 获取或设置本轮开始时排队中的票据数。
    /// </summary>
    public int ScannedTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本轮扫描出的过期票据数。
    /// </summary>
    public int ExpiredTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本轮产出的分配标识集合。
    /// </summary>
    public List<string> AssignmentIds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本轮被消费（转 Matched）的票据数。
    /// </summary>
    public int MatchedTicketCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本轮进入对局的玩家数。
    /// </summary>
    public int MatchedPlayerCount
    {
        get;
        set;
    }
}
