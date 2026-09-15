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
//   Any disputes or liabilities arising from secondary development based on this project
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

using System.Collections.Generic;

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计落档记录（<c>OnlineAuditService.IngestAsync</c> 落入 <c>IOnlineAuditStore</c> 的**持久化行**；
/// 字段 = C93 <c>OnlineEventAuditView</c> 元数据投影 + 审计语义字段 域/操作者/原因）。
/// <para>
/// 维护约束（红线）：记录**只追加、不修改、不淘汰**（审计不可丢）；
/// <see cref="SanitizedFields"/> 是接入时经 C93 <c>OnlineEventSanitizer</c> 脱敏后的字段投影，
/// 记录自落档起即不含明文敏感值（VC-8.16 结构性保证），检索消费方无需二次脱敏；
/// 原始载荷字节不落档（审计链路只消费语义投影，对齐 C93 审计视图约束）。
/// </para>
/// </summary>
public sealed class OnlineAuditRecord
{
    /// <summary>
    /// 获取或设置全局唯一审计标识（幂等去重键；与接入条目 <c>EventId</c> 一致）。
    /// </summary>
    public string EventId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置审计域（<see cref="OnlineAuditDomain"/> 七值之一）。
    /// </summary>
    public string Domain
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件/操作类型（接入时的来源域事件常量或 Admin 审计动作名）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置审计业务时刻（UTC 毫秒；VC-8.3 审计三要素之一——时间；跨域检索全序的第一键）。
    /// </summary>
    public long OccurredTime
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
    /// 获取或设置区服标识（0 表示 App 级操作）。
    /// </summary>
    public long ServerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置玩家标识（0 表示系统级/非玩家主体操作）。
    /// </summary>
    public long PlayerId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者标识（VC-8.3 审计三要素之一——操作者；检索可按操作者过滤定位）。
    /// </summary>
    public string OperatorId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者显示名（辅助定位信息；定位以 <see cref="OperatorId"/> 为准）。
    /// </summary>
    public string OperatorName
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作原因（VC-8.3 审计三要素之一——原因）。
    /// </summary>
    public string Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源模块标识。
    /// </summary>
    public string Source
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联链路键（VC-8.5 可定位检索入口）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置脱敏后的载荷字段投影（敏感键值已替换为掩码；原始载荷字节不落档）。
    /// </summary>
    public IReadOnlyDictionary<string, string> SanitizedFields
    {
        get;
        set;
    }
}
