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
/// 玩家静音条目（vault:C7 S6.3：静音是**展示层**偏好，不是互动拒绝）。
/// <para>
/// 维护约束：静音只影响本人客户端的呈现（不提示、不弹出、不进会话列表），**绝不参与任何拒绝裁决**——
/// 把静音当成屏蔽会让「我暂时不想看他的消息」升级为「他发不了消息给我」，
/// 单方面剥夺他人互动能力。二者的分野由 <see cref="OnlineSocialDecisionService"/> 统一承担：
/// 屏蔽参与裁决，静音不参与。
/// </para>
/// </summary>
public sealed class OnlineMuteEntry
{
    /// <summary>
    /// 获取或设置本条记录标识（**每次写入调用**新铸一个，非业务键）。
    /// <para>
    /// 业务唯一键是「归属玩家 + 被静音玩家」，存储层按它做「不存在则创建」；本标识因此只在
    /// 「并发重复静音时判定谁真正落库」这一处使用——拿到既有记录的一方据标识不同即可断定自己不是写入者，
    /// 从而不重复外发变更事件。不用「创建时刻是否等于本地时钟」当判据：同一毫秒内的两次调用会双双判定成功。
    /// </para>
    /// </summary>
    public string EntryId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置静音发起人（本条记录的归属玩家）。
    /// </summary>
    public long OwnerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置被静音玩家。
    /// </summary>
    public long MutedPlayerId
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
    /// 获取或设置发起静音时所在的区服标识（仅作事件信封与审计记录用；**不参与存储键**）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置静音失效时刻（UTC 毫秒；<c>0</c> 表示永久静音）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置静音生效时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 判定静音在给定时刻是否仍然有效。
    /// </summary>
    /// <param name="nowUnixMilliseconds">判定时刻（UTC 毫秒）。</param>
    /// <returns>有效返回 <c>true</c>；已超期返回 <c>false</c>。</returns>
    public bool IsEffectiveAt(long nowUnixMilliseconds)
    {
        return ExpiresAtTime <= 0 || ExpiresAtTime > nowUnixMilliseconds;
    }

    /// <summary>
    /// 构造深拷贝（存储层与调用方不得共享同一实例）。
    /// </summary>
    /// <returns>字段级拷贝。</returns>
    public OnlineMuteEntry Copy()
    {
        return new OnlineMuteEntry
        {
            EntryId = EntryId,
            OwnerId = OwnerId,
            MutedPlayerId = MutedPlayerId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            ExpiresAtTime = ExpiresAtTime,
            CreatedAtTime = CreatedAtTime,
        };
    }
}
