// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses!
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the full license text in the root directory's LICENSE file.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担任何责任。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Social;

using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 好友关系持久化行（Social 域 → GameDb 适配的落库形态）。
/// <para>
/// 键编码约定（物化复合键）：<see cref="UniqueKey"/> =
/// "friend:{TenantId}:{AppId}:{LowPlayerId}:{HighPlayerId}"（作用域 + 规范化无向对，与 InMemory
/// <c>BuildPairKey</c> 同构），单列唯一索引承接「一对玩家至多一条关系记录」的结构性不变量。
/// 行 Id 无自然主键，由 <c>NextSequenceAsync</c> 分配；业务字段保留为独立列供等值/归属查询；
/// 状态以枚举数值落库。
/// </para>
/// </summary>
public sealed class OnlineFriendshipRowState : BaseCacheState
{
    /// <summary>
    /// 获取或设置物化复合键（"friend:{TenantId}:{AppId}:{LowPlayerId}:{HighPlayerId}"；唯一索引列）。
    /// </summary>
    [EntityIndex("ux_online_friendship_unique_key", Unique = true)]
    public string UniqueKey { get; set; }

    /// <summary>
    /// 获取或设置关系标识（服务端生成的字符串 Id，定位 + 回读保真）。
    /// </summary>
    public string FriendshipId { get; set; }

    /// <summary>
    /// 获取或设置租户标识（数据隔离根边界）。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置 App 标识（数据隔离根边界）。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置关系建立时的来源区服标识（仅审计参考，不参与唯一键）。
    /// </summary>
    public long ServerId { get; set; }

    /// <summary>
    /// 获取或设置无向对的低标识侧玩家（规范化结果）。
    /// </summary>
    public long LowPlayerId { get; set; }

    /// <summary>
    /// 获取或设置无向对的高标识侧玩家（规范化结果）。
    /// </summary>
    public long HighPlayerId { get; set; }

    /// <summary>
    /// 获取或设置发起方玩家标识（方向事实；关系被接受后不翻转）。
    /// </summary>
    public long RequesterId { get; set; }

    /// <summary>
    /// 获取或设置被请求方玩家标识（方向事实）。
    /// </summary>
    public long AddresseeId { get; set; }

    /// <summary>
    /// 获取或设置关系状态（枚举数值落库）。
    /// </summary>
    public int State { get; set; }

    /// <summary>
    /// 获取或设置创建时刻（Unix 毫秒）。
    /// </summary>
    public long CreatedAtTime { get; set; }

    /// <summary>
    /// 获取或设置最近更新时刻（Unix 毫秒）。
    /// </summary>
    public long UpdatedAtTime { get; set; }

    /// <summary>
    /// 获取或设置待答复请求的失效时刻（Unix 毫秒）。
    /// </summary>
    public long ExpiresAtTime { get; set; }

    /// <summary>
    /// 获取或设置最近一次答复时刻（Unix 毫秒；未答复为 0）。
    /// </summary>
    public long RespondedAtTime { get; set; }
}
