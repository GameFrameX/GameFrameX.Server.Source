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

using System.Collections.Generic;

namespace GameFrameX.Online.Season;

/// <summary>
/// 赛季奖励结算回执（一次结算对该赛季全部命中玩家的逐玩家执行汇总）。
/// <para>
/// 维护约束（红线）：<see cref="GrantedCount"/> 只计**本次首次实际发放成功**的玩家，命中幂等回放的计入
/// <see cref="ReplayCount"/>——重试时两者之和守恒，即「无重复发放」的可观测判据（VC-7.6 账本半边）；
/// <see cref="FailedPlayers"/> 是逐玩家隔离结果，一个玩家发放失败不影响同赛季其余玩家到账；
/// <see cref="IsReplay"/> 表示本次是对已结算赛季的重复触发（返回首次结果，不重复发放）。
/// </para>
/// </summary>
public sealed class OnlineSeasonSettlementOutcome
{
    /// <summary>
    /// 获取或设置被结算的赛季标识。
    /// </summary>
    public string SeasonId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本次是否为重复触发（赛季已处于结算完成态；重试返回首次结果，不再发放）。
    /// </summary>
    public bool IsReplay
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本次首次发放成功的玩家数（实际产生新账本条目）。
    /// </summary>
    public int GrantedCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置本次命中幂等回放（此前已发放过）的玩家数。
    /// </summary>
    public int ReplayCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发放失败的玩家明细列表（非空表示本次结算未完成，赛季保持已结束态待重试）。
    /// </summary>
    public List<OnlineSeasonSettlementFailure> FailedPlayers
    {
        get;
        set;
    } = new List<OnlineSeasonSettlementFailure>();
}
