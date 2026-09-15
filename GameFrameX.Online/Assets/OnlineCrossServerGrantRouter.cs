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
/// 跨服发奖路由（vault:C4 S3.8/VC-3.10：归属服明确的路由规则——本服直接执行，跨服经传输投递）。
/// <para>
/// 维护约束（红线）：归属服以 <see cref="OnlineGrantRequest.HomeServerId"/>（缺省取作用域区服）为准，
/// ServerId 只表运行位置不产生数据歧义（vault:C1）；跨服投递失败（不可达）进补偿队列并返回
/// <see cref="OnlineErrorCode.DependencyUnavailable"/>（可重试；重试后不得重复生效——由幂等保证，VC-3.11）；
/// 目标服业务性失败原样透传，不转补偿。
/// </para>
/// </summary>
public sealed class OnlineCrossServerGrantRouter
{
    /// <summary>本服标识（路由判定基准）。</summary>
    private readonly long _localServerId;

    /// <summary>本服统一入口。</summary>
    private readonly OnlineGrantService _grantService;

    /// <summary>跨服投递传输。</summary>
    private readonly IOnlineCrossServerGrantTransport _transport;

    /// <summary>补偿队列（归属服不可用时的续投承载）。</summary>
    private readonly OnlineGrantCompensationQueue _compensationQueue;

    /// <summary>
    /// 初始化 <see cref="OnlineCrossServerGrantRouter"/>。
    /// </summary>
    /// <param name="localServerId">本服标识。</param>
    /// <param name="grantService">本服统一入口。</param>
    /// <param name="transport">跨服投递传输。</param>
    /// <param name="compensationQueue">补偿队列。</param>
    public OnlineCrossServerGrantRouter(long localServerId, OnlineGrantService grantService, IOnlineCrossServerGrantTransport transport, OnlineGrantCompensationQueue compensationQueue)
    {
        _localServerId = localServerId;
        _grantService = grantService ?? throw new ArgumentNullException(nameof(grantService));
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
        _compensationQueue = compensationQueue ?? throw new ArgumentNullException(nameof(compensationQueue));
    }

    /// <summary>
    /// 路由一笔统一资产交易（归属服 = 本服直接执行；跨服经传输投递，不可达进补偿队列）。
    /// </summary>
    /// <param name="request">统一入口请求。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>执行结果（不可达时返回 DependencyUnavailable 并已入队补偿）。</returns>
    public async Task<OnlineResult<OnlineGrantResult>> RouteAsync(OnlineGrantRequest request, CancellationToken cancellationToken = default)
    {
        if (request == null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var homeServerId = request.HomeServerId > 0 ? request.HomeServerId : request.Scope.ServerId;
        if (homeServerId == _localServerId)
        {
            return await _grantService.ExecuteAsync(request, cancellationToken);
        }

        try
        {
            return await _transport.DeliverAsync(homeServerId, request, cancellationToken);
        }
        catch (Exception ex)
        {
            _compensationQueue.Enqueue(request);
            return OnlineResult<OnlineGrantResult>.Fail(OnlineErrorCode.DependencyUnavailable, "归属服 " + homeServerId + " 暂不可用，已进入补偿队列：" + ex.Message);
        }
    }
}
