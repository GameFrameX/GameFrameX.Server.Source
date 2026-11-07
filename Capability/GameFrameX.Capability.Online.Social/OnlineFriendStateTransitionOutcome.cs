//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
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
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Social;

/// <summary>
/// 带好友上限判定的状态迁移结果（<see cref="IOnlineFriendStore.UpdateStateWithFriendLimitAsync"/> 的唯一返回形态）。
/// <para>
/// 维护约束：三态互斥——迁移成功时 <see cref="Friendship"/> 为更新后的副本且
/// <see cref="FriendLimitReached"/> 为 <c>false</c>；任一侧已建立好友数达到上限时
/// <see cref="FriendLimitReached"/> 为 <c>true</c> 且零写入（<see cref="Friendship"/> 为 null）；
/// CAS 失败（状态被并发改写 / 记录不存在 / 作用域不符）时两者分别为 null 与 <c>false</c>，
/// 调用方据此走「重读当前事实」的收敛分支，而不是误判为上限拒绝。
/// </para>
/// </summary>
public sealed class OnlineFriendStateTransitionOutcome
{
    /// <summary>
    /// 初始化 <see cref="OnlineFriendStateTransitionOutcome"/>。
    /// </summary>
    /// <param name="friendship">更新后的关系副本（仅迁移成功时非空）。</param>
    /// <param name="friendLimitReached">是否因好友上限拒绝（真拒绝时无任何写入）。</param>
    private OnlineFriendStateTransitionOutcome(OnlineFriendship friendship, bool friendLimitReached)
    {
        Friendship = friendship;
        FriendLimitReached = friendLimitReached;
    }

    /// <summary>
    /// 获取迁移成功后的关系副本；CAS 失败或上限拒绝时为 null。
    /// </summary>
    /// <remarks>Gets the updated friendship copy; null on CAS failure or friend-limit rejection.</remarks>
    public OnlineFriendship Friendship { get; }

    /// <summary>
    /// 获取是否因任一侧好友数达到上限而被拒绝（零写入）。
    /// </summary>
    /// <remarks>Gets whether the transition was rejected because either side reached the friend limit (no write applied).</remarks>
    public bool FriendLimitReached { get; }

    /// <summary>
    /// 构造迁移成功结果。
    /// </summary>
    /// <param name="friendship">更新后的关系副本。</param>
    /// <returns>成功结果。</returns>
    public static OnlineFriendStateTransitionOutcome FromUpdated(OnlineFriendship friendship)
    {
        return new OnlineFriendStateTransitionOutcome(friendship, false);
    }

    /// <summary>
    /// 构造 CAS 失败结果（状态被并发改写 / 记录不存在 / 作用域不符）。
    /// </summary>
    /// <returns>CAS 失败结果。</returns>
    public static OnlineFriendStateTransitionOutcome FromCasConflict()
    {
        return new OnlineFriendStateTransitionOutcome(null, false);
    }

    /// <summary>
    /// 构造好友上限拒绝结果（零写入）。
    /// </summary>
    /// <returns>上限拒绝结果。</returns>
    public static OnlineFriendStateTransitionOutcome FromFriendLimitReached()
    {
        return new OnlineFriendStateTransitionOutcome(null, true);
    }
}
