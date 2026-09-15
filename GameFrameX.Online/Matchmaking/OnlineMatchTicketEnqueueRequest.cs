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
/// 入队请求（vault:C5 S4.5：匹配条件，不含票据标识与时间戳）。
/// <para>
/// 维护约束：请求只表达「以什么条件入队」，票据标识、创建/过期时刻、状态一律由服务生成——
/// 调用方不可指定票据标识（避免客户端伪造/复用票据）。
/// </para>
/// </summary>
public sealed class OnlineMatchTicketEnqueueRequest
{
    /// <summary>
    /// 获取或设置来源队伍标识（单人排队留空）。
    /// </summary>
    public string PartyId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置票据携带的玩家集合（队伍票据必须为整队成员）。
    /// </summary>
    public List<long> PlayerIds
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
    /// 获取或设置技术水平区间。
    /// </summary>
    public OnlineMatchSkillRange SkillRange
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置目标对局规模（组内总人数）。
    /// </summary>
    public int TeamSize
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置延迟要求（毫秒；0 表示不限）。
    /// </summary>
    public int LatencyRequirement
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置自定义匹配属性。
    /// </summary>
    public Dictionary<string, string> CustomProperties
    {
        get;
        set;
    }
}
