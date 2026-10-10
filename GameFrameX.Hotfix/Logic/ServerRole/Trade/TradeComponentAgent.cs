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

using GameFrameX.Apps.ServerRole.Trade.Component;
using GameFrameX.Apps.ServerRole.Trade.Entity;
using GameFrameX.Foundation.Utility;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.ServerRole.Trade;

/// <summary>
/// 挂单交易服务器（Trade Role）业务组件代理：订单簿挂单 / 撤单 / 盘口查询与即时撮合。
/// </summary>
/// <remarks>
/// Trade Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="TradeRules"/>（纯函数，可单测）；
/// 本类只做状态编排：订单簿懒创建、新单撮合、留量挂单入簿、撤单摘除与成交流水落库。
/// 成交仅记录流水，不动背包/货币余额（物品系统属 Game 服，范围外）。
/// </remarks>
public class TradeComponentAgent : StateComponentAgent<TradeComponent, TradeState>
{
    /// <summary>
    /// 挂单：参数校验后创建新单并按对手盘价格优先序即时撮合；
    /// 留量归零返回 OrderId=0，否则留量入簿并返回挂单号。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">挂单请求。</param>
    /// <param name="response">挂单响应。</param>
    public Task OnPlaceAsync(long playerId, ReqTradePlace request, RespTradePlace response)
    {
        if (!TradeRules.IsSideValid(request.Side))
        {
            response.ErrorCode = (int)TradeErrorCode.SideInvalid;
            return Task.CompletedTask;
        }

        if (!TradeRules.IsQuantityValid(request.Quantity))
        {
            response.ErrorCode = (int)TradeErrorCode.QuantityInvalid;
            return Task.CompletedTask;
        }

        if (!TradeRules.IsPriceValid(request.UnitPrice))
        {
            response.ErrorCode = (int)TradeErrorCode.PriceInvalid;
            return Task.CompletedTask;
        }

        var now = TimerHelper.UnixTimeSeconds();
        var book = GetOrCreateBook(request.ItemId);
        var order = new TradeOrderState
        {
            OrderId = State.NextOrderId++,
            PlayerId = playerId,
            Side = request.Side,
            ItemId = request.ItemId,
            Quantity = request.Quantity,
            Remaining = request.Quantity,
            UnitPrice = request.UnitPrice,
            CreatedUnixTime = now,
        };

        var opposite = request.Side == TradeSide.Buy ? book.Sells : book.Buys;
        var (remaining, deals) = TradeRules.ExecuteMatch(order, opposite);
        foreach (var deal in deals)
        {
            var buyerPlayerId = request.Side == TradeSide.Buy ? playerId : deal.CounterpartyPlayerId;
            var sellerPlayerId = request.Side == TradeSide.Buy ? deal.CounterpartyPlayerId : playerId;
            var tradeId = State.NextTradeId++;
            response.Trades.Add(new TradeDealInfo
            {
                TradeId = tradeId,
                Quantity = deal.Quantity,
                UnitPrice = deal.UnitPrice,
                CounterpartyPlayerId = deal.CounterpartyPlayerId,
            });
            TradeRules.AppendTrade(State, new TradeDealState
            {
                TradeId = tradeId,
                ItemId = request.ItemId,
                BuyerPlayerId = buyerPlayerId,
                SellerPlayerId = sellerPlayerId,
                Quantity = deal.Quantity,
                UnitPrice = deal.UnitPrice,
                UnixTime = now,
            });
            if (deal.MakerRemaining == 0)
            {
                State.Orders.Remove(deal.OrderId);
            }
        }

        if (remaining > 0)
        {
            order.Remaining = (int)remaining;
            var ownSide = request.Side == TradeSide.Buy ? book.Buys : book.Sells;
            ownSide.Add(order);
            ownSide.Sort(request.Side == TradeSide.Buy ? TradeRules.CompareBuy : TradeRules.CompareSell);
            State.Orders[order.OrderId] = order;
            response.OrderId = order.OrderId;
        }

        response.Remaining = (int)remaining;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤单：仅可撤销本人且仍有剩余量的挂单；返回撤销时的剩余数量。
    /// </summary>
    /// <param name="playerId">玩家ID。</param>
    /// <param name="request">撤单请求。</param>
    /// <param name="response">撤单响应。</param>
    public Task OnCancelAsync(long playerId, ReqTradeCancel request, RespTradeCancel response)
    {
        if (!State.Orders.TryGetValue(request.OrderId, out var order) || order.PlayerId != playerId || order.Remaining <= 0)
        {
            response.ErrorCode = (int)TradeErrorCode.OrderNotFound;
            return Task.CompletedTask;
        }

        State.Orders.Remove(order.OrderId);
        if (State.Books.TryGetValue(order.ItemId, out var book))
        {
            var ownSide = order.Side == TradeSide.Buy ? book.Buys : book.Sells;
            ownSide.Remove(order);
        }

        response.Remaining = order.Remaining;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 盘口查询：按方向返回价格优先序（买高价优先 / 卖低价优先，同价时间优先）的挂单列表。
    /// </summary>
    /// <param name="request">盘口查询请求。</param>
    /// <param name="response">盘口查询响应。</param>
    public Task OnBookAsync(ReqTradeBook request, RespTradeBook response)
    {
        if (!TradeRules.IsSideValid(request.Side))
        {
            response.ErrorCode = (int)TradeErrorCode.SideInvalid;
            return Task.CompletedTask;
        }

        if (State.Books.TryGetValue(request.ItemId, out var book))
        {
            var ownSide = request.Side == TradeSide.Buy ? book.Buys : book.Sells;
            ownSide.Sort(request.Side == TradeSide.Buy ? TradeRules.CompareBuy : TradeRules.CompareSell);
            foreach (var order in ownSide)
            {
                response.Orders.Add(new TradeOrderInfo
                {
                    OrderId = order.OrderId,
                    PlayerId = order.PlayerId,
                    Remaining = order.Remaining,
                    UnitPrice = order.UnitPrice,
                });
            }
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询订单簿，不存在时按需懒创建。
    /// </summary>
    /// <param name="itemId">物品ID。</param>
    /// <returns>订单簿状态。</returns>
    private TradeBookState GetOrCreateBook(long itemId)
    {
        if (!State.Books.TryGetValue(itemId, out var book))
        {
            book = new TradeBookState { ItemId = itemId };
            State.Books[itemId] = book;
        }

        return book;
    }
}
