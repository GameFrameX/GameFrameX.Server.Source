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
/// 合服执行上下文（参与者可见面：计划 + 预演标记 + 全域触碰记账）。
/// </summary>
public sealed class OnlineServerMergeContext
{
    /// <summary>
    /// 初始化上下文。
    /// </summary>
    /// <param name="plan">归并计划。</param>
    /// <param name="dryRun">是否 dry-run 预演。</param>
    /// <param name="rowKeySchemaVersion">行键模式版本快照。</param>
    public OnlineServerMergeContext(OnlineServerMergePlan plan, bool dryRun, int rowKeySchemaVersion)
    {
        Plan = plan ?? throw new ArgumentNullException(nameof(plan));
        DryRun = dryRun;
        RowKeySchemaVersion = rowKeySchemaVersion;
    }

    /// <summary>
    /// 归并计划。
    /// </summary>
    public OnlineServerMergePlan Plan { get; }

    /// <summary>
    /// 是否 dry-run 预演（true 时参与者必须零写入，仅产出预估报告）。
    /// </summary>
    public bool DryRun { get; }

    /// <summary>
    /// 行键模式版本快照（重写器按版本感知：键形升级 = 重写逻辑需随版本迁移）。
    /// </summary>
    public int RowKeySchemaVersion { get; }

    /// <summary>
    /// 参与者登记的全局域触碰计数（铁律断言面：执行结束必须为 0）。
    /// </summary>
    public long GlobalDomainRowsTouched { get; private set; }

    /// <summary>
    /// 登记全局域触碰行数（仅测试性参与者可调用；生产参与者触碰全局域 = 违反 C462 键形铁律，
    /// 合服服务会以非零计数拒绝落定执行结果）。
    /// </summary>
    /// <param name="count">触碰行数。</param>
    public void RecordGlobalDomainRowsTouched(long count)
    {
        GlobalDomainRowsTouched += count;
    }
}
