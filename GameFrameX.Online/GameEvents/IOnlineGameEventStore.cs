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
/// 游戏事件存储契约（**只存通过 L0 校验的事件**；VC-7.10「原始事件可查询、指标可复算」的存储半边）。
/// <para>
/// 维护约束（红线）：
/// (1) **只收受理事件**——本存储不认识「校验」这回事，脏事件由摄取器拦在门外（结构上保证下游读到的
/// 事件必然通过 schema 校验）；
/// (2) **事件标识唯一**（EventId 为幂等键）：重复投递同一信封只落一条（返回 false），
/// 否则重投会让指标翻倍、报表失真；
/// (3) **追加即不可变**——只追加不修改不删除（留存与归档策略归运行时装配 X4）；
/// (4) 作用域隔离：查询以 (TenantId, AppId) 为前置条件，跨作用域查不到（反预言）。
/// </para>
/// </summary>
public interface IOnlineGameEventStore
{
    /// <summary>
    /// 追加事件（EventId 已存在时不落档，返回 false）。
    /// </summary>
    /// <param name="onlineEvent">通过 L0 校验的事件信封。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>本次是否新落档（false 表示重复投递被去重）。</returns>
    Task<bool> AppendAsync(OnlineEvent onlineEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按事件标识查找事件。
    /// </summary>
    /// <param name="eventId">事件标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>事件信封；不存在返回 null。</returns>
    Task<OnlineEvent> FindAsync(string eventId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按作用域与时间窗列出事件（按事件发生时刻升序，**含窗口两端**——指标复算要求边界确定）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="query">时间窗查询载荷（闭区间）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>事件列表；无记录返回空列表。</returns>
    Task<List<OnlineEvent>> ListAsync(long tenantId, long appId, EventTimeRangeQuery query, CancellationToken cancellationToken = default);
}
