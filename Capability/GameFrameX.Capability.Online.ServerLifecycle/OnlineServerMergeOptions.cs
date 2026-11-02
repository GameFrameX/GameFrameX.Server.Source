// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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
//   本组织与贡献者概不承担。
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

namespace GameFrameX.Capability.Online.ServerLifecycle;

using System;
using GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 合服参数段。
/// </summary>
public sealed class OnlineServerMergeOptions
{
    /// <summary>
    /// 区服域行键重写批大小（默认 500；每批一批落库，批间让出）。
    /// </summary>
    public int RowRewriteBatchSize { get; set; } = 500;

    /// <summary>
    /// 是否强制 dry-run 先行（默认 true：未对该归并计划产出过 dry-run 报告前拒绝真实执行）。
    /// </summary>
    /// <remarks>
    /// 关闭该门禁 = 部署方显式承担跳过预演的数据风险；生产环境禁止关闭。
    /// </remarks>
    public bool RequireDryRunBeforeExecute { get; set; } = true;

    /// <summary>
    /// 校验：批大小必须为正。
    /// </summary>
    /// <param name="validation">校验收集器。</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        validation.RequirePositive(nameof(RowRewriteBatchSize), RowRewriteBatchSize);
    }
}
