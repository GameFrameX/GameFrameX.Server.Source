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

using GameFrameX.Apps.ServerRole.Auction.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Auction;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Auction 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖挂拍参数校验分支、最低有效出价（首价=保留价 / 加价 ×1.1 向上取整）、
/// 到期判定边界与结算分支（有最高出价→Sold，无→Expired）。
/// </remarks>
public class AuctionRulesTests
{
    private static AuctionLotState NewLot(long reservePrice, long buyoutPrice, long highestBid, long expireUnixTime)
    {
        return new AuctionLotState
        {
            ReservePrice = reservePrice,
            BuyoutPrice = buyoutPrice,
            HighestBid = highestBid,
            ExpireUnixTime = expireUnixTime,
        };
    }

    [Fact]
    public void IsLotValid_ShouldEnforcePriceOrderAndExpiry()
    {
        Assert.True(AuctionRules.IsLotValid(100, 200, 5000, 4000));
        Assert.False(AuctionRules.IsLotValid(0, 200, 5000, 4000));
        Assert.False(AuctionRules.IsLotValid(100, 100, 5000, 4000));
        Assert.False(AuctionRules.IsLotValid(100, 99, 5000, 4000));
        Assert.False(AuctionRules.IsLotValid(100, 200, 4000, 4000));
    }

    [Fact]
    public void MinBid_NoBid_ShouldBeReservePrice()
    {
        var lot = NewLot(100, 200, 0, 5000);
        Assert.Equal(100, AuctionRules.MinBid(lot));
    }

    [Fact]
    public void MinBid_WithBid_ShouldBeTenPercentCeiling()
    {
        var lot = NewLot(100, 1000, 100, 5000);
        Assert.Equal(110, AuctionRules.MinBid(lot));

        // 101 × 1.1 = 111.1 → 向上取整 112
        var fractional = NewLot(100, 1000, 101, 5000);
        Assert.Equal(112, AuctionRules.MinBid(fractional));
    }

    [Fact]
    public void IsExpired_ShouldTreatNowAsExpired()
    {
        var lot = NewLot(100, 200, 0, 5000);
        Assert.False(AuctionRules.IsExpired(lot, 4999));
        Assert.True(AuctionRules.IsExpired(lot, 5000));
        Assert.True(AuctionRules.IsExpired(lot, 5001));
    }

    [Fact]
    public void SettleOutcome_WithBid_ShouldBeSold()
    {
        var lot = NewLot(100, 200, 150, 5000);
        Assert.Equal(AuctionLotStatus.Sold, AuctionRules.SettleOutcome(lot));
    }

    [Fact]
    public void SettleOutcome_WithoutBid_ShouldBeExpired()
    {
        var lot = NewLot(100, 200, 0, 5000);
        Assert.Equal(AuctionLotStatus.Expired, AuctionRules.SettleOutcome(lot));
    }

    [Fact]
    public void IsPriceValid_ShouldRejectNonPositive()
    {
        Assert.False(AuctionRules.IsPriceValid(0));
        Assert.False(AuctionRules.IsPriceValid(-1));
        Assert.True(AuctionRules.IsPriceValid(1));
    }
}
