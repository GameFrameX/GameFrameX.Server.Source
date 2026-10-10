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

using GameFrameX.Apps.ServerRole.Auction.Component;
using GameFrameX.Apps.ServerRole.Auction.Entity;
using GameFrameX.Foundation.Utility;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.ServerRole.Auction;

/// <summary>
/// 拍卖服务器（Auction Role）业务组件代理：挂拍 / 出价 / 一口价 / 结算 / 批次查询。
/// </summary>
/// <remarks>
/// Auction Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="AuctionRules"/>（纯函数，可单测）；
/// 本类只做状态编排：批次创建、出价状态迁移、到期惰性结算与挂牌列表过滤。
/// 成交仅记录批次状态，不动背包/货币余额（物品系统属 Game 服，范围外）。
/// </remarks>
public class AuctionComponentAgent : StateComponentAgent<AuctionComponent, AuctionState>
{
    /// <summary>
    /// 挂拍：参数校验（保留价&gt;0、一口价&gt;保留价、到期晚于当前）通过后创建 Listing 批次。
    /// </summary>
    /// <param name="playerId">货主玩家ID。</param>
    /// <param name="request">挂拍请求。</param>
    /// <param name="response">挂拍响应。</param>
    public Task OnListLotAsync(long playerId, ReqAuctionListLot request, RespAuctionListLot response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        if (!AuctionRules.IsLotValid(request.ReservePrice, request.BuyoutPrice, request.ExpireUnixTime, now))
        {
            response.ErrorCode = (int)AuctionErrorCode.PriceInvalid;
            return Task.CompletedTask;
        }

        var lot = new AuctionLotState
        {
            LotId = State.NextLotId++,
            OwnerPlayerId = playerId,
            ItemId = request.ItemId,
            ReservePrice = request.ReservePrice,
            BuyoutPrice = request.BuyoutPrice,
            ExpireUnixTime = request.ExpireUnixTime,
            CreatedUnixTime = now,
        };
        State.Lots[lot.LotId] = lot;
        response.LotId = lot.LotId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 出价：到期批次先惰性结算并拒绝；不能拍自己挂拍的批次；
    /// 出价须不低于最低有效出价（首价=保留价，其后=最高价 ×1.1 向上取整）。
    /// </summary>
    /// <param name="playerId">出价玩家ID。</param>
    /// <param name="request">出价请求。</param>
    /// <param name="response">出价响应。</param>
    public Task OnBidAsync(long playerId, ReqAuctionBid request, RespAuctionBid response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        if (!State.Lots.TryGetValue(request.LotId, out var lot))
        {
            response.ErrorCode = (int)AuctionErrorCode.LotNotFound;
            return Task.CompletedTask;
        }

        if (SettleIfEnded(lot, now))
        {
            response.ErrorCode = (int)AuctionErrorCode.LotEnded;
            return Task.CompletedTask;
        }

        if (lot.OwnerPlayerId == playerId)
        {
            response.ErrorCode = (int)AuctionErrorCode.CannotSelfBid;
            return Task.CompletedTask;
        }

        if (!AuctionRules.IsPriceValid(request.Price))
        {
            response.ErrorCode = (int)AuctionErrorCode.PriceInvalid;
            return Task.CompletedTask;
        }

        if (request.Price < AuctionRules.MinBid(lot))
        {
            response.ErrorCode = (int)AuctionErrorCode.BidTooLow;
            return Task.CompletedTask;
        }

        lot.HighestBid = request.Price;
        lot.HighestBidderPlayerId = playerId;
        lot.BidCount++;
        response.HighestBid = lot.HighestBid;
        response.BidCount = lot.BidCount;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 一口价买断：到期批次先惰性结算并拒绝；不能买断自己挂拍的批次；
    /// 成功后批次即时置 Sold，成交价为一口价。
    /// </summary>
    /// <param name="playerId">买断玩家ID。</param>
    /// <param name="request">买断请求。</param>
    /// <param name="response">买断响应。</param>
    public Task OnBuyoutAsync(long playerId, ReqAuctionBuyout request, RespAuctionBuyout response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        if (!State.Lots.TryGetValue(request.LotId, out var lot))
        {
            response.ErrorCode = (int)AuctionErrorCode.LotNotFound;
            return Task.CompletedTask;
        }

        if (SettleIfEnded(lot, now))
        {
            response.ErrorCode = (int)AuctionErrorCode.LotEnded;
            return Task.CompletedTask;
        }

        if (lot.OwnerPlayerId == playerId)
        {
            response.ErrorCode = (int)AuctionErrorCode.CannotSelfBid;
            return Task.CompletedTask;
        }

        lot.Status = AuctionLotStatus.Sold;
        lot.HighestBid = lot.BuyoutPrice;
        lot.HighestBidderPlayerId = playerId;
        lot.BidCount++;
        response.FinalPrice = lot.BuyoutPrice;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 结算：仅可结算已到期的挂牌批次（未到期拒绝）；
    /// 有最高出价 → Sold（成交价为最高出价），无 → Expired（流拍）。
    /// </summary>
    /// <param name="request">结算请求。</param>
    /// <param name="response">结算响应。</param>
    public Task OnSettleAsync(ReqAuctionSettle request, RespAuctionSettle response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        if (!State.Lots.TryGetValue(request.LotId, out var lot))
        {
            response.ErrorCode = (int)AuctionErrorCode.LotNotFound;
            return Task.CompletedTask;
        }

        if (lot.Status != AuctionLotStatus.Listing)
        {
            response.ErrorCode = (int)AuctionErrorCode.LotEnded;
            return Task.CompletedTask;
        }

        if (!AuctionRules.IsExpired(lot, now))
        {
            response.ErrorCode = (int)AuctionErrorCode.NotExpired;
            return Task.CompletedTask;
        }

        lot.Status = AuctionRules.SettleOutcome(lot);
        response.Status = lot.Status;
        response.FinalPrice = lot.Status == AuctionLotStatus.Sold ? lot.HighestBid : 0;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询挂牌中的批次：过期批次惰性结算后仅返回仍为 Listing 的批次。
    /// </summary>
    /// <param name="response">查询响应。</param>
    public Task OnQueryLotsAsync(RespAuctionQueryLots response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        foreach (var lot in State.Lots.Values)
        {
            if (lot.Status == AuctionLotStatus.Listing && AuctionRules.IsExpired(lot, now))
            {
                lot.Status = AuctionRules.SettleOutcome(lot);
            }

            if (lot.Status != AuctionLotStatus.Listing)
            {
                continue;
            }

            response.Lots.Add(new AuctionLotInfo
            {
                LotId = lot.LotId,
                ItemId = lot.ItemId,
                ReservePrice = lot.ReservePrice,
                BuyoutPrice = lot.BuyoutPrice,
                HighestBid = lot.HighestBid,
                HighestBidderPlayerId = lot.HighestBidderPlayerId,
                BidCount = lot.BidCount,
                Status = lot.Status,
                ExpireUnixTime = lot.ExpireUnixTime,
            });
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 惰性结算：挂牌中且已到期的批次按结算分支落定状态。
    /// </summary>
    /// <param name="lot">批次状态。</param>
    /// <param name="now">当前 Unix 秒。</param>
    /// <returns>批次已结束（本次结算或此前已非 Listing）返回 true。</returns>
    private static bool SettleIfEnded(AuctionLotState lot, long now)
    {
        if (lot.Status != AuctionLotStatus.Listing)
        {
            return true;
        }

        if (!AuctionRules.IsExpired(lot, now))
        {
            return false;
        }

        lot.Status = AuctionRules.SettleOutcome(lot);
        return true;
    }
}
