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

using System;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Events;

namespace GameFrameX.Online.GameEvents;

/// <summary>
/// 游戏事件摄取器（vault:C8 S7.5 / VC-7.9）：**校验 → 去重 → 存储 / 死信**的唯一入口。
/// <para>
/// 维护约束（红线）：
/// (1) **校验先于一切副作用**——未通过 L0 的事件绝不写存储、绝不进下游管道，只写死信；
/// 这是「不污染下游」的结构性保证，不得为了「先存后审」的便利而调换顺序；
/// (2) 摄取对同一信封**幂等**——EventId 重复只落一条，回执置 <see cref="OnlineGameEventIngestOutcome.IsDuplicate"/>
/// （重投是投递方的正常重试路径，不是拒绝）；
/// (3) 本类不产生事件、不解读业务语义——事件由投影器（<see cref="OnlineGameEventProjector"/>）
/// 或投递方构造，本类只做守门与落档；
/// (4) **空信封是编程错误**（<see cref="ArgumentNullException"/>），不是可拒绝的输入——拒绝意味着「有事件但脏」，
/// 而空信封没有可归档的实体（死信汇要求非空信封）。因此本类的「拒绝则死信」不存在无档可落的缺口。
/// </para>
/// </summary>
public sealed class OnlineGameEventIngestor
{
    /// <summary>事件存储（只收受理事件）。</summary>
    private readonly IOnlineGameEventStore _eventStore;

    /// <summary>死信汇（只收拒绝事件）。</summary>
    private readonly IOnlineGameEventDeadLetterSink _deadLetterSink;

    /// <summary>
    /// 初始化 <see cref="OnlineGameEventIngestor"/>。
    /// </summary>
    /// <param name="eventStore">事件存储。</param>
    /// <param name="deadLetterSink">死信汇。</param>
    public OnlineGameEventIngestor(IOnlineGameEventStore eventStore, IOnlineGameEventDeadLetterSink deadLetterSink)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _deadLetterSink = deadLetterSink ?? throw new ArgumentNullException(nameof(deadLetterSink));
    }

    /// <summary>
    /// 摄取一个事件（校验 → 受理则去重落档 / 拒绝则死信）。
    /// </summary>
    /// <param name="onlineEvent">待摄取的事件信封。</param>
    /// <param name="nowUnixMilliseconds">当前时刻（UTC 毫秒；传 0 取系统时钟，测试可注入）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>摄取回执。</returns>
    public async Task<OnlineGameEventIngestOutcome> IngestAsync(OnlineEvent onlineEvent, long nowUnixMilliseconds = 0, CancellationToken cancellationToken = default)
    {
        if (onlineEvent == null)
        {
            throw new ArgumentNullException(nameof(onlineEvent));
        }

        var validation = OnlineGameEventValidator.Validate(onlineEvent);
        if (!validation.IsAccepted)
        {
            var rejectedTime = nowUnixMilliseconds > 0 ? nowUnixMilliseconds : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            await _deadLetterSink.WriteAsync(onlineEvent, validation.Reason, validation.Message, rejectedTime, cancellationToken).ConfigureAwait(false);
            return new OnlineGameEventIngestOutcome
            {
                EventId = onlineEvent.EventId,
                EventName = onlineEvent.EventType,
                IsAccepted = false,
                IsDuplicate = false,
                RejectionReason = validation.Reason,
                RejectionMessage = validation.Message,
            };
        }

        var appended = await _eventStore.AppendAsync(onlineEvent, cancellationToken).ConfigureAwait(false);
        return new OnlineGameEventIngestOutcome
        {
            EventId = onlineEvent.EventId,
            EventName = onlineEvent.EventType,
            IsAccepted = true,
            IsDuplicate = !appended,
            RejectionReason = OnlineGameEventRejectionReason.None,
            RejectionMessage = null,
        };
    }
}
