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

using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;

/// <summary>
/// 跨服补偿队列（vault:C4 S3.8/VC-3.11：归属服不可用时的续投承载；恢复后自动完成且不重复）。
/// <para>
/// 维护约束（红线）：续投原样保留请求（幂等键不变——恢复后重复投递只生效一次）；
/// SLO 超时告警一次并保留条目（超时转人工，积压超时 = 0 的守护输入）；
/// 内存实现为单进程默认（生产装配以持久化队列替换；运维巡检消费 <see cref="ListPending"/>）。
/// ponytail: 内存队列天花板——进程重启丢队列；生产以持久化实现替换，SLO 计时随之落库。
/// </para>
/// </summary>
public sealed class OnlineGrantCompensationQueue
{
    /// <summary>跨服投递传输。</summary>
    private readonly IOnlineCrossServerGrantTransport _transport;

    /// <summary>告警出口。</summary>
    private readonly IOnlineAssetAlertSink _alertSink;

    /// <summary>SLO 时长（毫秒）。</summary>
    private readonly long _sloMilliseconds;

    /// <summary>队列守卫锁。</summary>
    private readonly object _syncRoot = new object();

    /// <summary>待续投条目。</summary>
    private readonly List<OnlineGrantCompensationItem> _items = new List<OnlineGrantCompensationItem>();

    /// <summary>
    /// 初始化 <see cref="OnlineGrantCompensationQueue"/>。
    /// </summary>
    /// <param name="transport">跨服投递传输。</param>
    /// <param name="alertSink">告警出口（可空 = 跳过告警）。</param>
    /// <param name="sloMilliseconds">SLO 时长（毫秒；默认 10 分钟）。</param>
    public OnlineGrantCompensationQueue(IOnlineCrossServerGrantTransport transport, IOnlineAssetAlertSink alertSink = null, long sloMilliseconds = 600000)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _alertSink = alertSink;
        _sloMilliseconds = sloMilliseconds;
    }

    /// <summary>
    /// 入队一笔待续投请求（路由器在归属服不可达时调用）。
    /// </summary>
    /// <param name="request">原始请求（原样保留）。</param>
    public void Enqueue(OnlineGrantRequest request)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        lock (_syncRoot)
        {
            _items.Add(new OnlineGrantCompensationItem(request, now, now + _sloMilliseconds));
        }
    }

    /// <summary>
    /// 续投一批待补偿请求（逐条向归属服重投；成功移除，失败保留并检查 SLO）。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>续投计数。</returns>
    public async Task<OnlineGrantCompensationDrainResult> DrainAsync(CancellationToken cancellationToken = default)
    {
        var delivered = 0;
        List<OnlineGrantCompensationItem> snapshot;
        lock (_syncRoot)
        {
            snapshot = _items.ToList();
        }

        foreach (var item in snapshot)
        {
            var homeServerId = item.Request.HomeServerId > 0 ? item.Request.HomeServerId : item.Request.Scope.ServerId;
            try
            {
                var result = await _transport.DeliverAsync(homeServerId, item.Request, cancellationToken);
                if (result.IsSuccess)
                {
                    delivered++;
                }

                // 非异常返回 = 已获得确定答复（成功或业务性失败）：均移除，不重试。
                Remove(item);
                continue;
            }
            catch (Exception)
            {
                // 归属服仍不可达：保留条目，走 SLO 检查。
            }

            await CheckSloAsync(item, cancellationToken);
        }

        int remaining;
        lock (_syncRoot)
        {
            remaining = _items.Count;
        }

        return new OnlineGrantCompensationDrainResult(delivered, remaining);
    }

    /// <summary>
    /// 列举滞留条目（运维巡检/Admin 观测输入；返回快照）。
    /// </summary>
    /// <returns>滞留条目快照。</returns>
    public IReadOnlyList<OnlineGrantCompensationItem> ListPending()
    {
        lock (_syncRoot)
        {
            return _items.ToList();
        }
    }

    /// <summary>
    /// 移除条目。
    /// </summary>
    /// <param name="item">条目。</param>
    private void Remove(OnlineGrantCompensationItem item)
    {
        lock (_syncRoot)
        {
            _items.Remove(item);
        }
    }

    /// <summary>
    /// SLO 检查（超时且未告警过 → 发 CompensationSloExceeded 告警一次；条目保留转人工）。
    /// </summary>
    /// <param name="item">条目。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task CheckSloAsync(OnlineGrantCompensationItem item, CancellationToken cancellationToken)
    {
        if (_alertSink == null || item.SloAlerted)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        if (now <= item.DeadlineTime)
        {
            return;
        }

        item.SloAlerted = true;
        var homeServerId = item.Request.HomeServerId > 0 ? item.Request.HomeServerId : item.Request.Scope.ServerId;
        var record = new OnlineAssetAlertRecord
        {
            Kind = OnlineAssetAlertKind.CompensationSloExceeded,
            TransactionId = string.Empty,
            ScopeKey = item.Request.Scope.ToScopeKey(true),
            AssetId = string.Empty,
            Detail = "跨服补偿滞留超过 SLO（归属服 " + homeServerId + "，幂等键 " + item.Request.IdempotencyKey + "），转人工处理",
            OccurredTime = now,
        };
        await _alertSink.AlertAsync(record, cancellationToken);
    }
}
