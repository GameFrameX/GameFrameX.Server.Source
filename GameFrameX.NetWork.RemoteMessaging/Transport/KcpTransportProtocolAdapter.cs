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

namespace GameFrameX.NetWork.RemoteMessaging.Transport;

/// <summary>
/// KCP 协议适配器占位实现。
/// 当前仅提供抽象接入点，便于在不改业务调用层的前提下逐步接入 KCP 传输。
/// </summary>
/// <remarks>
/// Placeholder implementation for the KCP protocol adapter.
/// It only provides an integration point so KCP transport can be introduced incrementally
/// without changing business-side call code.
/// </remarks>
internal sealed class KcpTransportProtocolAdapter : ITransportProtocolAdapter
{
    private readonly IServiceEndpointResolver _endpointResolver;

    /// <summary>
    /// 初始化 KCP 协议适配器占位实现。
    /// </summary>
    /// <remarks>
    /// Initializes the placeholder KCP protocol adapter.
    /// </remarks>
    /// <param name="endpointResolver">服务端点解析器 / Service endpoint resolver</param>
    public KcpTransportProtocolAdapter(IServiceEndpointResolver endpointResolver)
    {
        _endpointResolver = endpointResolver;
    }

    /// <summary>
    /// 获取协议名称，固定返回 KCP。
    /// </summary>
    /// <remarks>
    /// Gets the protocol name, always returning KCP.
    /// </remarks>
    public string ProtocolName => "KCP";

    /// <summary>
    /// 获取或创建流的占位实现：仅解析出服务端点即抛出 <see cref="NotSupportedException"/>，KCP 传输尚未接入。
    /// </summary>
    /// <remarks>
    /// Placeholder implementation for getting or creating a stream: it only resolves the service endpoint
    /// and then throws <see cref="NotSupportedException"/> because KCP transport is not wired yet.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <exception cref="NotSupportedException">KCP 传输尚未接入，恒抛出 / Always thrown since KCP transport is not wired yet</exception>
    public Task<Stream> GetOrCreateStreamAsync(string serviceName, CancellationToken cancellationToken = default)
    {
        var endpoint = _endpointResolver.ResolveTcpEndpoint(serviceName);
        throw new NotSupportedException(
            $"KCP adapter is not wired yet for service '{serviceName}'. Raw endpoint: '{endpoint}'.");
    }

    /// <summary>
    /// 检查目标服务是否可用：沿用 TCP 发现键解析端点，端点非空白即视为可用。
    /// </summary>
    /// <remarks>
    /// Checks whether the target service is available: it reuses the TCP discovery key to resolve the endpoint
    /// and treats any non-whitespace endpoint as available.
    /// </remarks>
    /// <param name="serviceName">目标服务名 / Target service name</param>
    /// <returns>端点解析结果非空白时返回 true / Returns true when the resolved endpoint is non-whitespace</returns>
    public bool IsServiceAvailable(string serviceName)
    {
        // 当前沿用现有发现键做可用性预判，后续接入 KCP 专属端点后可替换。
        var endpoint = _endpointResolver.ResolveTcpEndpoint(serviceName);
        return !string.IsNullOrWhiteSpace(endpoint);
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
