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
/// 加入对局列表项请求（vault:C5 S4.4：按 JoinPolicy 分别裁决）。
/// <para>
/// 维护约束：<see cref="PlayerId"/> 即请求方主体位（由鉴权上下文注入，客户端不可覆盖）；
/// <see cref="MemberPlayerIds"/> 是随行成员名单，整队加入时由调用方带全——
/// 服务只按名单校验策略与容量，不反查名单真实性（队伍真实性由 Party 域的事实源保证）。
/// </para>
/// </summary>
public sealed class OnlineMatchListingJoinRequest
{
    /// <summary>
    /// 获取或设置请求方玩家标识（必须与作用域主体位一致）。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置随行成员标识集合（不含请求方也可；服务会并入去重）。
    /// </summary>
    public List<long> MemberPlayerIds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置加入密码（仅 <see cref="OnlineJoinPolicy.Password"/> 使用）。
    /// </summary>
    public string Password
    {
        get;
        set;
    }
}
