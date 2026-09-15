// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others that are prohibited by laws and regulations!
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Assets;

using GameFrameX.Online.Contracts;

/// <summary>
/// 资产批次应用结果（存储层原子应用的输出；失败携带段位化错误码供统一入口透传）。
/// <para>
/// 维护约束：成功时 <see cref="Entries"/> 为本批次全部落账条目（顺序与批次行一致）；
/// 失败时净效应为 0（先全量校验后落账，无部分应用）；余额/库存不足映射
/// <see cref="OnlineErrorCode.StateOperationForbidden"/>（5xxx 业务状态段，错误码复用约束见 C95 方案复审 R1）。
/// </para>
/// </summary>
public sealed class OnlineAssetApplyResult
{
    /// <summary>
    /// 获取应用是否成功。
    /// </summary>
    public bool Success
    {
        get;
    }

    /// <summary>
    /// 获取失败错误码（成功时为 <see cref="OnlineErrorCode.None"/>）。
    /// </summary>
    public OnlineErrorCode ErrorCode
    {
        get;
    }

    /// <summary>
    /// 获取失败描述（服务端内部语义）。
    /// </summary>
    public string Message
    {
        get;
    }

    /// <summary>
    /// 获取落账条目（成功时非空；失败时为空列表）。
    /// </summary>
    public IReadOnlyList<OnlineLedgerEntry> Entries
    {
        get;
    }

    /// <summary>
    /// 构造应用结果。
    /// </summary>
    /// <param name="success">是否成功。</param>
    /// <param name="errorCode">失败错误码。</param>
    /// <param name="message">失败描述。</param>
    /// <param name="entries">落账条目。</param>
    public OnlineAssetApplyResult(bool success, OnlineErrorCode errorCode, string message, IReadOnlyList<OnlineLedgerEntry> entries)
    {
        Success = success;
        ErrorCode = errorCode;
        Message = message;
        Entries = entries ?? Array.Empty<OnlineLedgerEntry>();
    }
}
