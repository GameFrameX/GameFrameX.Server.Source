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

namespace GameFrameX.Hotfix.Logic.ServerRole.Auction;

/// <summary>
/// 挂拍处理器（创建拍卖批次）。
/// </summary>
[MessageMapping(typeof(ReqAuctionListLot))]
internal sealed class ReqAuctionListLotHandler : GlobalRpcComponentHandler<AuctionComponentAgent, ReqAuctionListLot, RespAuctionListLot>
{
    protected override async Task ActionAsync(ReqAuctionListLot request, RespAuctionListLot response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetworkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)AuctionErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnListLotAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 出价竞拍处理器。
/// </summary>
[MessageMapping(typeof(ReqAuctionBid))]
internal sealed class ReqAuctionBidHandler : GlobalRpcComponentHandler<AuctionComponentAgent, ReqAuctionBid, RespAuctionBid>
{
    protected override async Task ActionAsync(ReqAuctionBid request, RespAuctionBid response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetworkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)AuctionErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnBidAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 一口价买断处理器。
/// </summary>
[MessageMapping(typeof(ReqAuctionBuyout))]
internal sealed class ReqAuctionBuyoutHandler : GlobalRpcComponentHandler<AuctionComponentAgent, ReqAuctionBuyout, RespAuctionBuyout>
{
    protected override async Task ActionAsync(ReqAuctionBuyout request, RespAuctionBuyout response)
    {
        try
        {
            var playerId = ServerRoleHandlerHelper.GetCurrentPlayerId(NetworkChannel);
            if (playerId == 0)
            {
                response.ErrorCode = (int)AuctionErrorCode.NotLoggedIn;
                return;
            }

            await ComponentAgent.OnBuyoutAsync(playerId, request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 批次结算处理器。
/// </summary>
[MessageMapping(typeof(ReqAuctionSettle))]
internal sealed class ReqAuctionSettleHandler : GlobalRpcComponentHandler<AuctionComponentAgent, ReqAuctionSettle, RespAuctionSettle>
{
    protected override async Task ActionAsync(ReqAuctionSettle request, RespAuctionSettle response)
    {
        try
        {
            await ComponentAgent.OnSettleAsync(request, response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}

/// <summary>
/// 挂牌批次查询处理器（无需登录）。
/// </summary>
[MessageMapping(typeof(ReqAuctionQueryLots))]
internal sealed class ReqAuctionQueryLotsHandler : GlobalRpcComponentHandler<AuctionComponentAgent, ReqAuctionQueryLots, RespAuctionQueryLots>
{
    protected override async Task ActionAsync(ReqAuctionQueryLots request, RespAuctionQueryLots response)
    {
        try
        {
            await ComponentAgent.OnQueryLotsAsync(response);
        }
        catch (Exception e)
        {
            LogHelper.Fatal(e);
            response.ErrorCode = (int)OperationStatusCode.InternalServerError;
        }
    }
}
