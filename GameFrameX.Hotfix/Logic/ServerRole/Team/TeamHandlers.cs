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

namespace GameFrameX.Hotfix.Logic.ServerRole.Team;

/// <summary>
/// 创建队伍处理器。
/// </summary>
[MessageMapping(typeof(ReqTeamCreate))]
internal sealed class ReqTeamCreateHandler : GlobalRpcComponentHandler<TeamComponentAgent, ReqTeamCreate, RespTeamCreate>
{
    protected override async Task ActionAsync(ReqTeamCreate request, RespTeamCreate response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetWorkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)TeamErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnCreateAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 加入队伍处理器。
/// </summary>
[MessageMapping(typeof(ReqTeamJoin))]
internal sealed class ReqTeamJoinHandler : GlobalRpcComponentHandler<TeamComponentAgent, ReqTeamJoin, RespTeamJoin>
{
    protected override async Task ActionAsync(ReqTeamJoin request, RespTeamJoin response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetWorkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)TeamErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnJoinAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 离开队伍处理器。
/// </summary>
[MessageMapping(typeof(ReqTeamLeave))]
internal sealed class ReqTeamLeaveHandler : GlobalRpcComponentHandler<TeamComponentAgent, ReqTeamLeave, RespTeamLeave>
{
    protected override async Task ActionAsync(ReqTeamLeave request, RespTeamLeave response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetWorkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)TeamErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnLeaveAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 队长移除成员处理器。
/// </summary>
[MessageMapping(typeof(ReqTeamKick))]
internal sealed class ReqTeamKickHandler : GlobalRpcComponentHandler<TeamComponentAgent, ReqTeamKick, RespTeamKick>
{
    protected override async Task ActionAsync(ReqTeamKick request, RespTeamKick response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetWorkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)TeamErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnKickAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 解散队伍处理器。
/// </summary>
[MessageMapping(typeof(ReqTeamDisband))]
internal sealed class ReqTeamDisbandHandler : GlobalRpcComponentHandler<TeamComponentAgent, ReqTeamDisband, RespTeamDisband>
{
    protected override async Task ActionAsync(ReqTeamDisband request, RespTeamDisband response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetWorkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)TeamErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnDisbandAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 拉取队伍列表处理器。
/// </summary>
[MessageMapping(typeof(ReqTeamList))]
internal sealed class ReqTeamListHandler : GlobalRpcComponentHandler<TeamComponentAgent, ReqTeamList, RespTeamList>
{
    protected override async Task ActionAsync(ReqTeamList request, RespTeamList response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetWorkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)TeamErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnListAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}
