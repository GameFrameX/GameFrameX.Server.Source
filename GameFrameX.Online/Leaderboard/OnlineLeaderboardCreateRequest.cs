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

namespace GameFrameX.Online.Leaderboard;

/// <summary>
/// 排行榜创建请求（服务端运营 / 运行时装配入口；客户端无此入口）。
/// <para>
/// 维护约束：作用域取鉴权上下文的 <see cref="OnlineScope"/>，请求体不携带 TenantId / AppId（服务端不信任请求体作用域）；
/// 同名榜单在 (TenantId, AppId) 内已存在时创建被拒绝（ParameterInvalid），不覆盖既有榜单。
/// </para>
/// </summary>
public sealed class OnlineLeaderboardCreateRequest
{
    /// <summary>
    /// 获取或设置榜单标识（App 内唯一，非空）。
    /// </summary>
    public string LeaderboardId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置排序方向（默认高分优先）。
    /// </summary>
    public OnlineLeaderboardSortOrder SortOrder
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置分数累计策略（默认保留最优）。
    /// </summary>
    public OnlineLeaderboardScoreUpdatePolicy ScoreUpdatePolicy
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置单次提交分数合理上限（0 = 使用全局默认值）。
    /// </summary>
    public long MaxScorePerSubmission
    {
        get;
        set;
    }
}
