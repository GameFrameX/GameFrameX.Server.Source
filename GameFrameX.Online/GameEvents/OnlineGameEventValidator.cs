// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
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

using System.Collections.Generic;
using System.Globalization;
using GameFrameX.Online.Events;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件 L0 校验器（vault:C8 S7.5 / VC-7.9）：登记表驱动的四类拒绝。
/// <para>
/// 维护约束（红线）：
/// (1) **校验依据全部来自登记表**（<see cref="OnlineGameEventSchema"/>）——事件名、版本上限、必需字段
/// 三处硬编码必然与登记表漂移，本类只做「查表 + 比对」；
/// (2) **必需字段从 <c>PayloadAuditFields</c> 读取**——那是 C93 信封的载荷语义投影，因此本校验
/// 与载荷的序列化格式无关（既不需要反序列化，也不解析兄弟事件的 JSON 载荷），
/// 上游载荷格式演进不会静默击穿本层（P1-5）；
/// (3) 校验是**只读判定**：不修改事件、不落存储；拒绝事件由摄取器送死信（<see cref="OnlineGameEventIngestor"/>）。
/// </para>
/// </summary>
public static class OnlineGameEventValidator
{
    /// <summary>
    /// 执行 L0 校验（按耗时从低到高的顺序：作用域 → 事件名 → 版本 → 必需字段）。
    /// </summary>
    /// <param name="onlineEvent">待校验的事件信封。</param>
    /// <returns>校验结果。</returns>
    public static OnlineGameEventValidationResult Validate(OnlineEvent onlineEvent)
    {
        if (onlineEvent == null)
        {
            return OnlineGameEventValidationResult.Reject(OnlineGameEventRejectionReason.UnknownEventName, "事件为空");
        }

        if (onlineEvent.TenantId <= 0 || onlineEvent.AppId <= 0)
        {
            return OnlineGameEventValidationResult.Reject(
                OnlineGameEventRejectionReason.InvalidScope,
                "事件作用域非法：TenantId=" + onlineEvent.TenantId.ToString(CultureInfo.InvariantCulture) + "，AppId=" + onlineEvent.AppId.ToString(CultureInfo.InvariantCulture));
        }

        var descriptor = OnlineGameEventSchema.Find(onlineEvent.EventType);
        if (descriptor == null)
        {
            return OnlineGameEventValidationResult.Reject(
                OnlineGameEventRejectionReason.UnknownEventName,
                "事件名未登记：" + (onlineEvent.EventType ?? string.Empty));
        }

        if (onlineEvent.SchemaVersion < 1 || onlineEvent.SchemaVersion > descriptor.CurrentVersion)
        {
            return OnlineGameEventValidationResult.Reject(
                OnlineGameEventRejectionReason.UnsupportedVersion,
                "事件版本超出登记范围：" + onlineEvent.EventType + " v" + onlineEvent.SchemaVersion.ToString(CultureInfo.InvariantCulture) + "（当前版本 " + descriptor.CurrentVersion.ToString(CultureInfo.InvariantCulture) + "）");
        }

        var missing = FindMissingField(descriptor, onlineEvent.PayloadAuditFields);
        if (missing != null)
        {
            return OnlineGameEventValidationResult.Reject(
                OnlineGameEventRejectionReason.MissingRequiredField,
                "必需字段缺失：" + onlineEvent.EventType + " 缺 " + missing);
        }

        return OnlineGameEventValidationResult.Accept();
    }

    /// <summary>
    /// 查找登记表声明的必需字段中、载荷投影里缺失或为空的第一个字段。
    /// </summary>
    /// <param name="descriptor">事件登记项。</param>
    /// <param name="auditFields">载荷语义投影（可空）。</param>
    /// <returns>缺失字段名；全部齐备返回 null。</returns>
    private static string FindMissingField(OnlineGameEventDescriptor descriptor, IReadOnlyDictionary<string, string> auditFields)
    {
        var requiredFields = descriptor.RequiredFields;
        if (requiredFields == null || requiredFields.Count == 0)
        {
            return null;
        }

        foreach (var field in requiredFields)
        {
            if (auditFields == null || !auditFields.TryGetValue(field, out var value) || string.IsNullOrEmpty(value))
            {
                return field;
            }
        }

        return null;
    }
}
