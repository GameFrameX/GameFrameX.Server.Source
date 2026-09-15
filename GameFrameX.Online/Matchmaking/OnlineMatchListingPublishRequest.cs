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
/// 发布对局列表项请求（vault:C5 S4.4）。
/// <para>
/// 维护约束：请求只表达「想发布什么」，不携带作用域——作用域一律由鉴权上下文经
/// <c>OnlineScope</c> 注入，客户端字段不可覆盖（C93 作用域红线）。
/// </para>
/// </summary>
public sealed class OnlineMatchListingPublishRequest
{
    /// <summary>
    /// 获取或设置展示名称。
    /// </summary>
    public string Name
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源队伍标识（非队伍来源留空）。
    /// </summary>
    public string PartyId
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
    /// 获取或设置加入策略。
    /// </summary>
    public OnlineJoinPolicy JoinPolicy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置加入密码（仅 <see cref="OnlineJoinPolicy.Password"/> 必填）。
    /// </summary>
    public string Password
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置邀请白名单（仅 <see cref="OnlineJoinPolicy.InviteOnly"/> 使用）。
    /// <para>
    /// 第一版简化：白名单在发布时一次登记，不提供后续增删接口；需要动态邀请时再补独立的邀请签发契约。
    /// </para>
    /// </summary>
    public List<long> InvitedPlayerIds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置标签集合。
    /// </summary>
    public List<string> Tags
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置容量上限。
    /// </summary>
    public int Capacity
    {
        get;
        set;
    }
}
