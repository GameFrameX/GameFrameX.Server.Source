//  ==========================================================================================
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

namespace GameFrameX.Online.Social;

/// <summary>
/// 好友关系聚合（vault:C7 S6.2：一对玩家一条记录，方向由 <see cref="RequesterId"/> 表达）。
/// <para>
/// 维护约束（红线）：
/// ① **唯一键是无向对**——<see cref="LowPlayerId"/>/<see cref="HighPlayerId"/> 由
/// <see cref="Canonicalize"/> 规范化（低标识在前），A→B 与 B→A 命中同一条记录，
/// 因此「重复请求」在存储层就无第二条可落（VC-6.1）；
/// ② 本类型不承载作用域内的 ServerId 唯一性——好友关系是跨区服成立的，键为 Tenant + App + 无向对，
/// 不含 ServerId（与 C94「键 = Tenant+App+Player，不含 Server」一致）；<see cref="ServerId"/> 仅记录
/// 关系建立时的来源区服，供审计与投递路由参考；
/// ③ <see cref="State"/> 只能经 <see cref="OnlineFriendshipStateMachine"/> 迁移；
/// ④ <see cref="RequesterId"/> 是方向事实，关系被接受后**不翻转**——「谁先发起」是审计语义，
/// 消费方判断「我发起的请求」用 <see cref="RequesterId"/> 对比自身标识。
/// </para>
/// </summary>
public sealed class OnlineFriendship
{
    /// <summary>
    /// 获取或设置关系标识。
    /// </summary>
    public string FriendshipId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（数据隔离根边界）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（数据隔离根边界）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关系建立时的来源区服标识（仅审计与投递路由参考，不参与唯一键）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置无向对的低标识侧玩家（规范化结果，恒有 <c>LowPlayerId &lt; HighPlayerId</c>）。
    /// </summary>
    public long LowPlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置无向对的高标识侧玩家（规范化结果）。
    /// </summary>
    public long HighPlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置发起方玩家标识（方向事实；关系被接受后不翻转）。
    /// </summary>
    public long RequesterId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被请求方玩家标识（方向事实）。
    /// </summary>
    public long AddresseeId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关系状态。
    /// </summary>
    public OnlineFriendshipState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近更新时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置待答复请求的失效时刻（UTC 毫秒；超期由扫描置 <see cref="OnlineFriendshipState.Expired"/>）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次答复时刻（UTC 毫秒；未答复为 0）。
    /// </summary>
    public long RespondedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 规范化玩家对为无向键（低标识在前）。
    /// <para>
    /// 该约定与既有 <c>FriendRelationState</c> 的 <c>PlayerIdA &lt; PlayerIdB</c> 不变量一致——
    /// 既有实现已用「无向对 + 软删状态」表达好友关系，本 change 沿用同一约定，不另立规约。
    /// </para>
    /// </summary>
    /// <param name="leftPlayerId">一侧玩家标识。</param>
    /// <param name="rightPlayerId">另一侧玩家标识。</param>
    /// <param name="lowPlayerId">规范化后的低标识侧。</param>
    /// <param name="highPlayerId">规范化后的高标识侧。</param>
    public static void Canonicalize(long leftPlayerId, long rightPlayerId, out long lowPlayerId, out long highPlayerId)
    {
        if (leftPlayerId <= rightPlayerId)
        {
            lowPlayerId = leftPlayerId;
            highPlayerId = rightPlayerId;
            return;
        }

        lowPlayerId = rightPlayerId;
        highPlayerId = leftPlayerId;
    }

    /// <summary>
    /// 判定指定玩家是否为本关系的其中一方。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>是其中一方返回 <c>true</c>。</returns>
    public bool Contains(long playerId)
    {
        return LowPlayerId == playerId || HighPlayerId == playerId;
    }

    /// <summary>
    /// 取出本关系中「除指定玩家之外」的另一方。
    /// </summary>
    /// <param name="playerId">本关系中的一方玩家标识。</param>
    /// <returns>另一方玩家标识；指定玩家不在本关系中时返回 0。</returns>
    public long OtherOf(long playerId)
    {
        if (LowPlayerId == playerId)
        {
            return HighPlayerId;
        }

        if (HighPlayerId == playerId)
        {
            return LowPlayerId;
        }

        return 0;
    }

    /// <summary>
    /// 生成防御性深拷贝（存储实现按「持久化行」语义返回副本，调用方改动不回写存储）。
    /// </summary>
    /// <returns>关系副本。</returns>
    public OnlineFriendship Copy()
    {
        return new OnlineFriendship
        {
            FriendshipId = FriendshipId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            LowPlayerId = LowPlayerId,
            HighPlayerId = HighPlayerId,
            RequesterId = RequesterId,
            AddresseeId = AddresseeId,
            State = State,
            CreatedAtTime = CreatedAtTime,
            UpdatedAtTime = UpdatedAtTime,
            ExpiresAtTime = ExpiresAtTime,
            RespondedAtTime = RespondedAtTime,
        };
    }
}
