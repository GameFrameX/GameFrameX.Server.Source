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
/// 对局生命周期状态（vault:C6「Match 生命周期」：Created → Waiting → Ready → Running → Settling → Completed → Closed，
/// 以及 Cancelled / Timeout / Failed / Aborted / SettlementFailed 分支）。
/// <para>
/// 维护约束（红线）：状态只能由服务端 Match Actor 依 <see cref="OnlineMatchStateMachine"/> 的合法边推进，
/// 客户端没有任何直接写入路径（VC-5.2「客户端篡改结果无效」的落点）。枚举取值是持久化与跨服协议的一部分，
/// 禁止重排或改号；新增状态必须先改状态机邻接表并补状态机测试。
/// </para>
/// </summary>
public enum OnlineMatchState
{
    /// <summary>已创建（由 assignment 落地，尚未进入等待阶段）。</summary>
    Created = 0,

    /// <summary>等待阶段（成员陆续加入与准备）。</summary>
    Waiting = 1,

    /// <summary>全部成员已准备，可开始对局。</summary>
    Ready = 2,

    /// <summary>运行中（权威推进阶段，唯一接受输入的阶段）。</summary>
    Running = 3,

    /// <summary>结算中（结算事实落定中，尚未产出结果）。</summary>
    Settling = 4,

    /// <summary>正常结束（结果已产出，等待释放）。</summary>
    Completed = 5,

    /// <summary>已释放（唯一终态；进入后 Actor 从运行时摘除，VC-5.11「无僵尸 Match」的落点）。</summary>
    Closed = 6,

    /// <summary>已取消（开始前被取消）。</summary>
    Cancelled = 7,

    /// <summary>已超时（等待或运行阶段超出时限）。</summary>
    Timeout = 8,

    /// <summary>已失败（玩法级规则不可满足）。</summary>
    Failed = 9,

    /// <summary>异常中止（异常退出或 Actor 异常，VC-5.11）。</summary>
    Aborted = 10,

    /// <summary>结算失败（结算事实未成立，可重试结算）。</summary>
    SettlementFailed = 11,
}
