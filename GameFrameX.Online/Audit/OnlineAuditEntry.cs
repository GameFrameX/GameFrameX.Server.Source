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
/// 统一审计接入条目（vault:C9 S8.2：Admin 既有支付/奖励/邮件/兑换码/远程配置/处罚审计
/// 与 S8.1 受控操作审计接入统一链路的**接入面**模型；字段 = 审计语义字段 + C93 事件信封字段的超集）。
/// <para>
/// 维护约束（红线）：
/// ① <see cref="EventId"/> 全局唯一（C93 契约去重键，重复接入幂等回执不重复落档，VC-8.4）；
/// ② <see cref="OperatorId"/> 与 <see cref="Reason"/> 必填——「审计完整含操作者/原因/时间」（VC-8.3），
/// 缺失在 <c>OnlineAuditService.IngestAsync</c> 校验处拒绝（宁拒毋缺）；
/// ③ <see cref="PayloadAuditFields"/> 是载荷的语义字段投影，落档前经 C93 <c>OnlineEventSanitizer</c> 脱敏
/// （VC-8.16：存储与检索结构性无明文敏感值），**不得**把令牌/口令/手机号等敏感值放入非敏感键下绕过脱敏；
/// ④ <see cref="ServerId"/> 为 0 表示 App 级操作（支付、远程配置等无区服维度的审计），
/// <see cref="PlayerId"/> 为 0 表示系统级（非玩家主体）操作——对齐 C93 信封作用域语义。
/// </para>
/// </summary>
public sealed class OnlineAuditEntry
{
    /// <summary>
    /// 获取或设置审计域（<see cref="OnlineAuditDomain"/> 七值之一；未知域接入被拒）。
    /// </summary>
    public string Domain
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置全局唯一审计标识（幂等去重键）。受控操作审计应从操作幂等键**确定性派生**
    /// （如 <c>audit-{操作幂等键}</c>，对齐 C102/C103 幂等键派生先例）——重复执行同一命令命中同一条审计，
    /// 与接入侧幂等共同构成 VC-8.4 半边；随机派生的标识会让重复执行产生多条审计记录。
    /// </summary>
    public string EventId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置事件/操作类型（复用来源域既有事件常量或 Admin 审计动作名；如 <c>online.session.kicked</c>）。
    /// </summary>
    public string EventType
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置审计业务时刻（UTC 毫秒；VC-8.3 审计三要素之一——时间）。
    /// </summary>
    public long OccurredTime
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置租户标识（必填，必须大于 0）。
    /// </summary>
    public long TenantId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置 App 标识（必填，必须大于 0）。
    /// </summary>
    public long AppId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置区服标识（0 表示 App 级操作——支付、远程配置等无区服维度的审计）。
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
    /// 获取或设置操作者标识（VC-8.3 审计三要素之一——操作者；必填，通常为管理员账号标识）。
    /// </summary>
    public string OperatorId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作者显示名（审计检索的辅助定位信息；可为空，定位以 <see cref="OperatorId"/> 为准）。
    /// </summary>
    public string OperatorName
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置操作原因（VC-8.3 审计三要素之一——原因；必填，受控操作的业务理由留痕）。
    /// </summary>
    public string Reason
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置来源模块标识（如 <c>online-admin</c>；对齐 C93 信封 <c>Source</c> 语义）。
    /// </summary>
    public string Source
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置关联链路键（VC-8.5 可定位；未参与链路时可为空）。
    /// </summary>
    public string CorrelationId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置载荷语义字段投影（键值对；落档前经 C93 脱敏器生成 <c>SanitizedFields</c>，敏感键值替换为掩码）。
    /// </summary>
    public IReadOnlyDictionary<string, string> PayloadAuditFields
    {
        get;
        set;
    }
}
