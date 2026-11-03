// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Online.ServerLifecycle.ServerMerge;

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 内置参与者：开服锚点合服标记（源服锚点 <c>MergedIntoServerId</c> 置目标服；幂等）。
/// <para>
/// 前置全量校验（dry-run 与真实执行共用）：目标服锚点已合入其他服（已退役）、任一源服锚点未登记或已合入其他目标即整体拒绝
/// （<see cref="OnlineErrorCode.StateOperationForbidden"/>）——dry-run 即暴露执行期失败，
/// 真实执行不会出现部分源服已标记的半途状态。
/// </para>
/// </summary>
public sealed class OnlineServerAnchorMergeParticipant : IServerMergeParticipant
{
    /// <summary>
    /// 开服锚点存储。
    /// </summary>
    private readonly IOnlineServerCalendarStore _calendarStore;

    /// <summary>
    /// 初始化参与者。
    /// </summary>
    /// <param name="calendarStore">开服锚点存储。</param>
    public OnlineServerAnchorMergeParticipant(IOnlineServerCalendarStore calendarStore)
    {
        _calendarStore = calendarStore ?? throw new ArgumentNullException(nameof(calendarStore));
    }

    /// <inheritdoc />
    public string Name
    {
        get { return "server-anchor"; }
    }

    /// <inheritdoc />
    public async Task<OnlineServerMergeStepReport> MergeAsync(OnlineServerMergeContext context, CancellationToken cancellationToken)
    {
        var details = new Dictionary<string, long>();
        long affected = 0;

        var targetAnchor = await _calendarStore.FindAsync(context.Plan.TargetServerId, cancellationToken).ConfigureAwait(false);
        if (targetAnchor != null && targetAnchor.MergedIntoServerId != 0)
        {
            throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden, "Target server " + context.Plan.TargetServerId + " already merged into " + targetAnchor.MergedIntoServerId + "; cannot merge into a retired server.");
        }
        foreach (var sourceServerId in context.Plan.SourceServerIds)
        {
            var anchor = await _calendarStore.FindAsync(sourceServerId, cancellationToken).ConfigureAwait(false);
            if (anchor == null)
            {
                throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden, "Server anchor not registered: " + sourceServerId + "; register the open anchor before merging.");
            }

            if (anchor.MergedIntoServerId != 0 && anchor.MergedIntoServerId != context.Plan.TargetServerId)
            {
                throw new OnlineServiceException(OnlineErrorCode.StateOperationForbidden, "Server " + sourceServerId + " already merged into " + anchor.MergedIntoServerId + "; cannot retarget to " + context.Plan.TargetServerId + ".");
            }

            if (anchor.MergedIntoServerId != context.Plan.TargetServerId)
            {
                affected++;
            }
        }

        if (!context.DryRun)
        {
            foreach (var sourceServerId in context.Plan.SourceServerIds)
            {
                await _calendarStore.MarkMergedAsync(sourceServerId, context.Plan.TargetServerId, cancellationToken).ConfigureAwait(false);
            }
        }

        details["anchors"] = affected;
        return new OnlineServerMergeStepReport("server-anchor:mark-merged", affected, details);
    }
}
