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

namespace GameFrameX.Apps.ServerRole.Auction.Entity;

/// <summary>
/// 拍卖服务器（Auction Role）服务端作用域状态。
/// </summary>
/// <remarks>
/// 承载全部拍卖批次：生命周期由 StateComponent 管理
/// （激活读取 / 消息完成回写 / 定时与停机兜底回存）。
/// </remarks>
public sealed class AuctionState : BaseCacheState
{
    /// <summary>
    /// 全部拍卖批次。Key: 批次ID。
    /// </summary>
    public Dictionary<long, AuctionLotState> Lots { get; set; } = new Dictionary<long, AuctionLotState>();

    /// <summary>
    /// 下一个批次ID（从 10001 递增）。
    /// </summary>
    public long NextLotId { get; set; } = 10001;
}

/// <summary>
/// 单个拍卖批次状态。
/// </summary>
public sealed class AuctionLotState
{
    /// <summary>
    /// 批次ID。
    /// </summary>
    public long LotId { get; set; }

    /// <summary>
    /// 挂拍货主玩家ID。
    /// </summary>
    public long OwnerPlayerId { get; set; }

    /// <summary>
    /// 物品ID。
    /// </summary>
    public long ItemId { get; set; }

    /// <summary>
    /// 保留价（起拍价）。
    /// </summary>
    public long ReservePrice { get; set; }

    /// <summary>
    /// 一口价。
    /// </summary>
    public long BuyoutPrice { get; set; }

    /// <summary>
    /// 当前最高出价（0 = 暂无出价）。
    /// </summary>
    public long HighestBid { get; set; }

    /// <summary>
    /// 当前最高出价玩家ID（0 = 暂无出价）。
    /// </summary>
    public long HighestBidderPlayerId { get; set; }

    /// <summary>
    /// 有效出价次数。
    /// </summary>
    public int BidCount { get; set; }

    /// <summary>
    /// 批次状态（Listing / Sold / Expired）。
    /// </summary>
    public GameFrameX.Proto.Proto.AuctionLotStatus Status { get; set; } = GameFrameX.Proto.Proto.AuctionLotStatus.Listing;

    /// <summary>
    /// 挂拍创建 Unix 秒。
    /// </summary>
    public long CreatedUnixTime { get; set; }

    /// <summary>
    /// 到期 Unix 秒。
    /// </summary>
    public long ExpireUnixTime { get; set; }
}
