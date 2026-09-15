//  ==========================================================================================
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
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Match;

/// <summary>
/// 重连结果（vault:C6 S5.6「重连成功先发送完整快照，再发送客户端缺失的增量」）。
/// <para>
/// 维护约束（红线）：<see cref="Snapshot"/> 与 <see cref="Delta"/> 的服务器序号必须自洽——
/// 增量区间起点即快照序号（<c>Snapshot.ServerSequence == Delta.FromSequence</c>），
/// 客户端据此可直接续接而不需要额外对齐。当增量不可用（跨越了事件日志下界）时
/// <see cref="Delta"/> 为 null，客户端必须以快照为准重新建立本地状态（VC-5.6）。
/// </para>
/// </summary>
public sealed class OnlineMatchReconnectResult
{
    /// <summary>
    /// 获取或设置是否重连成功。
    /// </summary>
    public bool Succeeded
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败原因（成功时为 <see cref="Contracts.OnlineErrorCode.None"/>）。
    /// </summary>
    public Contracts.OnlineErrorCode FailureCode
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置失败说明（诊断用）。
    /// </summary>
    public string FailureMessage
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置对局标识。
    /// </summary>
    public string MatchId
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置重连后的对局状态。
    /// </summary>
    public OnlineMatchState State
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置重连后的服务器序号。
    /// </summary>
    public long ServerSequence
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置完整快照（重连成功时非空）。
    /// </summary>
    public OnlineMatchSnapshot Snapshot
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置缺失增量（为 null 表示增量不可用，客户端须以快照为准重建）。
    /// </summary>
    public OnlineMatchDelta Delta
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置处置说明（如「转托管」「转退出」，VC-5.7 的确定态说明）。
    /// </summary>
    public string Instruction
    {
        get;
        set;
    }
}
