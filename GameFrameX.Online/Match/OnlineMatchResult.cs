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
/// 对局结算结果（vault:C6「结算生成唯一 MatchResultId，同一对局只能成功结算一次」）。
/// <para>
/// 维护约束（红线）：<see cref="MatchResultId"/> 是**幂等的唯一边界**——
/// 资产发放以它同时作为 <c>BusinessOrderId</c> 与 <c>IdempotencyKey</c>，
/// 因此「以同一 MatchResultId 重试结算 3 次只发一次奖」（VC-5.8）由既有资产域幂等直接保证，
/// 本模块不再自建第二套去重。结果一经落定不可改写（重试返回首次结果）。
/// </para>
/// <para>
/// 结算事实与发奖解耦：本类型只陈述「谁赢、发什么」，是否已发放由资产交易状态决定，
/// 发奖失败不回滚本结果（可重试，不阻塞对局结束）。
/// </para>
/// </summary>
public sealed class OnlineMatchResult
{
    /// <summary>
    /// 获取或设置结果标识（全局唯一，幂等边界）。
    /// </summary>
    public string MatchResultId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId
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
    /// 获取或设置区服标识（作用域隔离键）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩法模式。
    /// </summary>
    public int Mode
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区域。
    /// </summary>
    public int Region
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置结束方式（正常完成 / 超时 / 中止等，对应结束态的原因）。
    /// </summary>
    public OnlineMatchState Outcome
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置各玩家条目（按名次升序）。
    /// </summary>
    public List<OnlineMatchResultEntry> Entries
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置结算时刻（UTC 毫秒）。
    /// </summary>
    public long SettledTime
    {
        get;
        set;
    }

    /// <summary>
    /// 复制结果（条目逐项深拷贝）。
    /// </summary>
    /// <returns>结果副本。</returns>
    public OnlineMatchResult Copy()
    {
        var entries = new List<OnlineMatchResultEntry>();
        if (Entries != null)
        {
            foreach (var entry in Entries)
            {
                entries.Add(entry == null ? null : entry.Copy());
            }
        }

        return new OnlineMatchResult
        {
            MatchResultId = MatchResultId,
            MatchId = MatchId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            Mode = Mode,
            Region = Region,
            Outcome = Outcome,
            Entries = entries,
            SettledTime = SettledTime,
        };
    }
}
