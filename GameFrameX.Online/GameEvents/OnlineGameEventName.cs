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

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 标准游戏事件名（vault:C8 S7.5 冻结的 17 个事件名；**跨仓契约常量**，服务端 / 客户端 / 数据管道的共同标识）。
/// <para>
/// 维护约束（红线）：事件名是**跨系统契约**——拼写、大小写、分隔符一经发布即冻结，不得重命名、不得复用。
/// 本类只声明常量；分类、当前版本与必需字段由 <see cref="OnlineGameEventSchema"/> 登记表声明，
/// 二者必须一一声明（登记表缺项 = 登记表自检失败，由守护测试锁定）。
/// </para>
/// </summary>
public static class OnlineGameEventName
{
    /// <summary>安装（客户端首次启动；在玩家身份建立之前）。</summary>
    public const string Install = "Install";

    /// <summary>注册（账号创建成功）。</summary>
    public const string Register = "Register";

    /// <summary>登录（凭据校验通过、会话建立）。</summary>
    public const string Login = "Login";

    /// <summary>会话开始（连接就绪、进入可游戏状态）。</summary>
    public const string SessionStart = "SessionStart";

    /// <summary>会话结束（正常关闭 / 被踢 / 过期）。</summary>
    public const string SessionEnd = "SessionEnd";

    /// <summary>进入匹配队列。</summary>
    public const string MatchQueue = "MatchQueue";

    /// <summary>对局开始。</summary>
    public const string MatchStart = "MatchStart";

    /// <summary>对局结束（权威结果落档）。</summary>
    public const string MatchEnd = "MatchEnd";

    /// <summary>胜利（玩家维度）。</summary>
    public const string Win = "Win";

    /// <summary>失败（玩家维度）。</summary>
    public const string Lose = "Lose";

    /// <summary>中途退出 / 对局中断（玩家维度，无胜负归属）。</summary>
    public const string Abandon = "Abandon";

    /// <summary>奖励发放（资产到账）。</summary>
    public const string RewardGrant = "RewardGrant";

    /// <summary>货币消耗（资产扣减）。</summary>
    public const string CurrencySpend = "CurrencySpend";

    /// <summary>付费（支付确认对应的资产到账）。</summary>
    public const string Purchase = "Purchase";

    /// <summary>邮件打开（本 change 只登记、不投影——邮件域不在 Server 半边）。</summary>
    public const string MailOpen = "MailOpen";

    /// <summary>聊天举报受理。</summary>
    public const string ChatReport = "ChatReport";

    /// <summary>处罚生效（封禁 / 禁言等）。</summary>
    public const string Penalty = "Penalty";
}
