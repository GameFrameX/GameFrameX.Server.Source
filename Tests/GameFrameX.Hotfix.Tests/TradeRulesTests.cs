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

using GameFrameX.Apps.ServerRole.Trade.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Trade;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Trade 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖挂单参数校验、价格优先排序（买高价优先 / 卖低价优先、同价时间优先）、
/// 撮合跨单部分成交、价格不交叉停止、自我成交跳过与成交后对手单移除语义。
/// </remarks>
public class TradeRulesTests
{
    private static TradeOrderState NewOrder(long orderId, long playerId, TradeSide side, int quantity, long unitPrice, long createdUnixTime)
    {
        return new TradeOrderState
        {
            OrderId = orderId,
            PlayerId = playerId,
            Side = side,
            Quantity = quantity,
            Remaining = quantity,
            UnitPrice = unitPrice,
            CreatedUnixTime = createdUnixTime,
        };
    }

    [Fact]
    public void IsQuantityValid_ShouldRejectNonPositive()
    {
        Assert.False(TradeRules.IsQuantityValid(0));
        Assert.False(TradeRules.IsQuantityValid(-1));
        Assert.True(TradeRules.IsQuantityValid(1));
    }

    [Fact]
    public void IsPriceValid_ShouldRejectNonPositive()
    {
        Assert.False(TradeRules.IsPriceValid(0));
        Assert.False(TradeRules.IsPriceValid(-100));
        Assert.True(TradeRules.IsPriceValid(1));
    }

    [Fact]
    public void IsSideValid_ShouldRejectUndefined()
    {
        Assert.True(TradeRules.IsSideValid(TradeSide.Buy));
        Assert.True(TradeRules.IsSideValid(TradeSide.Sell));
        Assert.False(TradeRules.IsSideValid((TradeSide)99));
    }

    [Fact]
    public void CompareBuy_ShouldPreferHigherPriceThenEarlierTime()
    {
        var early = NewOrder(1, 10, TradeSide.Buy, 5, 100, 1000);
        var late = NewOrder(2, 11, TradeSide.Buy, 5, 100, 2000);
        Assert.Equal(0, TradeRules.CompareBuy(early, early));
        Assert.True(TradeRules.CompareBuy(early, late) < 0);

        var higher = NewOrder(3, 12, TradeSide.Buy, 5, 101, 3000);
        Assert.True(TradeRules.CompareBuy(higher, early) < 0);
    }

    [Fact]
    public void CompareSell_ShouldPreferLowerPriceThenEarlierTime()
    {
        var early = NewOrder(1, 10, TradeSide.Sell, 5, 100, 1000);
        var late = NewOrder(2, 11, TradeSide.Sell, 5, 100, 2000);
        Assert.True(TradeRules.CompareSell(early, late) < 0);

        var lower = NewOrder(3, 12, TradeSide.Sell, 5, 99, 3000);
        Assert.True(TradeRules.CompareSell(lower, early) < 0);
    }

    [Fact]
    public void ExecuteMatch_ShouldFillAcrossMultipleOrdersPartially()
    {
        var opposite = new List<TradeOrderState>
        {
            NewOrder(1, 10, TradeSide.Sell, 5, 100, 1000),
            NewOrder(2, 11, TradeSide.Sell, 5, 101, 1001),
        };
        var incoming = NewOrder(3, 20, TradeSide.Buy, 7, 105, 1002);

        var (remaining, deals) = TradeRules.ExecuteMatch(incoming, opposite);

        Assert.Equal(0, remaining);
        Assert.Equal(2, deals.Count);
        // 成交价按挂单（maker）价
        Assert.Equal(1, deals[0].OrderId);
        Assert.Equal(5, deals[0].Quantity);
        Assert.Equal(100, deals[0].UnitPrice);
        Assert.Equal(0, deals[0].MakerRemaining);
        Assert.Equal(2, deals[1].OrderId);
        Assert.Equal(2, deals[1].Quantity);
        Assert.Equal(101, deals[1].UnitPrice);
        Assert.Equal(3, deals[1].MakerRemaining);
        // 全部成交的首笔挂单被移除，第二笔保留剩余量
        Assert.Single(opposite);
        Assert.Equal(2, opposite[0].OrderId);
        Assert.Equal(3, opposite[0].Remaining);
    }

    [Fact]
    public void ExecuteMatch_ShouldStopWhenPriceNotCrossed()
    {
        var opposite = new List<TradeOrderState>
        {
            NewOrder(1, 10, TradeSide.Sell, 5, 101, 1000),
        };
        var incoming = NewOrder(2, 20, TradeSide.Buy, 5, 100, 1001);

        var (remaining, deals) = TradeRules.ExecuteMatch(incoming, opposite);

        // 买价 100 < 最佳卖价 101：不交叉，留量挂单
        Assert.Equal(5, remaining);
        Assert.Empty(deals);
        Assert.Single(opposite);
    }

    [Fact]
    public void ExecuteMatch_SellIncoming_ShouldStopWhenAboveBestBuy()
    {
        var opposite = new List<TradeOrderState>
        {
            NewOrder(1, 10, TradeSide.Buy, 5, 100, 1000),
        };
        var incoming = NewOrder(2, 20, TradeSide.Sell, 5, 101, 1001);

        var (remaining, deals) = TradeRules.ExecuteMatch(incoming, opposite);

        // 卖价 101 > 最佳买价 100：不交叉
        Assert.Equal(5, remaining);
        Assert.Empty(deals);
    }

    [Fact]
    public void ExecuteMatch_ShouldSkipSelfOrderAndMatchOthers()
    {
        var opposite = new List<TradeOrderState>
        {
            NewOrder(1, 20, TradeSide.Sell, 5, 99, 1000),
            NewOrder(2, 10, TradeSide.Sell, 5, 100, 1001),
        };
        var incoming = NewOrder(3, 20, TradeSide.Buy, 5, 100, 1002);

        var (remaining, deals) = TradeRules.ExecuteMatch(incoming, opposite);

        // 跳过自己的更优价挂单，正常成交他人单
        Assert.Equal(0, remaining);
        Assert.Single(deals);
        Assert.Equal(2, deals[0].OrderId);
        Assert.Equal(10, deals[0].CounterpartyPlayerId);
        // 自己的挂单仍保留在对手盘
        Assert.Single(opposite);
        Assert.Equal(1, opposite[0].OrderId);
    }

    [Fact]
    public void ExecuteMatch_OnlySelfOrderLeft_ShouldRetainIncoming()
    {
        var opposite = new List<TradeOrderState>
        {
            NewOrder(1, 20, TradeSide.Sell, 5, 99, 1000),
        };
        var incoming = NewOrder(2, 20, TradeSide.Buy, 5, 100, 1001);

        var (remaining, deals) = TradeRules.ExecuteMatch(incoming, opposite);

        Assert.Equal(5, remaining);
        Assert.Empty(deals);
        Assert.Single(opposite);
    }
}
