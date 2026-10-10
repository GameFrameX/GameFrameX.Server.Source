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

using System.Collections.Generic;
using GameFrameX.Apps.ServerRole.Gateway.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Gateway;

/// <summary>
/// 后端路由业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的路由规则：权重合法性、心跳存活判定、可用集按权重加权随机选择。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）；
/// 随机数由调用方注入（roll ∈ [0,1)），保证规则可确定性单测。
/// </remarks>
internal static class GatewayRules
{
    /// <summary>
    /// 心跳超时阈值（秒）：now - LastHeartbeatUnixTime 超过该值视为不可用。
    /// </summary>
    public const long HeartbeatTimeoutSeconds = 30;

    /// <summary>
    /// 判定路由权重是否合法：大于 0。
    /// </summary>
    /// <param name="weight">权重。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsWeightValid(int weight)
    {
        return weight > 0;
    }

    /// <summary>
    /// 判定路由目标在指定时刻是否存活：距最近心跳不超过超时阈值。
    /// </summary>
    /// <param name="entry">路由目标条目。</param>
    /// <param name="now">当前 Unix 秒（注入）。</param>
    /// <returns>存活返回 true。</returns>
    public static bool IsAlive(TargetEntryState entry, long now)
    {
        return now - entry.LastHeartbeatUnixTime <= HeartbeatTimeoutSeconds;
    }

    /// <summary>
    /// 从候选集中按权重加权随机选择一个路由目标：roll ∈ [0,1) 落在权重累积区间内即选中。
    /// </summary>
    /// <param name="candidates">候选条目（调用方已按角色类型过滤）。</param>
    /// <param name="now">当前 Unix 秒（注入）。</param>
    /// <param name="roll">[0,1) 均匀随机数（注入）。</param>
    /// <returns>选中的条目；无存活候选返回 null。</returns>
    public static TargetEntryState PickTarget(IEnumerable<TargetEntryState> candidates, long now, double roll)
    {
        TargetEntryState picked = null;
        var totalWeight = 0L;
        foreach (var candidate in candidates)
        {
            if (!IsAlive(candidate, now))
            {
                continue;
            }

            totalWeight += candidate.Weight;
        }

        if (totalWeight <= 0)
        {
            return null;
        }

        var cursor = roll * totalWeight;
        foreach (var candidate in candidates)
        {
            if (!IsAlive(candidate, now))
            {
                continue;
            }

            cursor -= candidate.Weight;
            if (cursor < 0)
            {
                picked = candidate;
                break;
            }
        }

        if (picked == null)
        {
            // 浮点舍入兜底：取最后一个存活候选。
            foreach (var candidate in candidates)
            {
                if (IsAlive(candidate, now))
                {
                    picked = candidate;
                }
            }
        }

        return picked;
    }
}
