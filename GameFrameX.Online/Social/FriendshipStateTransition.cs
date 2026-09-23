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
//   please see the LICENSE file in the root directory of the source code.
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

namespace GameFrameX.Online.Social;

/// <summary>
/// 好友关系状态 CAS 迁移载荷（<see cref="IOnlineFriendStore.UpdateStateAsync"/> 的唯一提交形态）。
/// <para>
/// 维护约束：期望状态不匹配即整体失败并返回 null，绝不部分应用——
/// 并发「接受 vs 拒绝」由此收敛到同一临界区。
/// </para>
/// </summary>
public sealed class FriendshipStateTransition
{
    /// <summary>
    /// 获取或设置关系标识。
    /// </summary>
    /// <remarks>Gets or sets the friendship id.</remarks>
    public string FriendshipId { get; init; }

    /// <summary>
    /// 获取或设置期望的当前状态；不匹配即失败。
    /// </summary>
    /// <remarks>Gets or sets the expected current state; a mismatch fails the whole transition.</remarks>
    public OnlineFriendshipState ExpectedState { get; init; }

    /// <summary>
    /// 获取或设置目标状态。
    /// </summary>
    /// <remarks>Gets or sets the target state.</remarks>
    public OnlineFriendshipState NewState { get; init; }

    /// <summary>
    /// 获取或设置本次变更时刻（UTC 毫秒）。
    /// </summary>
    /// <remarks>Gets or sets the change time (UTC milliseconds).</remarks>
    public long NowUnixMilliseconds { get; init; }

    /// <summary>
    /// 获取或设置本次变更是否构成一次答复（答复时回填答复时刻）。
    /// </summary>
    /// <remarks>Gets or sets whether this change counts as a response (fills the responded-at time when set).</remarks>
    public bool Responded { get; init; }
}
