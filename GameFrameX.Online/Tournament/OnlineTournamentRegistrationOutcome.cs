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

namespace GameFrameX.Online.Tournament;

/// <summary>
/// 赛事报名回执（vault:C8 VC-7.8：报名结论 + 机读拒绝原因 + 登记事实）。
/// <para>
/// 维护约束（红线）：资格不满足时服务返回**成功回执**并置 <see cref="Rejection"/>，而**不是**失败错误码——
/// 资格判定是业务结论（对齐 C99 <c>OnlineSocialDecision</c>「允许 + 拒绝码 + 原因」先例），
/// 系统级失败（赛事不存在 / 状态不允许报名 / 榜单不可读）仍走 <c>OnlineResult</c> 的失败分支。
/// </para>
/// </summary>
public sealed class OnlineTournamentRegistrationOutcome
{
    /// <summary>
    /// 获取或设置报名是否通过（拒绝时为 <c>false</c>，此时 <see cref="Registration"/> 为 null）。
    /// </summary>
    public bool Accepted
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否为重复报名（命中原有登记，不新增、不重发事件）。
    /// </summary>
    public bool IsReplay
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置拒绝原因（通过时为 <see cref="OnlineTournamentRegistrationRejection.None"/>）。
    /// </summary>
    public OnlineTournamentRegistrationRejection Rejection
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置报名登记（拒绝时为 null）。
    /// </summary>
    public OnlineTournamentRegistration Registration
    {
        get;
        set;
    }
}
