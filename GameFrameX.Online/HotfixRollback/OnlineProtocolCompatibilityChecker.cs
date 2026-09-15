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

using System;
using System.Collections.Generic;

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 回滚协议兼容检查器（vault:C9 S8.5 / VC-8.9「协议兼容检查通过」的检查钩子——纯函数，
/// 不触存储、不写审计，供 <see cref="OnlineHotfixRollbackService"/> 回滚闸门与只读预检共用）。
/// <para>
/// 判定规则（维护约束，调整须回 vault 契约评审）：
/// <b>RemovedMessage</b>——当前（活跃）版本有、目标（回滚）版本无：回滚丢失既有协议面，
/// 已升级客户端流量会被拒，<b>不兼容拒绝回滚</b>；
/// <b>ChangedMessage</b>——同名不同消息号 / 同消息号不同名：路由错乱，<b>不兼容拒绝回滚</b>；
/// <b>AddedMessage</b>——目标版本独有的消息行：回滚恢复旧处理器，无害，<b>兼容</b>，差异入报告提示。
/// 比对粒度 = 消息名 + 消息号（<see cref="OnlineHotfixProtocolMessage"/>）；proto 字段级签名兼容超出本层可见性，
/// 为升级路径（届时清单行扩展字段签名，本检查器逐字段比对）。
/// </para>
/// </summary>
public static class OnlineProtocolCompatibilityChecker
{
    /// <summary>
    /// 比对当前（活跃）版本清单与目标（回滚）版本清单，产出兼容检查报告。
    /// </summary>
    /// <param name="current">当前活跃版本清单。</param>
    /// <param name="target">目标回滚版本清单。</param>
    /// <returns>兼容检查报告（含三类差异行与结论；两清单协议面一致时差异为空且兼容）。</returns>
    public static OnlineProtocolCompatibilityReport Check(OnlineHotfixProtocolManifest current, OnlineHotfixProtocolManifest target)
    {
        if (current == null)
        {
            throw new ArgumentNullException(nameof(current));
        }

        if (target == null)
        {
            throw new ArgumentNullException(nameof(target));
        }

        var currentByName = BuildNameIndex(current);
        var currentById = BuildIdIndex(current);
        var targetByName = BuildNameIndex(target);
        var targetById = BuildIdIndex(target);
        var issues = new List<OnlineProtocolCompatibilityIssue>();

        // 当前 → 目标方向：判 Removed（当前有目标无）与 Changed（同名不同号 / 同号不同名）。
        foreach (var currentMessage in current.Messages ?? Array.Empty<OnlineHotfixProtocolMessage>())
        {
            if (currentMessage == null)
            {
                continue;
            }

            if (targetByName.TryGetValue(currentMessage.MessageName, out var targetMessageId))
            {
                if (targetMessageId != currentMessage.MessageId)
                {
                    issues.Add(new OnlineProtocolCompatibilityIssue
                    {
                        Kind = OnlineProtocolCompatibilityIssue.IssueKind.ChangedMessage,
                        MessageName = currentMessage.MessageName,
                        CurrentMessageId = currentMessage.MessageId,
                        TargetMessageId = targetMessageId,
                        Description = "消息名 " + currentMessage.MessageName + " 的消息号由 " + currentMessage.MessageId + " 变为 " + targetMessageId + "（路由错乱）",
                    });
                }

                continue;
            }

            if (targetById.TryGetValue(currentMessage.MessageId, out var renamedTarget))
            {
                issues.Add(new OnlineProtocolCompatibilityIssue
                {
                    Kind = OnlineProtocolCompatibilityIssue.IssueKind.ChangedMessage,
                    MessageName = currentMessage.MessageName,
                    CurrentMessageId = currentMessage.MessageId,
                    TargetMessageId = currentMessage.MessageId,
                    Description = "消息号 " + currentMessage.MessageId + " 的消息名由 " + currentMessage.MessageName + " 变为 " + renamedTarget + "（路由错乱）",
                });
                continue;
            }

            issues.Add(new OnlineProtocolCompatibilityIssue
            {
                Kind = OnlineProtocolCompatibilityIssue.IssueKind.RemovedMessage,
                MessageName = currentMessage.MessageName,
                CurrentMessageId = currentMessage.MessageId,
                TargetMessageId = 0,
                Description = "消息 " + currentMessage.MessageName + "(" + currentMessage.MessageId + ") 在目标版本不存在（回滚丢失既有协议面）",
            });
        }

        // 目标 → 当前方向：判 Added（目标独有——回滚恢复旧处理器，兼容入报告提示）。
        // 消息号在当前版本已占位的行不算 Added（那是改名差异，已在上方按 Changed 报告）。
        foreach (var targetMessage in target.Messages ?? Array.Empty<OnlineHotfixProtocolMessage>())
        {
            if (targetMessage == null)
            {
                continue;
            }

            if (!currentByName.ContainsKey(targetMessage.MessageName) && !currentById.ContainsKey(targetMessage.MessageId))
            {
                issues.Add(new OnlineProtocolCompatibilityIssue
                {
                    Kind = OnlineProtocolCompatibilityIssue.IssueKind.AddedMessage,
                    MessageName = targetMessage.MessageName,
                    CurrentMessageId = 0,
                    TargetMessageId = targetMessage.MessageId,
                    Description = "消息 " + targetMessage.MessageName + "(" + targetMessage.MessageId + ") 为目标版本独有（回滚恢复旧处理器，无害）",
                });
            }
        }

        return new OnlineProtocolCompatibilityReport(issues);
    }

    /// <summary>
    /// 构造清单的消息名索引（首个出现为准——同名重复行由登记处校验拒绝，此处防御性取首行）。
    /// </summary>
    /// <param name="manifest">清单。</param>
    /// <returns>消息名 → 消息号。</returns>
    private static Dictionary<string, int> BuildNameIndex(OnlineHotfixProtocolManifest manifest)
    {
        var index = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var message in manifest.Messages ?? Array.Empty<OnlineHotfixProtocolMessage>())
        {
            if (message == null || string.IsNullOrEmpty(message.MessageName))
            {
                continue;
            }

            if (!index.ContainsKey(message.MessageName))
            {
                index[message.MessageName] = message.MessageId;
            }
        }

        return index;
    }

    /// <summary>
    /// 构造清单的消息号索引（首个出现为准——同号重复行由登记处校验拒绝，此处防御性取首行）。
    /// </summary>
    /// <param name="manifest">清单。</param>
    /// <returns>消息号 → 消息名。</returns>
    private static Dictionary<int, string> BuildIdIndex(OnlineHotfixProtocolManifest manifest)
    {
        var index = new Dictionary<int, string>();
        foreach (var message in manifest.Messages ?? Array.Empty<OnlineHotfixProtocolMessage>())
        {
            if (message == null || message.MessageId <= 0)
            {
                continue;
            }

            if (!index.ContainsKey(message.MessageId))
            {
                index[message.MessageId] = message.MessageName;
            }
        }

        return index;
    }
}
