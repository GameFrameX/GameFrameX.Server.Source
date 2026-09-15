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

namespace GameFrameX.Online.HotfixRollback;

/// <summary>
/// Hotfix 回滚协议兼容检查报告（<see cref="OnlineProtocolCompatibilityChecker.Check"/> 的产物；
/// 含不兼容差异即 <see cref="IsCompatible"/> = <see langword="false"/>，回滚闸门据此拒绝）。
/// <para>
/// 维护约束：<see cref="RemovedCount"/> 与 <see cref="ChangedCount"/> 任一大于 0 即不兼容
/// （<see cref="AddedCount"/> 恒兼容）；三个计数须与 <see cref="Issues"/> 逐类一致。
/// </para>
/// </summary>
public sealed class OnlineProtocolCompatibilityReport
{
    /// <summary>
    /// 初始化 <see cref="OnlineProtocolCompatibilityReport"/>。
    /// </summary>
    /// <param name="issues">差异行集合（三类差异全量，含兼容差异）。</param>
    public OnlineProtocolCompatibilityReport(IReadOnlyList<OnlineProtocolCompatibilityIssue> issues)
    {
        Issues = issues ?? new List<OnlineProtocolCompatibilityIssue>();
        foreach (var issue in Issues)
        {
            if (issue == null)
            {
                continue;
            }

            switch (issue.Kind)
            {
                case OnlineProtocolCompatibilityIssue.IssueKind.RemovedMessage:
                    RemovedCount++;
                    break;
                case OnlineProtocolCompatibilityIssue.IssueKind.ChangedMessage:
                    ChangedCount++;
                    break;
                case OnlineProtocolCompatibilityIssue.IssueKind.AddedMessage:
                    AddedCount++;
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// 获取差异行集合（三类差异全量；无差异返回空集合）。
    /// </summary>
    public IReadOnlyList<OnlineProtocolCompatibilityIssue> Issues
    {
        get;
    }

    /// <summary>
    /// 获取 Removed 差异数（不兼容差异）。
    /// </summary>
    public int RemovedCount
    {
        get;
    }

    /// <summary>
    /// 获取 Changed 差异数（不兼容差异）。
    /// </summary>
    public int ChangedCount
    {
        get;
    }

    /// <summary>
    /// 获取 Added 差异数（兼容差异）。
    /// </summary>
    public int AddedCount
    {
        get;
    }

    /// <summary>
    /// 获取是否兼容（Removed 与 Changed 均为 0 才允许回滚）。
    /// </summary>
    public bool IsCompatible
    {
        get
        {
            return RemovedCount == 0 && ChangedCount == 0;
        }
    }
}
