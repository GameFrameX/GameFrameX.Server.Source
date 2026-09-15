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

using System.Collections.Generic;
using GameFrameX.Online.Assets;

namespace GameFrameX.Online.Match;

/// <summary>
/// 结算结果中的单玩家条目（vault:C6「结算生成唯一 MatchResultId，同一对局只能成功结算一次」）。
/// <para>
/// 维护约束（红线）：<see cref="Rewards"/> 是**服务端裁决**的奖励明细，
/// 客户端上报的任何奖励字段都不会进入本结构（VC-5.2）；
/// 奖励只描述「发什么」，实际发放由资产域统一入口按 <see cref="OnlineMatchResult.MatchResultId"/>
/// 幂等执行（结算与发奖解耦：结算失败不阻塞对局，发奖可重试）。
/// </para>
/// </summary>
public sealed class OnlineMatchResultEntry
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
    /// 获取或设置名次（从 1 开始；并列为同一名次）。
    /// </summary>
    public int Rank
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否为胜方。
    /// </summary>
    public bool IsWinner
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩法得分（玩法私有语义）。
    /// </summary>
    public long Score
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置奖励明细（资产域变更行；为空表示无奖励）。
    /// </summary>
    public List<OnlineAssetChangeLine> Rewards
    {
        get;
        set;
    }

    /// <summary>
    /// 复制条目（奖励行逐项重建）。
    /// </summary>
    /// <returns>条目副本。</returns>
    public OnlineMatchResultEntry Copy()
    {
        var rewards = new List<OnlineAssetChangeLine>();
        if (Rewards != null)
        {
            foreach (var line in Rewards)
            {
                if (line != null)
                {
                    rewards.Add(new OnlineAssetChangeLine(line.AssetKind, line.AssetId, line.Amount));
                }
            }
        }

        return new OnlineMatchResultEntry
        {
            PlayerId = PlayerId,
            Rank = Rank,
            IsWinner = IsWinner,
            Score = Score,
            Rewards = rewards,
        };
    }
}
