// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 可信写入单玩家执行回执（服务内部类型：投影器在幂等 Execute 分支内调用写入，承载成功条目或拒绝码）。
/// <para>
/// 维护约束：失败语义与错误码段位固定（分数越界 → RiskControlRejected；高频 → RateLimitExceeded），
/// 调用方（投影器）据此决定幂等记录落 Fail 并逐玩家隔离；本类型不进公共 API 面。
/// </para>
/// </summary>
internal sealed class OnlineLeaderboardWriteOutcome
{
    /// <summary>
    /// 获取或设置是否成功落榜。
    /// </summary>
    public bool IsSuccess
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败错误码（失败时有效）。
    /// </summary>
    public OnlineErrorCode Code
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败原因（失败时有效）。
    /// </summary>
    public string Message
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置落榜后的条目副本（成功时有效）。
    /// </summary>
    public OnlineLeaderboardEntry Entry
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置落榜前分数（成功时有效；事件载荷的 OldScore）。
    /// </summary>
    public long OldScore
    {
        get;
        set;
    }
}
