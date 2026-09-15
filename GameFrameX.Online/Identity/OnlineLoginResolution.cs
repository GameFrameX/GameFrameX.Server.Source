//  ==========================================================================================
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

namespace GameFrameX.Online.Identity;

/// <summary>
/// 登录身份解析结果（vault:C3 S2.3：登录成功后由服务端生成账号与玩家上下文材料）。
/// <para>
/// 维护约束：本结果只承载身份域解析产物，不含会话与 Token——
/// 会话签发由 Session 域（<c>OnlineSessionManager</c>）基于本结果执行，客户端不得自行拼接身份关系。
/// </para>
/// </summary>
public sealed class OnlineLoginResolution
{
    /// <summary>
    /// 获取命中的外源身份。
    /// </summary>
    public OnlineIdentity Identity
    {
        get;
    }

    /// <summary>
    /// 获取归属游戏账号。
    /// </summary>
    public OnlineGameAccount GameAccount
    {
        get;
    }

    /// <summary>
    /// 获取本次登录选定的玩家档案（App/Server 归属明确）。
    /// </summary>
    public OnlinePlayerProfile Player
    {
        get;
    }

    /// <summary>
    /// 初始化 <see cref="OnlineLoginResolution"/>。
    /// </summary>
    /// <param name="identity">命中的外源身份。</param>
    /// <param name="gameAccount">归属游戏账号。</param>
    /// <param name="player">选定的玩家档案。</param>
    public OnlineLoginResolution(OnlineIdentity identity, OnlineGameAccount gameAccount, OnlinePlayerProfile player)
    {
        Identity = identity ?? throw new ArgumentNullException(nameof(identity));
        GameAccount = gameAccount ?? throw new ArgumentNullException(nameof(gameAccount));
        Player = player ?? throw new ArgumentNullException(nameof(player));
    }
}
