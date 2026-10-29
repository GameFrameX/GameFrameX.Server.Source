//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file at the source code root for the full license text.
//   禁止利用本项目代码实施任何危害国家安全、破坏社会秩序、
//   侵犯其他个人或组织合法权益等法律法规禁止的行为！
//  ==========================================================================================

namespace GameFrameX.Capability.Online.Persistence.Match;

using System.Collections.Generic;
using GameFrameX.Capability.Online.Match;
using GameFrameX.DataBase;
using GameFrameX.Foundation.Orm.Attribute;

/// <summary>
/// 结算结果持久化行（Match 域 → GameDb 适配的落库形态）。
/// <para>
/// 键编码约定：<see cref="UniqueKey"/> = 物化复合键（"tenant:app:matchId"，与 InMemory 实现同构），
/// 单列唯一索引承接 (TenantId, AppId, MatchId) 唯一性——「同一对局首次结果胜出」由
/// <c>TryAddUniqueAsync</c> 原语在数据库层担保。
/// </para>
/// </summary>
public sealed class OnlineMatchResultRowState : BaseCacheState
{
    /// <summary>
    /// 获取或设置物化复合键（"tenant:app:matchId"；唯一索引列）。
    /// </summary>
    [EntityIndex("ux_online_match_result_unique_key", Unique = true)]
    public string UniqueKey { get; set; }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId { get; set; }

    /// <summary>
    /// 获取或设置应用标识。
    /// </summary>
    public long AppId { get; set; }

    /// <summary>
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId { get; set; }

    /// <summary>
    /// 获取或设置结算结果文档（JSON 落库；回读侧返回副本）。
    /// </summary>
    public OnlineMatchResult Result { get; set; }
}
