// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

namespace GameFrameX.Apps.ServerRole.Battle.Entity;

/// <summary>
/// 战斗服务器（Battle Role）服务端作用域状态。
/// </summary>
/// <remarks>
/// 承载战斗记录与玩家积分：生命周期由 StateComponent 管理
/// （激活读取 / 消息完成回写 / 定时与停机兜底回存）。
/// </remarks>
public sealed class BattleState : BaseCacheState
{
    /// <summary>
    /// 全部战斗记录。Key: 战斗ID。
    /// </summary>
    public Dictionary<long, BattleRecordState> Battles { get; set; } = new Dictionary<long, BattleRecordState>();

    /// <summary>
    /// 下一个战斗ID（从 10001 递增）。
    /// </summary>
    public long NextBattleId { get; set; } = 10001;

    /// <summary>
    /// 玩家积分。Key: 玩家ID；Value: 当前积分（下限 0）。
    /// </summary>
    public Dictionary<long, long> PlayerScores { get; set; } = new Dictionary<long, long>();
}

/// <summary>
/// 单场战斗记录状态。
/// </summary>
public sealed class BattleRecordState
{
    /// <summary>
    /// 战斗ID。
    /// </summary>
    public long BattleId { get; set; }

    /// <summary>
    /// 攻方玩家ID。
    /// </summary>
    public long AtkPlayerId { get; set; }

    /// <summary>
    /// 守方玩家ID。
    /// </summary>
    public long DefPlayerId { get; set; }

    /// <summary>
    /// 攻方评分。
    /// </summary>
    public int AtkRating { get; set; }

    /// <summary>
    /// 守方评分。
    /// </summary>
    public int DefRating { get; set; }

    /// <summary>
    /// 赛制局数（1 / 3 / 5）。
    /// </summary>
    public int BestOf { get; set; }

    /// <summary>
    /// 回合结果列表。
    /// </summary>
    public List<BattleRoundState> Rounds { get; set; } = new List<BattleRoundState>();

    /// <summary>
    /// 获胜玩家ID。
    /// </summary>
    public long WinnerPlayerId { get; set; }

    /// <summary>
    /// 攻方积分变化（胜 +30 / 败 -20）。
    /// </summary>
    public int ScoreDelta { get; set; }
}

/// <summary>
/// 单回合战斗结果状态。
/// </summary>
public sealed class BattleRoundState
{
    /// <summary>
    /// 回合序号（从 1 递增）。
    /// </summary>
    public int Round { get; set; }

    /// <summary>
    /// 攻方战力。
    /// </summary>
    public double AtkPower { get; set; }

    /// <summary>
    /// 守方战力。
    /// </summary>
    public double DefPower { get; set; }

    /// <summary>
    /// 攻方是否获胜。
    /// </summary>
    public bool AtkWin { get; set; }
}
