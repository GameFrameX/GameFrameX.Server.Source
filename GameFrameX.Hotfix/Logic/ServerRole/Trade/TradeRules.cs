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

using System;
using System.Collections.Generic;
using GameFrameX.Apps.ServerRole.Trade.Entity;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.ServerRole.Trade;

/// <summary>
/// 挂单交易业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的订单簿规则：挂单参数校验、价格优先排序（买高价优先 / 卖低价优先、同价时间优先）、
/// 撮合算法（按序逐单成交直至留量归零或价格不再交叉）、成交流水环形削顶。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class TradeRules
{
    /// <summary>
    /// 成交流水保留条数上限（环形削顶）。
    /// </summary>
    public const int MaxTrades = 200;

    /// <summary>
    /// 判定挂单方向是否合法（仅 Buy / Sell 有定义）。
    /// </summary>
    /// <param name="side">挂单方向。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsSideValid(TradeSide side)
    {
        return side == TradeSide.Buy || side == TradeSide.Sell;
    }

    /// <summary>
    /// 判定挂单数量是否合法（大于 0）。
    /// </summary>
    /// <param name="quantity">挂单数量。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsQuantityValid(int quantity)
    {
        return quantity > 0;
    }

    /// <summary>
    /// 判定挂单单价是否合法（大于 0）。
    /// </summary>
    /// <param name="unitPrice">挂单单价。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsPriceValid(long unitPrice)
    {
        return unitPrice > 0;
    }

    /// <summary>
    /// 买盘价格优先比较器：高价优先，同价创建时间早者优先。
    /// </summary>
    /// <param name="left">左订单。</param>
    /// <param name="right">右订单。</param>
    /// <returns>排序比较结果（负值表示 left 排前）。</returns>
    public static int CompareBuy(TradeOrderState left, TradeOrderState right)
    {
        var price = right.UnitPrice.CompareTo(left.UnitPrice);
        return price != 0 ? price : left.CreatedUnixTime.CompareTo(right.CreatedUnixTime);
    }

    /// <summary>
    /// 卖盘价格优先比较器：低价优先，同价创建时间早者优先。
    /// </summary>
    /// <param name="left">左订单。</param>
    /// <param name="right">右订单。</param>
    /// <returns>排序比较结果（负值表示 left 排前）。</returns>
    public static int CompareSell(TradeOrderState left, TradeOrderState right)
    {
        var price = left.UnitPrice.CompareTo(right.UnitPrice);
        return price != 0 ? price : left.CreatedUnixTime.CompareTo(right.CreatedUnixTime);
    }

    /// <summary>
    /// 判定吃单方与挂单方（maker）价格是否交叉：
    /// 买单吃卖盘时须买价不低于卖价；卖单吃买盘时须卖价不高于买价。
    /// </summary>
    /// <param name="incomingSide">吃单方向。</param>
    /// <param name="incomingPrice">吃单价格。</param>
    /// <param name="makerPrice">对手挂单价格。</param>
    /// <returns>交叉返回 true（可成交）。</returns>
    public static bool IsPriceCrossed(TradeSide incomingSide, long incomingPrice, long makerPrice)
    {
        return incomingSide == TradeSide.Buy ? incomingPrice >= makerPrice : incomingPrice <= makerPrice;
    }

    /// <summary>
    /// 撮合核心算法：吃单按对手盘价格优先序逐单成交，直至留量归零或价格不再交叉。
    /// </summary>
    /// <remarks>
    /// 成交价取挂单方（maker）价格；部分成交扣减 maker 剩余量，剩余量归零的对手单从列表移除；
    /// 跳过自己的挂单（自我成交被跳过而非报错，若因跳过导致无可成交则留量挂单）；
    /// 列表变异由调用方（Agent / 单测）负责应用与持久化。
    /// </remarks>
    /// <param name="incoming">吃单（Remaining 为吃单数量）。</param>
    /// <param name="opposite">对手盘挂单列表（须已按价格优先序排列）。</param>
    /// <returns>撮合后的剩余数量与逐笔成交明细。</returns>
    public static (long Remaining, List<TradeMatchDeal> Deals) ExecuteMatch(TradeOrderState incoming, List<TradeOrderState> opposite)
    {
        var deals = new List<TradeMatchDeal>();
        long remaining = incoming.Remaining;
        for (var index = 0; index < opposite.Count && remaining > 0;)
        {
            var maker = opposite[index];
            if (!IsPriceCrossed(incoming.Side, incoming.UnitPrice, maker.UnitPrice))
            {
                break;
            }

            if (maker.PlayerId == incoming.PlayerId)
            {
                index++;
                continue;
            }

            var quantity = (int)Math.Min(remaining, maker.Remaining);
            remaining -= quantity;
            maker.Remaining -= quantity;
            deals.Add(new TradeMatchDeal
            {
                OrderId = maker.OrderId,
                CounterpartyPlayerId = maker.PlayerId,
                Quantity = quantity,
                UnitPrice = maker.UnitPrice,
                MakerRemaining = maker.Remaining,
            });
            if (maker.Remaining == 0)
            {
                opposite.RemoveAt(index);
            }
            else
            {
                index++;
            }
        }

        return (remaining, deals);
    }

    /// <summary>
    /// 追加成交流水并维持环形削顶（仅保留最近 <see cref="MaxTrades"/> 条）。
    /// </summary>
    /// <param name="state">交易状态。</param>
    /// <param name="deal">新成交流水。</param>
    public static void AppendTrade(TradeState state, TradeDealState deal)
    {
        state.Trades.Add(deal);
        while (state.Trades.Count > MaxTrades)
        {
            state.Trades.RemoveAt(0);
        }
    }
}

/// <summary>
/// 撮合成交明细（Rules 层中间结果，由 Agent 转为协议回执与流水落库）。
/// </summary>
public sealed class TradeMatchDeal
{
    /// <summary>
    /// 对手挂单（maker）订单ID。
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// 对手方玩家ID。
    /// </summary>
    public long CounterpartyPlayerId { get; set; }

    /// <summary>
    /// 成交数量。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 成交单价（maker 价格）。
    /// </summary>
    public long UnitPrice { get; set; }

    /// <summary>
    /// 撮合后对手挂单剩余量（0 表示该挂单已全部成交并从订单簿移除）。
    /// </summary>
    public int MakerRemaining { get; set; }
}
