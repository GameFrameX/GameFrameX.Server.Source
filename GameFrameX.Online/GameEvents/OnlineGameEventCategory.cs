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
/// 标准游戏事件分类（vault:C8 S7.5 六类接入口径：登录 / 匹配 / 对局 / 奖励 / 付费 / 举报）。
/// <para>
/// 维护约束（红线）：分类是**事件名的静态归属**，由 <see cref="OnlineGameEventSchema"/> 登记表唯一确定；
/// 分类在运行期不可由事件投递方指定（否则同一事件名可被投成不同分类，指标与告警口径随之漂移）。
/// 分类枚举值**只增不改**——已发布的值语义冻结，新增分类走追加。
/// </para>
/// </summary>
public enum OnlineGameEventCategory
{
    /// <summary>登录（安装、注册、登录、会话开始/结束）。</summary>
    Login = 1,

    /// <summary>匹配（进入匹配队列）。</summary>
    Matchmaking = 2,

    /// <summary>对局（开局、结束、胜、负、中途退出）。</summary>
    Match = 3,

    /// <summary>奖励（奖励发放、货币消耗）。</summary>
    Reward = 4,

    /// <summary>付费（支付确认成功对应的道具/货币到账）。</summary>
    Payment = 5,

    /// <summary>举报（举报受理、处罚生效）。</summary>
    Report = 6,
}
