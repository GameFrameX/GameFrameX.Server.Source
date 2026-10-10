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
using GameFrameX.Apps.ServerRole.Auction.Entity;
using GameFrameX.Proto.Proto;

namespace GameFrameX.Hotfix.Logic.ServerRole.Auction;

/// <summary>
/// 拍卖业务纯规则（可脱离 Actor 基础设施单测）。
/// </summary>
/// <remarks>
/// 承载与存储无关的拍卖规则：挂拍参数校验、最低有效出价（首价=保留价，其后=最高价 ×1.1 向上取整）、
/// 到期判定与结算分支（有最高出价→Sold，无→Expired）。
/// Agent 只做状态编排，规则判定一律走本类（<c>MailCampaignFilter</c> 先例）。
/// </remarks>
internal static class AuctionRules
{
    /// <summary>
    /// 判定出价是否合法（大于 0）。
    /// </summary>
    /// <param name="price">出价。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsPriceValid(long price)
    {
        return price > 0;
    }

    /// <summary>
    /// 判定挂拍参数是否合法：保留价大于 0、一口价大于保留价、到期时间晚于当前时间。
    /// </summary>
    /// <param name="reservePrice">保留价。</param>
    /// <param name="buyoutPrice">一口价。</param>
    /// <param name="expireUnixTime">到期 Unix 秒。</param>
    /// <param name="now">当前 Unix 秒。</param>
    /// <returns>合法返回 true。</returns>
    public static bool IsLotValid(long reservePrice, long buyoutPrice, long expireUnixTime, long now)
    {
        return reservePrice > 0 && buyoutPrice > reservePrice && expireUnixTime > now;
    }

    /// <summary>
    /// 计算当前最低有效出价：暂无出价时为保留价；有出价时为最高价 ×1.1 向上取整。
    /// </summary>
    /// <param name="lot">批次状态。</param>
    /// <returns>最低有效出价。</returns>
    public static long MinBid(AuctionLotState lot)
    {
        // 整数上取整（highest + ceil(highest/10)）实现 ×1.1 向上取整，规避 double 乘法伪进位（100×1.1 → 110.000…01 → 111）。
        return lot.HighestBid == 0 ? lot.ReservePrice : lot.HighestBid + (lot.HighestBid + 9) / 10;
    }

    /// <summary>
    /// 判定批次是否已到期（当前时间不早于到期时间视为到期）。
    /// </summary>
    /// <param name="lot">批次状态。</param>
    /// <param name="now">当前 Unix 秒。</param>
    /// <returns>到期返回 true。</returns>
    public static bool IsExpired(AuctionLotState lot, long now)
    {
        return now >= lot.ExpireUnixTime;
    }

    /// <summary>
    /// 结算分支判定（仅对已到期的挂牌批次有意义）：
    /// 有最高出价 → <see cref="AuctionLotStatus.Sold"/>（成交价为最高出价）；无 → <see cref="AuctionLotStatus.Expired"/>（流拍）。
    /// </summary>
    /// <param name="lot">批次状态。</param>
    /// <returns>结算后状态。</returns>
    public static AuctionLotStatus SettleOutcome(AuctionLotState lot)
    {
        return lot.HighestBid > 0 ? AuctionLotStatus.Sold : AuctionLotStatus.Expired;
    }
}
