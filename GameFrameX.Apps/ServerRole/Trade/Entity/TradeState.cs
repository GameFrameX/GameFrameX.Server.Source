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

namespace GameFrameX.Apps.ServerRole.Trade.Entity;

/// <summary>
/// 挂单交易服务器（Trade Role）服务端作用域状态。
/// </summary>
/// <remarks>
/// 承载按物品划分的订单簿、全局订单索引与成交流水：
/// 生命周期由 StateComponent 管理（激活读取 / 消息完成回写 / 定时与停机兜底回存）。
/// </remarks>
public sealed class TradeState : BaseCacheState
{
    /// <summary>
    /// 按物品划分的订单簿。Key: 物品ID。
    /// </summary>
    public Dictionary<long, TradeBookState> Books { get; set; } = new Dictionary<long, TradeBookState>();

    /// <summary>
    /// 全部留量挂单索引。Key: 订单ID。
    /// </summary>
    public Dictionary<long, TradeOrderState> Orders { get; set; } = new Dictionary<long, TradeOrderState>();

    /// <summary>
    /// 下一个订单ID（从 10001 递增）。
    /// </summary>
    public long NextOrderId { get; set; } = 10001;

    /// <summary>
    /// 下一个成交流水ID（从 1 递增）。
    /// </summary>
    public long NextTradeId { get; set; } = 1;

    /// <summary>
    /// 成交流水（仅保留最近 TradeRules.MaxTrades 条）。
    /// </summary>
    public List<TradeDealState> Trades { get; set; } = new List<TradeDealState>();
}

/// <summary>
/// 单个物品的订单簿状态（买卖两侧挂单列表，各自按价格优先序维护）。
/// </summary>
public sealed class TradeBookState
{
    /// <summary>
    /// 物品ID。
    /// </summary>
    public long ItemId { get; set; }

    /// <summary>
    /// 买盘（高价优先，同价时间优先）。
    /// </summary>
    public List<TradeOrderState> Buys { get; set; } = new List<TradeOrderState>();

    /// <summary>
    /// 卖盘（低价优先，同价时间优先）。
    /// </summary>
    public List<TradeOrderState> Sells { get; set; } = new List<TradeOrderState>();
}

/// <summary>
/// 单笔挂单状态。
/// </summary>
public sealed class TradeOrderState
{
    /// <summary>
    /// 订单ID。
    /// </summary>
    public long OrderId { get; set; }

    /// <summary>
    /// 挂单玩家ID。
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// 挂单方向（Buy / Sell）。
    /// </summary>
    public GameFrameX.Proto.Proto.TradeSide Side { get; set; }

    /// <summary>
    /// 物品ID。
    /// </summary>
    public long ItemId { get; set; }

    /// <summary>
    /// 挂单数量。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 剩余未成交数量。
    /// </summary>
    public int Remaining { get; set; }

    /// <summary>
    /// 挂单单价。
    /// </summary>
    public long UnitPrice { get; set; }

    /// <summary>
    /// 挂单创建 Unix 秒。
    /// </summary>
    public long CreatedUnixTime { get; set; }
}

/// <summary>
/// 单笔成交流水状态。
/// </summary>
public sealed class TradeDealState
{
    /// <summary>
    /// 成交流水ID。
    /// </summary>
    public long TradeId { get; set; }

    /// <summary>
    /// 物品ID。
    /// </summary>
    public long ItemId { get; set; }

    /// <summary>
    /// 买方玩家ID。
    /// </summary>
    public long BuyerPlayerId { get; set; }

    /// <summary>
    /// 卖方玩家ID。
    /// </summary>
    public long SellerPlayerId { get; set; }

    /// <summary>
    /// 成交数量。
    /// </summary>
    public int Quantity { get; set; }

    /// <summary>
    /// 成交单价（挂单方 maker 价格）。
    /// </summary>
    public long UnitPrice { get; set; }

    /// <summary>
    /// 成交 Unix 秒。
    /// </summary>
    public long UnixTime { get; set; }
}
