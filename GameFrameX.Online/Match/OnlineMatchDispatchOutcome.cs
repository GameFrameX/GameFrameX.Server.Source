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

namespace GameFrameX.Online.Match;

/// <summary>
/// 结果分发结果（vault:C6 S5.8「可信结果 → 资产 / 排行 / 通知」的资产侧回执）。
/// <para>
/// 维护约束（红线）：分发**逐玩家独立**——单个玩家的发奖失败不回滚其他玩家，
/// 也不改变已落定的结算事实。失败玩家留在 <see cref="FailedPlayerIds"/> 中由后续补偿流程重投
/// （重投仍然幂等，因为幂等键由 MatchResultId 派生）。
/// </para>
/// </summary>
public sealed class OnlineMatchDispatchOutcome
{
    /// <summary>
    /// 获取或设置结算结果标识（幂等边界）。
    /// </summary>
    public string MatchResultId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置成功分发的玩家数（含幂等重放）。
    /// </summary>
    public int SucceededCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被幂等重放的玩家数（奖励早已发放，本次未重复发放）。
    /// </summary>
    public int ReplayCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分发失败的玩家标识集合。
    /// </summary>
    public List<long> FailedPlayerIds
    {
        get;
        set;
    }
}
