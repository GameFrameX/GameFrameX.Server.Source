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
/// 处罚记录（vault:C7 S6.3 / 方案复审 P0-2：管理员施加的禁言与封禁，含生效区间与作用域）。
/// <para>
/// 维护约束：处罚是**带生效区间的独立记录**，不是挂在玩家档案上的一个布尔标志——
/// 布尔标志无法表达「多久解封」「为何封禁」「是哪一单举报导致的」，也无法留痕复查。
/// 生效判定唯一入口是 <see cref="IsActiveAt"/>，裁决服务不得自行拼装时间比较条件；
/// 撤销是**原位标记**（<see cref="Revoked"/>）而非删除记录，保证审计链完整。
/// </para>
/// <para>
/// 天花板（ponytail）：一次处罚只作用于单个玩家、单一 App 作用域；跨服封禁与账号级
/// 全局封禁需扩展作用域字段，当前量级不需要。
/// </para>
/// </summary>
public sealed class OnlinePunishment
{
    /// <summary>
    /// 获取或设置处罚标识。
    /// </summary>
    public string PunishmentId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被处罚玩家。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置处罚种类。
    /// </summary>
    public OnlinePunishmentKind Kind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置处罚原因（Admin 填写，必填——无原因的封禁不可复查）。
    /// </summary>
    public string Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置生效时刻（UTC 毫秒；可设为未来时刻以预约处罚）。
    /// </summary>
    public long EffectiveAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失效时刻（UTC 毫秒；<c>0</c> 表示永久处罚）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否已被撤销。
    /// </summary>
    public bool Revoked
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置撤销时刻（UTC 毫秒；未撤销为 <c>0</c>）。
    /// </summary>
    public long RevokedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置执行撤销的 Admin 标识（未撤销为 <c>0</c>）。
    /// </summary>
    public long RevokedByAdminId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置施加处罚的 Admin 标识。
    /// </summary>
    public long CreatedByAdminId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源案件标识（Admin 侧案件编号；非举报来源为空字符串）。
    /// </summary>
    public string AdminCaseId
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
    /// 获取或设置最后变更时刻（UTC 毫秒）。
    /// </summary>
    public long UpdatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置版本号（存储层 CAS 判据，随每次写入自增）。
    /// </summary>
    public int Revision
    {
        get;
        set;
    }

    /// <summary>
    /// 判定处罚在给定时刻是否生效（生效区间 + 未撤销的唯一判据）。
    /// </summary>
    /// <param name="nowUnixMilliseconds">判定时刻（UTC 毫秒）。</param>
    /// <returns>生效返回 <c>true</c>；未到生效时刻、已失效或已撤销返回 <c>false</c>。</returns>
    public bool IsActiveAt(long nowUnixMilliseconds)
    {
        if (Revoked)
        {
            return false;
        }

        if (EffectiveAtTime > nowUnixMilliseconds)
        {
            return false;
        }

        return ExpiresAtTime <= 0 || ExpiresAtTime > nowUnixMilliseconds;
    }

    /// <summary>
    /// 构造深拷贝（存储层与调用方不得共享同一实例）。
    /// </summary>
    /// <returns>字段级拷贝。</returns>
    public OnlinePunishment Copy()
    {
        return new OnlinePunishment
        {
            PunishmentId = PunishmentId,
            TenantId = TenantId,
            AppId = AppId,
            PlayerId = PlayerId,
            Kind = Kind,
            Reason = Reason,
            EffectiveAtTime = EffectiveAtTime,
            ExpiresAtTime = ExpiresAtTime,
            Revoked = Revoked,
            RevokedAtTime = RevokedAtTime,
            RevokedByAdminId = RevokedByAdminId,
            CreatedByAdminId = CreatedByAdminId,
            AdminCaseId = AdminCaseId,
            CreatedAtTime = CreatedAtTime,
            UpdatedAtTime = UpdatedAtTime,
            Revision = Revision,
        };
    }
}
