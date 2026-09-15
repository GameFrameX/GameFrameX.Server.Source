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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事报名登记（vault:C8 S7.4「报名」的事实记录）。
/// <para>
/// 维护约束（红线）：
/// (1) 唯一键 = (TournamentId, PlayerId)——同一玩家在同一赛事内只有一条登记，重复报名返回既有登记而非新增；
/// (2) 登记时**冻结判定输入**（<see cref="RankAtRegistration"/> / <see cref="ScoreAtRegistration"/>）：
/// 资格判定发生在报名时刻，事后榜单变化不得让登记的合法性失去依据（可解释性，VC-7.8 的审计半边）；
/// (3) 登记不代表成绩——成绩只存在于冻结的 <see cref="OnlineTournamentStandings"/> 中，本类型不承载分数排名语义。
/// </para>
/// </summary>
public sealed class OnlineTournamentRegistration
{
    /// <summary>
    /// 获取或设置赛事标识（归属键）。
    /// </summary>
    public string TournamentId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（作用域隔离键）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（作用域隔离键）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置报名玩家标识。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置报名时刻（UTC 毫秒）。
    /// </summary>
    public long RegisteredTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置报名时刻在关联榜单上的名次（无门槛赛事或未取榜时为 0；资格判定输入，事后冻结）。
    /// </summary>
    public int RankAtRegistration
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置报名时刻在关联榜单上的分数（无门槛赛事或未取榜时为 0；资格判定输入，事后冻结）。
    /// </summary>
    public long ScoreAtRegistration
    {
        get;
        set;
    }

    /// <summary>
    /// 复制报名登记（存储出入参防御性拷贝）。
    /// </summary>
    /// <returns>登记副本。</returns>
    public OnlineTournamentRegistration Copy()
    {
        return new OnlineTournamentRegistration
        {
            TournamentId = TournamentId,
            TenantId = TenantId,
            AppId = AppId,
            PlayerId = PlayerId,
            RegisteredTime = RegisteredTime,
            RankAtRegistration = RankAtRegistration,
            ScoreAtRegistration = ScoreAtRegistration,
        };
    }
}
