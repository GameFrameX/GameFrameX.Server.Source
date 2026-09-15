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
/// 通知聚合（vault:C7 S6.8：一条通知对应一次投递意图，接收者是唯一属主）。
/// <para>
/// 维护约束（红线）：
/// ① **去重键的唯一范围是「作用域 + 接收者 + <see cref="DedupeKey"/>」**——同一去重键对不同接收者是
/// 两条合法通知（一次业务动作通知多人），唯一性由存储层落实，服务层不得先查后建（VC-6.12）；
/// ② <see cref="Payload"/> 是来源域序列化的载荷文本，通知域**视为不透明**——不解析、不改写、不据此判业务，
/// 只在推送时原样透传；正文可能含敏感信息，故不入事件载荷（沿用 C93 脱敏要求）；
/// ③ <see cref="ExpiresAtTime"/> 为 0 表示**永不失效**（不设有效期；超期扫描不碰这类记录，VC-6.13）；
/// ④ <see cref="State"/> 只能经 <see cref="OnlineNotificationStateMachine"/> 迁移；
/// ⑤ <see cref="AttemptCount"/> 只增不减、重投不清零——上限判据是累计尝试数，清零会让失败通知无限重投。
/// </para>
/// </summary>
public sealed class OnlineNotification
{
    /// <summary>
    /// 获取或设置通知标识（全局唯一，格式 <c>ntf-{32 位十六进制}</c>）。
    /// </summary>
    public string NotificationId
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
    /// 获取或设置通知产生时的来源区服标识（仅审计与投递路由参考，不参与唯一键）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置接收者玩家标识（通知的唯一属主；仅本人可读、可补发）。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置通知来源分类。
    /// </summary>
    public OnlineNotificationKind Kind
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置去重键（来源域给出的业务幂等键，如 <c>invite:{inviteId}</c>）。
    /// <para>
    /// 唯一范围是「作用域 + 接收者 + 本字段」：同一去重键重复入队只落一条通知，重复请求直接返回既有记录
    /// （VC-6.12 的收敛点）。
    /// </para>
    /// </summary>
    public string DedupeKey
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置载荷（来源域提供的 JSON 文本，通知域视为不透明，推送时原样透传）。
    /// </summary>
    public string Payload
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失效时刻（UTC 毫秒；&lt;= 0 表示永不失效，不参与超期扫描）。
    /// </summary>
    public long ExpiresAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置通知状态。
    /// </summary>
    public OnlineNotificationState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置累计推送尝试次数（含首次；只增不减）。
    /// </summary>
    public int AttemptCount
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置最近一次推送失败原因（成功时为空字符串；重试与排障的唯一依据）。
    /// </summary>
    public string LastError
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
    /// 获取或设置投递成功时刻（UTC 毫秒；未投递为 0）。
    /// </summary>
    public long DeliveredAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家已读时刻（UTC 毫秒；未读为 0）。
    /// </summary>
    public long ReadAtTime
    {
        get;
        set;
    }

    /// <summary>
    /// 生成防御性深拷贝（存储实现按「持久化行」语义返回副本，调用方改动不回写存储）。
    /// <para>
    /// 服务层的写路径统一是「取副本 → 在副本上改 → CAS 回写」，禁止直改读取到的实例。
    /// </para>
    /// </summary>
    /// <returns>通知副本。</returns>
    public OnlineNotification Copy()
    {
        return new OnlineNotification
        {
            NotificationId = NotificationId,
            TenantId = TenantId,
            AppId = AppId,
            ServerId = ServerId,
            PlayerId = PlayerId,
            Kind = Kind,
            DedupeKey = DedupeKey,
            Payload = Payload,
            ExpiresAtTime = ExpiresAtTime,
            State = State,
            AttemptCount = AttemptCount,
            LastError = LastError,
            CreatedAtTime = CreatedAtTime,
            UpdatedAtTime = UpdatedAtTime,
            DeliveredAtTime = DeliveredAtTime,
            ReadAtTime = ReadAtTime,
        };
    }
}
