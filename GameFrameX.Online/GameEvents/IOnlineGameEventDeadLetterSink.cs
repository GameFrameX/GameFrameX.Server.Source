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
//   please see the LICENSE file in the root directory of the source code for the full license text.
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
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Events;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件死信汇（被 L0 校验拒绝的事件在此落档，**绝不进入事件存储与下游管道**）。
/// <para>
/// 维护约束（红线）：死信汇是**脏事件的唯一去向**——摄取器一律「受理则存储、拒绝则死信」，
/// 不存在「拒绝但仍入库」的中间态，否则下游消费者仍会读到未通过 schema 的事件，
/// 「不污染下游」的结构性保证即失效（VC-7.9）。
/// </para>
/// <para>
/// 归档口径：死信按**事件自身的作用域**归档，故作用域非法的事件会落在其退化作用域
/// （TenantId 或 AppId 为 0）之下——不丢失、可检索，但正常作用域的查询看不到它们，
/// 这类拒绝对投递方的可达路径是摄取回执。
/// </para>
/// </summary>
public interface IOnlineGameEventDeadLetterSink
{
    /// <summary>
    /// 写入一条死信。
    /// </summary>
    /// <param name="onlineEvent">被拒绝的事件信封。</param>
    /// <param name="reason">拒绝码。</param>
    /// <param name="message">拒绝原因。</param>
    /// <param name="rejectedTime">拒收时刻（UTC 毫秒）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>落档后的死信记录。</returns>
    Task<OnlineGameEventDeadLetter> WriteAsync(OnlineEvent onlineEvent, OnlineGameEventRejectionReason reason, string message, long rejectedTime, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按作用域与时间窗列出死信（查询与指标复算入口；跨作用域查不到，反预言）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="query">时间窗查询载荷（闭区间；与事件存储同型）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>死信列表（按拒收时刻升序）；无记录返回空列表。</returns>
    Task<List<OnlineGameEventDeadLetter>> ListAsync(long tenantId, long appId, EventTimeRangeQuery query, CancellationToken cancellationToken = default);
}
