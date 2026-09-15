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
//   Any legal disputes and liabilities arising from secondary development based on this project
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

using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Idempotency;

/// <summary>
/// Online 幂等判定结果（Foundation 决策的 Online 语义封装，含协议错误码映射与首次响应透出）。
/// <para>
/// 维护约束：<see cref="ErrorCode"/> 映射固定——Execute/Replay 映射 <see cref="OnlineErrorCode.None"/>
/// （Replay 是成功回放而非错误），Conflict 映射 <see cref="OnlineErrorCode.VersionConflict"/>，
/// Busy 映射 <see cref="OnlineErrorCode.ServiceBusy"/>，InvalidKey 映射 <see cref="OnlineErrorCode.ParameterInvalid"/>；
/// 映射关系调整须回 vault 契约评审。
/// </para>
/// </summary>
public sealed class OnlineIdempotencyOutcome
{
    /// <summary>
    /// 获取判定类型。
    /// </summary>
    public OnlineIdempotencyOutcomeKind Kind
    {
        get;
    }

    /// <summary>
    /// 获取映射的协议错误码（Replay 成功回放为 <see cref="OnlineErrorCode.None"/>）。
    /// </summary>
    public OnlineErrorCode ErrorCode
    {
        get;
    }

    /// <summary>
    /// 获取首次响应字节（仅 Replay 时非空；调用方按自身序列化契约还原）。
    /// </summary>
    public ReadOnlyMemory<byte> FirstResponse
    {
        get;
    }

    /// <summary>
    /// 获取本次判定的作用域绑定键（含玩家位与否由调用方 Begin 时决定）。
    /// </summary>
    public string ScopeKey
    {
        get;
    }

    /// <summary>
    /// 获取本次判定的幂等键。
    /// </summary>
    public string IdempotencyKey
    {
        get;
    }

    /// <summary>
    /// 获取判定是否允许执行业务。
    /// </summary>
    public bool CanExecute
    {
        get
        {
            return Kind == OnlineIdempotencyOutcomeKind.Execute;
        }
    }

    /// <summary>
    /// 构造判定结果。
    /// </summary>
    /// <param name="kind">判定类型。</param>
    /// <param name="errorCode">映射的协议错误码。</param>
    /// <param name="firstResponse">首次响应字节（Replay 时）。</param>
    /// <param name="scopeKey">作用域绑定键。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    public OnlineIdempotencyOutcome(OnlineIdempotencyOutcomeKind kind, OnlineErrorCode errorCode, ReadOnlyMemory<byte> firstResponse, string scopeKey, string idempotencyKey)
    {
        Kind = kind;
        ErrorCode = errorCode;
        FirstResponse = firstResponse;
        ScopeKey = scopeKey;
        IdempotencyKey = idempotencyKey;
    }
}
