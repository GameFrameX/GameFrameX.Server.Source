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

using GameFrameX.Apps.ServerRole.World.Component;
using GameFrameX.Apps.ServerRole.World.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.World;

/// <summary>
/// 世界公告服务器（World Role）业务组件代理：公告发布、撤销与生效中列表查询。
/// </summary>
/// <remarks>
/// World Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="WorldRules"/>（纯函数，可单测）；
/// 本类只做状态编排：公告落库、仅 Active 可撤销、查询时惰性置 Expired 并过滤。
/// </remarks>
public class WorldComponentAgent : StateComponentAgent<WorldComponent, WorldState>
{
    /// <summary>
    /// 发布公告：标题/过期时间校验通过后落库（状态 Active）。
    /// </summary>
    /// <param name="playerId">发布者玩家ID。</param>
    /// <param name="request">发布请求。</param>
    /// <param name="response">发布响应。</param>
    public Task OnPublishAsync(long playerId, ReqWorldPublish request, RespWorldPublish response)
    {
        if (!WorldRules.IsTitleValid(request.Title))
        {
            response.ErrorCode = (int)WorldErrorCode.TitleInvalid;
            return Task.CompletedTask;
        }

        var now = TimerHelper.UnixTimeSeconds();
        if (!WorldRules.IsExpireValid(request.ExpireUnixTime, now))
        {
            response.ErrorCode = (int)WorldErrorCode.ExpireInvalid;
            return Task.CompletedTask;
        }

        var announcement = new WorldAnnouncementState
        {
            Id = State.NextAnnouncementId++,
            Title = request.Title,
            Content = request.Content,
            Status = WorldAnnouncementStatus.Active,
            PublisherPlayerId = playerId,
            CreatedUnixTime = now,
            ExpireUnixTime = request.ExpireUnixTime,
        };
        State.Announcements[announcement.Id] = announcement;
        response.AnnouncementId = announcement.Id;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤销公告：仅 Active 可撤销（否则 <see cref="WorldErrorCode.NotActive"/>）。
    /// </summary>
    /// <param name="request">撤销请求。</param>
    /// <param name="response">撤销响应。</param>
    public Task OnRevokeAsync(ReqWorldRevoke request, RespWorldRevoke response)
    {
        if (!State.Announcements.TryGetValue(request.AnnouncementId, out var announcement))
        {
            response.ErrorCode = (int)WorldErrorCode.AnnouncementNotFound;
            return Task.CompletedTask;
        }

        if (announcement.Status != WorldAnnouncementStatus.Active)
        {
            response.ErrorCode = (int)WorldErrorCode.NotActive;
            return Task.CompletedTask;
        }

        announcement.Status = WorldAnnouncementStatus.Revoked;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询生效中公告：Active 但已到过期时间的惰性置 Expired，最终仅返回 Active。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryAsync(ReqWorldQuery request, RespWorldQuery response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        foreach (var announcement in State.Announcements.Values)
        {
            if (announcement.Status == WorldAnnouncementStatus.Active && WorldRules.IsExpired(announcement, now))
            {
                announcement.Status = WorldAnnouncementStatus.Expired;
            }

            if (announcement.Status == WorldAnnouncementStatus.Active)
            {
                response.Announcements.Add(new WorldAnnouncementInfo
                {
                    Id = announcement.Id,
                    Title = announcement.Title,
                    Content = announcement.Content,
                    PublisherPlayerId = announcement.PublisherPlayerId,
                    CreatedUnixTime = announcement.CreatedUnixTime,
                    ExpireUnixTime = announcement.ExpireUnixTime,
                });
            }
        }

        return Task.CompletedTask;
    }
}
