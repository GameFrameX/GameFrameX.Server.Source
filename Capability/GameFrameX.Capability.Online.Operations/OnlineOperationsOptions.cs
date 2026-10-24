// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the PRC and relevant international regulations.
//   使用本项目须严格遵守相关法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with relevant laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Online.Operations.HotfixRollback;

namespace GameFrameX.Capability.Online.Operations;

/// <summary>
/// 运营聚合能力包级 Options 根类型（C294：聚合 Hotfix 登记期制品获取参数；默认值唯一来源 = 字段初始值）。
/// </summary>
public sealed class OnlineOperationsOptions : ICapabilityOptions
{
    /// <summary>
    /// 获取或设置登记期制品获取参数（单次超时 / 重试次数 / 大小上限；<c>OnlineHotfixArtifactFetcher</c> 必填入参）。
    /// </summary>
    public OnlineHotfixArtifactFetchOptions ArtifactFetch { get; set; } = new OnlineHotfixArtifactFetchOptions();

    /// <summary>
    /// 校验制品获取参数：单次尝试超时须为正时长，最大重试次数不得为负，制品大小上限须为正数。
    /// </summary>
    /// <remarks>
    /// Requires a positive attempt timeout, a non-negative maximum retry count and a positive maximum artifact size.
    /// </remarks>
    /// <param name="validation">选项校验收集器 / Options validation collector</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        if (ArtifactFetch.AttemptTimeout <= TimeSpan.Zero)
        {
            validation.AddError("ArtifactFetch.AttemptTimeout", "must be a positive time span (actual: " + ArtifactFetch.AttemptTimeout + ")");
        }

        if (ArtifactFetch.MaxRetryCount < 0)
        {
            validation.AddError("ArtifactFetch.MaxRetryCount", "must not be negative (actual: " + ArtifactFetch.MaxRetryCount + ")");
        }

        validation.RequirePositive("ArtifactFetch.MaxArtifactBytes", ArtifactFetch.MaxArtifactBytes);
    }
}
