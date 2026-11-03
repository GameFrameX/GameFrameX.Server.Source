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
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Keys;

/// <summary>
/// 合服归并计划（幂等键 = <c>BuildPlanId()</c>；同一计划重复提交为 no-op 回放）。
/// </summary>
/// <remarks>
/// 计划语义：全部 <see cref="SourceServerIds"/> 归并入 <see cref="TargetServerId"/>。
/// 源服集合登记时去重排序冻结（等价集合 ⇒ 同一 PlanId ⇒ 幂等）。
/// </remarks>
public sealed class OnlineServerMergePlan
{
    /// <summary>
    /// 目标区服标识（正数）。
    /// </summary>
    public long TargetServerId { get; set; }

    /// <summary>
    /// 源区服标识集合（登记时去重排序冻结）。
    /// </summary>
    public IReadOnlyList<long> SourceServerIds { get; set; } = new List<long>();

    /// <summary>
    /// 计划创建时刻（UTC 毫秒）。
    /// </summary>
    public long CreatedAtUnixMilliseconds { get; set; }

    /// <summary>
    /// 构造归并计划（源集合去重 + 升序排序冻结）。
    /// </summary>
    /// <param name="targetServerId">目标区服标识。</param>
    /// <param name="sourceServerIds">源区服标识集合。</param>
    /// <param name="createdAtUnixMilliseconds">计划创建时刻（UTC 毫秒）。</param>
    /// <returns>归并计划。</returns>
    public static OnlineServerMergePlan Create(long targetServerId, IEnumerable<long> sourceServerIds, long createdAtUnixMilliseconds)
    {
        if (targetServerId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(targetServerId), "TargetServerId must be positive.");
        }

        if (sourceServerIds == null)
        {
            throw new ArgumentNullException(nameof(sourceServerIds));
        }

        var sources = sourceServerIds.Distinct().OrderBy(id => id).ToList();
        if (sources.Count == 0)
        {
            throw new ArgumentException("SourceServerIds must not be empty.", nameof(sourceServerIds));
        }

        if (sources.Any(id => id <= 0))
        {
            throw new ArgumentException("SourceServerIds must be positive.", nameof(sourceServerIds));
        }

        if (sources.Contains(targetServerId))
        {
            throw new ArgumentException("TargetServerId must not appear in SourceServerIds.", nameof(sourceServerIds));
        }

        return new OnlineServerMergePlan
        {
            TargetServerId = targetServerId,
            SourceServerIds = sources,
            CreatedAtUnixMilliseconds = createdAtUnixMilliseconds,
        };
    }

    /// <summary>
    /// 计划标识（幂等键：<c>merge-{target}:{sources 升序连字符连接}</c>）。
    /// </summary>
    /// <returns>计划标识。</returns>
    public string BuildPlanId()
    {
        return "merge-" + TargetServerId.ToString(System.Globalization.CultureInfo.InvariantCulture) + "-" + string.Join("-", SourceServerIds.Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
    }
}
