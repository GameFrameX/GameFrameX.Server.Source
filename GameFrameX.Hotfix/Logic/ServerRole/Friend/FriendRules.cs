// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Collections.Generic;
using GameFrameX.Apps.ServerRole.Friend.Entity;

namespace GameFrameX.Hotfix.Logic.ServerRole.Friend;

/// <summary>
/// 好友业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的好友申请约束：集合化判重（自己 / 已好友 / 已在发出申请中）。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class FriendRules
{
    /// <summary>
    /// 判定能否向目标玩家发送好友申请：不能是自己、不能已是好友、不能已有待处理申请。
    /// </summary>
    /// <param name="selfPlayerId">申请发起玩家ID。</param>
    /// <param name="toPlayerId">目标玩家ID。</param>
    /// <param name="profile">发起者档案（好友 / 发出申请集合来源）。</param>
    /// <returns>可发送返回 true。</returns>
    public static bool CanSendRequest(long selfPlayerId, long toPlayerId, FriendProfileState profile)
    {
        if (selfPlayerId == toPlayerId)
        {
            return false;
        }

        var friends = new HashSet<long>(profile.Friends);
        if (friends.Contains(toPlayerId))
        {
            return false;
        }

        var outgoing = new HashSet<long>(profile.Outgoing);
        return !outgoing.Contains(toPlayerId);
    }

    /// <summary>
    /// 判定目标玩家是否为发起者的现有好友。
    /// </summary>
    /// <param name="profile">发起者档案。</param>
    /// <param name="targetPlayerId">目标玩家ID。</param>
    /// <returns>是好友返回 true。</returns>
    public static bool IsFriend(FriendProfileState profile, long targetPlayerId)
    {
        return new HashSet<long>(profile.Friends).Contains(targetPlayerId);
    }

    /// <summary>
    /// 判定发起者是否已向目标发出待处理申请。
    /// </summary>
    /// <param name="profile">发起者档案。</param>
    /// <param name="targetPlayerId">目标玩家ID。</param>
    /// <returns>已有申请返回 true。</returns>
    public static bool HasOutgoingRequest(FriendProfileState profile, long targetPlayerId)
    {
        return new HashSet<long>(profile.Outgoing).Contains(targetPlayerId);
    }

    /// <summary>
    /// 建立双向好友关系（去重追加），并清理双向残留申请。
    /// </summary>
    /// <param name="acceptor">接受方档案。</param>
    /// <param name="requester">申请方档案。</param>
    public static void Befriend(FriendProfileState acceptor, FriendProfileState requester)
    {
        AddUnique(acceptor.Friends, requester.PlayerId);
        AddUnique(requester.Friends, acceptor.PlayerId);
        RemoveAll(acceptor.Incoming, requester.PlayerId);
        RemoveAll(requester.Incoming, acceptor.PlayerId);
        RemoveAll(acceptor.Outgoing, requester.PlayerId);
        RemoveAll(requester.Outgoing, acceptor.PlayerId);
    }

    /// <summary>
    /// 双向解除好友关系并清理双向残留申请。
    /// </summary>
    /// <param name="self">发起解除方档案。</param>
    /// <param name="other">被解除方档案。</param>
    public static void Unfriend(FriendProfileState self, FriendProfileState other)
    {
        RemoveAll(self.Friends, other.PlayerId);
        RemoveAll(other.Friends, self.PlayerId);
        RemoveAll(self.Incoming, other.PlayerId);
        RemoveAll(other.Incoming, self.PlayerId);
        RemoveAll(self.Outgoing, other.PlayerId);
        RemoveAll(other.Outgoing, self.PlayerId);
    }

    /// <summary>
    /// 列表去重追加。
    /// </summary>
    /// <param name="list">目标列表。</param>
    /// <param name="value">追加值。</param>
    private static void AddUnique(List<long> list, long value)
    {
        if (!list.Contains(value))
        {
            list.Add(value);
        }
    }

    /// <summary>
    /// 从列表移除全部匹配项。
    /// </summary>
    /// <param name="list">目标列表。</param>
    /// <param name="value">移除值。</param>
    private static void RemoveAll(List<long> list, long value)
    {
        list.RemoveAll(item => item == value);
    }
}
