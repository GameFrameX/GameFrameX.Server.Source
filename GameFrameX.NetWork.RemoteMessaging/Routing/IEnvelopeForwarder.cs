// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Discovery;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨进程信封转发缝（C143d D3 case 2/3 的发送通道）。
/// </summary>
/// <remarks>
/// The send channel behind the D3 case 2/3 remote forwarding seam (C143d).
/// The router resolves <em>where</em> (instance selection + endpoint parsing); the
/// forwarder moves the envelope <em>there</em>. Keeping the transport behind this
/// narrow seam lets the routing logic be unit-tested with a fake forwarder and lets
/// the transport evolve (TCP now; KCP/QUIC via the same seam later) without
/// touching the decision code.
/// </remarks>
public interface IEnvelopeForwarder
{
    /// <summary>
    /// 将路由信封转发到已解析的目标端点。
    /// </summary>
    /// <remarks>
    /// Forwards the routing envelope to the already-parsed target endpoint.
    /// Transport failures propagate to the caller — forwarding must fail loudly
    /// (never a silent drop); retry/circuit policies belong to upper layers.
    /// </remarks>
    /// <param name="endpoint">已解析的目标端点 / The parsed target endpoint</param>
    /// <param name="envelope">路由信封 / The routing envelope</param>
    /// <param name="cancellationToken">取消令牌 / The cancellation token</param>
    /// <returns>异步任务；失败时抛出 / Async task; throws on transport failure</returns>
    Task ForwardAsync(ParsedEndpoint endpoint, MessageEnvelope envelope, CancellationToken cancellationToken = default);
}
