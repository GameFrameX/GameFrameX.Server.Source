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

namespace GameFrameX.Online.Social;

using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

/// <summary>
/// 通知存储契约（vault:C7 S6.8）。
/// <para>
/// 维护约束（红线，VC-6.12 的落点）：**去重键在「作用域 + 接收者 + <see cref="OnlineNotification.DedupeKey"/>」
/// 上唯一**是该存储的结构性不变量，不是调用方的纪律——<see cref="SaveIfAbsentAsync"/> 是它唯一的守卫点；
/// 通知状态只能经 <see cref="ReplaceAsync"/> 的 CAS 语义改写（期望状态或**期望尝试次数**任一项不匹配即整体失败
/// 并返回 null，绝不部分应用），并发「推送落定 vs 离线补发 vs 过期扫描」由此收敛到同一临界区——
/// 谁先改到目标态，谁的结果生效。
/// </para>
/// <para>
/// 存储实现负责防御性深拷贝，且**不得**在 CAS 失败时留下任何写入痕迹。
/// 载荷（<see cref="OnlineNotification.Payload"/>）在存储层同样是不可见的不透明文本，实现不得解析或改写。
/// </para>
/// </summary>
public interface IOnlineNotificationStore
{
    /// <summary>
    /// 以去重键为唯一键「不存在则创建」：键已存在时返回既有记录且不写入。
    /// <para>
    /// 该方法是「通知不重复消费」的实现依据（VC-6.12）：推送重试与离线补发并发时，只有第一次入队能创建记录，
    /// 其余全部拿到同一条既有记录，调用方据此返回既有通知而非新建或重发。
    /// 去重键含接收者，故同一去重键对不同接收者各落一条合法通知。
    /// </para>
    /// </summary>
    /// <param name="notification">待创建的通知（其去重键与作用域已由调用方规范化）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>当前生效的通知副本（既有记录或刚落库的入参）。</returns>
    Task<OnlineNotification> SaveIfAbsentAsync(OnlineNotification notification, CancellationToken cancellationToken = default);

    /// <summary>
    /// 按通知标识查找（标识在「作用域 + 接收者」内定位，故查询必须带接收者）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="notificationId">通知标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>通知副本；不存在返回 null。</returns>
    Task<OnlineNotification> FindAsync(long tenantId, long appId, long playerId, string notificationId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 以 CAS 语义整体替换通知记录（推送落定 / 已读 / 过期走此入口）。
    /// <para>
    /// 替换的是**整条记录**而非单个字段：调用方传入的是「读取副本 → 在副本上改」后的完整快照，
    /// 存储层不做字段级合并，避免半新半旧的状态被读出。
    /// </para>
    /// <para>
    /// 为什么 CAS 除状态外还要比 <see cref="OnlineNotification.AttemptCount"/>：推送入口刻意允许
    /// 「已在途（Queued）的通知再次入队」这一自环，于是**仅比状态**时两个并发推送会说同一次谎都成真——
    /// 两个读线程读到的都是「Queued / 尝试 1」，先写者落尝试 2，后写者期望状态仍是 Queued 照样成功，
    /// 同一条通知被推两次（VC-6.12 明确禁止重复消费）。把「读到的尝试次数」一并纳入期望值后，
    /// 只有真正持有该次尝试的调用方能提交；而进程崩溃后滞留的 Queued 记录仍能被下一轮读到新的尝试次数后
    /// 重新认领，不牺牲活性。
    /// </para>
    /// </summary>
    /// <param name="notification">替换后的通知快照（其状态已是目标状态）。</param>
    /// <param name="expectedState">期望的当前状态；不匹配即失败。</param>
    /// <param name="expectedAttemptCount">期望的当前尝试次数（调用方读取快照时的值）；不匹配即失败。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>替换后的通知副本；CAS 失败或记录不存在返回 null。</returns>
    Task<OnlineNotification> ReplaceAsync(OnlineNotification notification, OnlineNotificationState expectedState, int expectedAttemptCount, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出某接收者的全部通知（任意状态；通知列表与离线补发的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="playerId">接收者玩家标识。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>通知副本列表。</returns>
    Task<IReadOnlyList<OnlineNotification>> ListByPlayerAsync(long tenantId, long appId, long playerId, CancellationToken cancellationToken = default);

    /// <summary>
    /// 列出作用域内处于指定状态的全部通知（超期扫描与重试扫描的输入）。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="state">通知状态。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>通知副本列表。</returns>
    Task<IReadOnlyList<OnlineNotification>> ListByStateAsync(long tenantId, long appId, OnlineNotificationState state, CancellationToken cancellationToken = default);
}
