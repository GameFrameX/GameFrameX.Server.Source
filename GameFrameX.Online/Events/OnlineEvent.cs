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

namespace GameFrameX.Online.Events;

/// <summary>
/// Online 事件信封（vault:C2 S1.6 事件契约：EventId/EventType/OccurredAt/SchemaVersion +
/// TenantId/AppId/ServerId/PlayerId + Source/CorrelationId/Payload）。
/// <para>
/// 维护约束：事件代表已经发生的事实，不作为当前状态的唯一存储（状态由 Actor 持有）；
/// <c>EventId</c> 全局唯一（消费端去重键，VC-1.13）；<c>CorrelationId</c> 贯穿业务链路（VC-1.14）；
/// 作用域字段为 Online 领域语义，经 <c>OnlineEventEnvelopeMapper</c> 以 Foundation 信封 Attributes 稳定键承载
/// （红线：Online 领域语义不进 Foundation）。<c>PayloadAuditFields</c> 是载荷的语义字段投影，
/// 仅供审计视图脱敏使用，不参与信封传输。
/// </para>
/// </summary>
public sealed class OnlineEvent
{
    /// <summary>
    /// 获取或设置全局唯一事件标识（消费端去重键）。
    /// </summary>
    public string EventId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件类型（调用方命名空间字符串，如 <c>online.session.created</c>）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件发生时刻（UTC 毫秒）。
    /// </summary>
    public long OccurredTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置载荷结构版本（不小于 1；结构演进时递增）。
    /// </summary>
    public int SchemaVersion
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（Online 作用域字段）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（Online 作用域字段）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（Online 作用域字段）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家标识（Online 作用域字段；系统级事件为 0）。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源模块标识（如 <c>online-identity</c>）。
    /// </summary>
    public string Source
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联链路键（未参与链路时为 null）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件载荷字节（调用方序列化契约产物）。
    /// </summary>
    public ReadOnlyMemory<byte> Payload
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置载荷语义字段投影（键值对；仅用于审计视图脱敏，不进入传输信封）。
    /// </summary>
    public IReadOnlyDictionary<string, string> PayloadAuditFields
    {
        get;
        set;
    }
}
