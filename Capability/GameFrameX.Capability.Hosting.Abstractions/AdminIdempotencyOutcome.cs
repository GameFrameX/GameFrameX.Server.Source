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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Capability.Hosting.AdminApi;

/// <summary>
/// admin 幂等裁决结果。
/// </summary>
public sealed class AdminIdempotencyOutcome
{
    /// <summary>
    /// 初始化 <see cref="AdminIdempotencyOutcome"/>。
    /// </summary>
    /// <param name="kind">裁决种类。</param>
    /// <param name="canExecute">是否允许执行。</param>
    /// <param name="errorCode">拒绝码（拒绝时必填）。</param>
    /// <param name="firstResponse">首次响应原文（Replay 时必填）。</param>
    public AdminIdempotencyOutcome(AdminIdempotencyOutcomeKind kind, bool canExecute, AdminApiErrorCode errorCode = null, byte[] firstResponse = null)
    {
        Kind = kind;
        CanExecute = canExecute;
        ErrorCode = errorCode;
        FirstResponse = firstResponse;
    }

    /// <summary>
    /// 获取裁决种类。
    /// </summary>
    public AdminIdempotencyOutcomeKind Kind { get; }

    /// <summary>
    /// 获取是否允许执行。
    /// </summary>
    public bool CanExecute { get; }

    /// <summary>
    /// 获取拒绝码。
    /// </summary>
    public AdminApiErrorCode ErrorCode { get; }

    /// <summary>
    /// 获取首次响应原文（Replay 用）。
    /// </summary>
    public byte[] FirstResponse { get; }
}
