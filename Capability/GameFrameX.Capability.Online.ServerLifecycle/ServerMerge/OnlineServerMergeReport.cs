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
/// 合服报告（dry-run 预演与真实执行共用形态；dry-run 报告是真实执行的强制前置）。
/// </summary>
public sealed class OnlineServerMergeReport
{
    /// <summary>
    /// 归并计划标识。
    /// </summary>
    public string PlanId { get; set; } = string.Empty;

    /// <summary>
    /// 目标区服标识。
    /// </summary>
    public long TargetServerId { get; set; }

    /// <summary>
    /// 源区服标识集合。
    /// </summary>
    public IReadOnlyList<long> SourceServerIds { get; set; } = new List<long>();

    /// <summary>
    /// 是否 dry-run 预演。
    /// </summary>
    public bool DryRun { get; set; }

    /// <summary>
    /// 报告产出时刻（UTC 毫秒）。
    /// </summary>
    public long ReportedAtUnixMilliseconds { get; set; }

    /// <summary>
    /// 行键模式版本快照（<see cref="OnlineRowKey.RowKeySchemaVersion"/>；重写器按版本感知键形）。
    /// </summary>
    public int RowKeySchemaVersion { get; set; }

    /// <summary>
    /// 分步报告集合。
    /// </summary>
    public IReadOnlyList<OnlineServerMergeStepReport> Steps { get; set; } = new List<OnlineServerMergeStepReport>();

    /// <summary>
    /// 全局域触碰行数（C462 键形铁律：好友/身份/玩家存储/钱包/账本/库存/排行投影为全局域，
    /// 键内无 ServerId 位，合服零操作——本计数恒为 0，非 0 = 实现缺陷，服务层拒绝落定）。
    /// </summary>
    public long GlobalDomainRowsTouched { get; set; }
}
