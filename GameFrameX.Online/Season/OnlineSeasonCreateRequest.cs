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
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Season;

/// <summary>
/// 赛季创建请求（vault:C8 S7.3：赛季定义的服务端意图载体）。
/// <para>
/// 维护约束（红线）：作用域取自鉴权上下文（<see cref="OnlineScope"/>），客户端提交的同名字段不可覆盖；
/// 请求只承载「服务端运营意图」——**不存在客户端可直接提交赛季结算结果或奖励的通道**，
/// 奖励规则一经创建即固化，赛季期间的配置变更不回流本定义（结算与配置变更的冻结要求）。
/// </para>
/// </summary>
public sealed class OnlineSeasonCreateRequest
{
    /// <summary>
    /// 获取或设置赛季标识（App 内唯一；同作用域重名创建被拒绝，不覆盖既有赛季）。
    /// </summary>
    public string SeasonId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联榜单标识（必须已存在于同一作用域，创建时校验）。
    /// </summary>
    public string LeaderboardId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置赛季开始时刻（UTC 毫秒；排期元数据，必须早于 <see cref="EndTime"/>）。
    /// </summary>
    public long StartTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置赛季结束时刻（UTC 毫秒；排期元数据，必须晚于 <see cref="StartTime"/>）。
    /// </summary>
    public long EndTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置赛季奖励规则（名次区间 → 资产变更行；区间之间必须互不重叠）。
    /// </summary>
    public List<OnlineSeasonRewardRule> RewardRules
    {
        get;
        set;
    }
}
