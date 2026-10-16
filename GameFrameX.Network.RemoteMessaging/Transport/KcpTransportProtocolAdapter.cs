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

using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// KCP 协议适配器占位实现。
/// 占位与真实现的边界：占位仅完成注册接线（让 kcp:// 端点能命中本适配器、参与拨号期 scheme 分发），
/// <see cref="IsServiceAvailable"/> 按真实能力恒返回 false，流创建恒抛 <see cref="NotSupportedException"/>；
/// KCP 传输真实现（数据报会话 → Stream 适配层）由另行 change 提供。
/// </summary>
/// <remarks>
/// Placeholder implementation for the KCP protocol adapter.
/// Boundary between the placeholder and the real implementation: the placeholder only completes
/// the registration wiring (so <c>kcp://</c> endpoints can hit this adapter and join the
/// dial-time scheme dispatch), <see cref="IsServiceAvailable"/> always returns false to reflect
/// the real capability, and stream creation always throws <see cref="NotSupportedException"/>;
/// the real KCP transport (datagram session → Stream adaptation) arrives in a separate change.
/// </remarks>
internal sealed class KcpTransportProtocolAdapter : ITransportProtocolAdapter
{
    /// <summary>
    /// 获取协议名称，固定返回 KCP。
    /// </summary>
    /// <remarks>
    /// Gets the protocol name, always returning KCP.
    /// </remarks>
    public string ProtocolName
    {
        get { return "KCP"; }
    }

    /// <summary>
    /// 获取或创建流的占位实现：KCP 传输真实现尚未注册（另行 change），恒抛出 <see cref="NotSupportedException"/>。
    /// </summary>
    /// <remarks>
    /// Placeholder implementation for getting or creating a stream: the real KCP transport is not
    /// wired yet (tracked by a separate change), so this always throws <see cref="NotSupportedException"/>.
    /// Callers observing <see cref="IsServiceAvailable"/> = false should never reach this path.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <exception cref="NotSupportedException">KCP 传输实现尚未接入，恒抛出 / Always thrown since the KCP transport implementation is not wired yet</exception>
    public Task<Stream> GetOrCreateStreamAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        // Localization: RemoteMessaging.Transport.KcpTransportNotImplemented - KCP 传输实现尚未接入（另行 change 跟进），服务 '{0}' 本次调用命中占位适配器
        throw new NotSupportedException(
            LocalizationService.GetString(Localization.Keys.RemoteMessaging.Transport.KcpTransportNotImplemented, serviceName));
    }

    /// <summary>
    /// 检查目标服务是否可用：按占位适配器的真实能力恒返回 false（KCP 传输未接入）。
    /// </summary>
    /// <remarks>
    /// Checks whether the target service is available: always returns false, reflecting the
    /// placeholder's real capability (the KCP transport is not wired yet). The pre-C250
    /// placeholder answered true based on a TCP endpoint probe, which made the availability
    /// pre-check lie about a path that always throws on use.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>恒为 false / Always false</returns>
    public bool IsServiceAvailable(string serviceName)
    {
        return false;
    }

    /// <summary>
    /// 标记连接失效的占位实现：占位适配器无持久连接状态，本方法不执行任何操作，仅保留接口兼容统一调用流程。
    /// </summary>
    /// <remarks>
    /// Placeholder implementation of invalidation: the placeholder adapter holds no persistent connection state,
    /// so this method does nothing and only keeps the interface compatible with the unified call flow.
    /// </remarks>
    public void Invalidate()
    {
        // 占位实现无持久连接状态，保留接口以兼容统一调用流程。
    }

    /// <summary>
    /// 按流标记连接失效的占位实现：占位适配器无持久连接状态，本方法不执行任何操作。
    /// </summary>
    /// <remarks>
    /// Placeholder implementation of stream-scoped invalidation: the placeholder adapter holds
    /// no persistent connection state, so this method does nothing.
    /// </remarks>
    /// <param name="stream">连接流 / The connection stream</param>
    public void Invalidate(Stream stream)
    {
        // 占位实现无持久连接状态，保留接口以兼容统一调用流程。
    }

    /// <summary>
    /// 释放资源的占位实现：占位适配器不持有任何托管或非托管资源，本方法不执行任何操作。
    /// </summary>
    /// <remarks>
    /// Placeholder implementation of disposal: the placeholder adapter holds no managed or unmanaged resources,
    /// so this method does nothing.
    /// </remarks>
    public void Dispose()
    {
        // 占位实现无非托管资源。
    }
}