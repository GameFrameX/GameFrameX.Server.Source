//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file at the source code root for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯公民合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes and liabilities arising from secondary development,
//   本组织不承担任何责任，开发者须自行承担。
//   The organization shall not be responsible for any disputes; developers shall bear the responsibility.
//  ==========================================================================================

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Abstractions.Contracts;

/// <summary>
/// 匹配域桥契约面（C246）：旧 420 协议族（入队 / 取消 / 状态）与新增入场、分配解析的最小桥接契约，
/// 供 Hotfix 协议面薄适配器与组合根通知推送方经 <c>OnlineMatchDomainBridgeHolder</c> 进程内消费。
/// <para>
/// 维护约束：
/// ① 匹配池唯一权威 = 票据域（实现方内部经同一票据存储入队并驱动撮合），本契约不得另建队列或池副本；
/// ② <see cref="EnqueueAsync"/> 在建票后同步执行一轮撮合（即时窗口兼容：对手已在池时响应即刻带
/// 数值对局标识，等价旧 FIFO 链第二人入队即配对的可观测行为）；
/// ③ 作用域三元组由组合根授权作用域供给，玩家位由请求主体（会话绑定身份）填充，客户端字段一律不可覆盖；
/// ④ 数值对局标识由实现方登记表从匹配分配派生（assignment → 协议数值 id 单射），重启即失效
///（与票据存储当前易失性一致），不得由调用方指定；
/// ⑤ <see cref="EnterAsync"/> 是「等价最小入场夹具」：按数值标识 create-or-reuse 平台对局运行时
/// Actor、双方就绪并启动——不发明对局玩法新面，玩法裁决 / 结算仍归平台编排。
/// </para>
/// </summary>
public interface IOnlineMatchDomain
{
    /// <summary>
    /// 入队（单人票据；即时窗口：对手已在池时本轮即撮合）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="mode">匹配模式（协议标签，同时是撮合池分区键）。</param>
    /// <param name="rating">玩家评分（大于 0；映射 ± 基础窗的技能区间）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入队回执（MatchId 非 0 = 即时撮合）。</returns>
    Task<OnlineResult<OnlineMatchDomainEnqueue>> EnqueueAsync(OnlineScope scope, int mode, int rating, CancellationToken cancellationToken = default);

    /// <summary>
    /// 取消本人最近排队票据；票据已处终态时回终态回执（<c>Cancelled=false</c>，不算失败）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>取消回执。</returns>
    Task<OnlineResult<OnlineMatchDomainCancel>> CancelAsync(OnlineScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 查询本人匹配状态（排队中 / 已撮合含数值对局标识 / 其余终态）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>状态回执。</returns>
    Task<OnlineResult<OnlineMatchDomainStatus>> GetStatusAsync(OnlineScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按数值对局标识入场（create-or-reuse + 双方就绪 + 启动；幂等，双方各自到达同一对局）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 入场成员）。</param>
    /// <param name="matchId">协议数值对局标识（来自入队回执 / 状态 / 通知）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>入场回执。</returns>
    Task<OnlineResult<OnlineMatchDomainEntry>> EnterAsync(OnlineScope scope, long matchId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按分配标识解析数值对局标识与成员表（通知推送方消费；幂等）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="assignmentId">分配标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>分配解析视图。</returns>
    Task<OnlineResult<OnlineMatchDomainAssignment>> ResolveAssignmentAsync(long tenantId, long appId, string assignmentId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 会话中介断线通知（C204）：物理连接断开时按「票据 → 分配登记 → 运行时成员表」定位玩家当前对局，
    /// 标记成员断线并开重连窗（rt- 令牌服务端留存，不下发客户端）。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 断线成员）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>断线标记回执（无进行中对局为 Marked=false，非失败）。</returns>
    Task<OnlineResult<OnlineMatchDomainDisconnect>> NotifyPlayerDisconnectedAsync(OnlineScope scope, CancellationToken cancellationToken = default);

    /// <summary>
    /// 会话中介恢复（C204）：身份已在更高层凭会话凭据证明，本面按成员表核对该玩家当前对局归属——
    /// 断线成员窗口内恢复（服务端留存令牌回灌，Actor 三重校验原样生效），活跃成员幂等确认；
    /// 不重新匹配、不另开对局、不重复结算。
    /// </summary>
    /// <param name="scope">生效作用域（必须含玩家主体位 = 恢复成员）。</param>
    /// <param name="lastAcknowledgedSequence">客户端最后确认的服务器序号（增量下界；不可服务时回退快照）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>恢复回执；失败码——无对局回 <c>Resumed=false</c> 成功，超窗 <see cref="OnlineErrorCode.NetworkTimeout"/>，
    /// 已结束 <see cref="OnlineErrorCode.StateEnded"/>，成员已离局 <see cref="OnlineErrorCode.ScopeDenied"/>。</returns>
    Task<OnlineResult<OnlineMatchDomainResume>> TryResumeForPlayerAsync(OnlineScope scope, long lastAcknowledgedSequence, CancellationToken cancellationToken = default);
}
