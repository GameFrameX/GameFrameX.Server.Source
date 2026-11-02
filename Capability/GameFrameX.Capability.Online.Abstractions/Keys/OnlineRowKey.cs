// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Abstractions.Keys;

/// <summary>
/// Online 物化行键唯一构造点（C462 单键化裁决 D4：全链只删 Tenant/App 两位，绝不新增 ServerId 位）。
/// <para>
/// 维护约束（红线）：
/// ① 全部持久化行键 / InMemory 同构键**只能**经本类构造，禁止各包自拼字符串（键形漂移 = 唯一索引失效）；
/// ② 行键分两类铁律——**全局域键**（好友对、身份、玩家存储、钱包/账本、库存、排行投影：主体是玩家或玩家对，
/// 跨服共享、合服零操作，键内**不得**出现 ServerId 位）与**区服域键**（对局结算等区服维度数据：合服按位重写；
/// 对局标识为 <c>match-{Guid:N}</c> 全局唯一，键内无需 ServerId 位，区服维度由行实体 ServerId 快照列承载）；
/// ③ 键形变更必须递增 <see cref="RowKeySchemaVersion"/> 并走数据迁移评审（2.0 断代无存量数据，首代直接落位）。
/// </para>
/// </summary>
public static class OnlineRowKey
{
    /// <summary>
    /// 行键 schema 代际（1 = 2.0 单键键形首代；旧三元组键形随 2.0 断代退役，不作迁移）。
    /// </summary>
    public const int RowKeySchemaVersion = 1;

    /// <summary>
    /// 构造好友关系全局域键（<c>friend:{低玩家}:{高玩家}</c>；规范化无向对——好友是全局关系，
    /// 跨服加好友天然成立，合服对全局域零操作）。
    /// </summary>
    /// <param name="lowPlayerId">无向对低标识侧玩家。</param>
    /// <param name="highPlayerId">无向对高标识侧玩家。</param>
    /// <returns>物化复合键。</returns>
    public static string Friendship(long lowPlayerId, long highPlayerId)
    {
        return "friend:" + lowPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + highPlayerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 构造登录身份全局域键（<c>{身份类型}:{身份标识串}</c>；身份归玩家全局所有，合服零操作）。
    /// </summary>
    /// <param name="identityKind">身份类型（域枚举数值，调用点以 <c>(int)</c> 投影——本类不可下行引用域包枚举）。</param>
    /// <param name="identifier">身份标识串。</param>
    /// <returns>物化复合键。</returns>
    public static string Identity(int identityKind, string identifier)
    {
        return identityKind.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + identifier;
    }

    /// <summary>
    /// 构造玩家设备全局域键（<c>{游戏账号}:{设备标识}</c>）。
    /// </summary>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="deviceIdentifier">设备唯一标识串。</param>
    /// <returns>物化复合键。</returns>
    public static string PlayerDevice(long gameAccountId, string deviceIdentifier)
    {
        return gameAccountId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + deviceIdentifier;
    }

    /// <summary>
    /// 构造玩家存储条目全局域键（<c>{玩家}:{集合名}:{条目键}</c>；玩家数据跨服共享，合服零操作）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="collection">集合名。</param>
    /// <param name="key">条目键。</param>
    /// <returns>物化复合键。</returns>
    public static string PlayerStorage(long playerId, string collection, string key)
    {
        return playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + collection + ":" + key;
    }

    /// <summary>
    /// 构造玩家账本序键控序列名（<c>{玩家}</c>；账本追加式天然可并，合服零操作）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>键控序列名。</returns>
    public static string LedgerSequence(long playerId)
    {
        return playerId.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// 构造钱包账户全局域键（<c>{玩家}:{货币代码}</c>；钱包归玩家全局所有，合服零操作）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="currencyId">货币代码。</param>
    /// <returns>物化复合键。</returns>
    public static string WalletAccount(long playerId, string currencyId)
    {
        return playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + currencyId;
    }

    /// <summary>
    /// 构造库存堆叠全局域键（<c>{玩家}:{道具标识}</c>）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="itemId">道具标识。</param>
    /// <returns>物化复合键。</returns>
    public static string InventoryStack(long playerId, string itemId)
    {
        return playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + itemId;
    }

    /// <summary>
    /// 构造批次内资产去重键（<c>{玩家}:{c|i}:{资产标识}</c>；c=货币 i=道具）。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="isCurrency">是否货币类资产（true=c 货币位 / false=i 道具位；域枚举在调用点判定——本类不可下行引用域包枚举）。</param>
    /// <param name="assetId">资产标识。</param>
    /// <returns>物化复合键。</returns>
    public static string AssetDedup(long playerId, bool isCurrency, string assetId)
    {
        return playerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + (isCurrency ? "c" : "i") + ":" + assetId;
    }

    /// <summary>
    /// 构造对局结算结果区服域键（<c>{对局标识}</c>；对局标识为 <c>match-{Guid:N}</c> 全局唯一，
    /// GUID 消解跨服碰撞；区服维度由行实体 ServerId 快照列承载，合服按列过滤重写归属）。
    /// </summary>
    /// <param name="matchId">对局标识（服务端生成的全局唯一字符串）。</param>
    /// <returns>物化复合键。</returns>
    public static string MatchResult(string matchId)
    {
        return matchId;
    }
}
