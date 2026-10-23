// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to violate the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// 传输协议适配器注册表：scheme（小写归一）→ <see cref="ITransportProtocolAdapter"/>。
/// </summary>
/// <remarks>
/// Transport protocol adapter registry mapping a scheme (normalized to lower case) to an
/// <see cref="ITransportProtocolAdapter"/>. The dial-time dispatcher selects the adapter by the
/// endpoint's scheme; the registry contains only explicitly registered adapters, plus an
/// optional universal adapter for the legacy single-selection factory semantics
/// (<c>RemoteMessagingBuilder.WithTransportAdapterFactory</c>), which serves every scheme
/// without triggering the tcp fallback.
/// </remarks>
internal sealed class TransportProtocolAdapterRegistry
{
    private readonly Dictionary<string, ITransportProtocolAdapter> _adapters = new(StringComparer.OrdinalIgnoreCase);
    private ITransportProtocolAdapter _universalAdapter;

    /// <summary>
    /// 注册指定 scheme 的适配器（重复注册同一 scheme 时后者覆盖前者）。
    /// </summary>
    /// <remarks>
    /// Registers an adapter for the given scheme (normalized to lower case; re-registering
    /// the same scheme overwrites the previous adapter).
    /// </remarks>
    /// <param name="scheme">传输 scheme（tcp/kcp/ws/wss，大小写不敏感）/ The transport scheme (tcp/kcp/ws/wss, case-insensitive)</param>
    /// <param name="adapter">传输协议适配器 / The transport protocol adapter</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="adapter"/> 为 null 时抛出 / Thrown when adapter is null</exception>
    /// <exception cref="ArgumentException">当 <paramref name="scheme"/> 为 null 或空串时抛出 / Thrown when scheme is null or empty</exception>
    public void Register(string scheme, ITransportProtocolAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        ArgumentException.ThrowIfNullOrEmpty(scheme);

        _adapters[scheme.Trim().ToLowerInvariant()] = adapter;
    }

    /// <summary>
    /// 注册通用适配器（旧「全局唯一传输」语义）：命中任意 scheme，不触发回落。
    /// </summary>
    /// <remarks>
    /// Registers the universal adapter (the legacy "globally unique transport" semantics):
    /// it serves every scheme and never triggers the tcp fallback.
    /// </remarks>
    /// <param name="adapter">通用传输协议适配器 / The universal transport protocol adapter</param>
    /// <exception cref="ArgumentNullException">当 <paramref name="adapter"/> 为 null 时抛出 / Thrown when adapter is null</exception>
    public void RegisterUniversal(ITransportProtocolAdapter adapter)
    {
        ArgumentNullException.ThrowIfNull(adapter);

        _universalAdapter = adapter;
    }

    /// <summary>
    /// 按 scheme 查找适配器；存在通用适配器时任意 scheme 均命中。
    /// </summary>
    /// <remarks>
    /// Looks up the adapter for a scheme; when a universal adapter is registered it serves
    /// every scheme.
    /// </remarks>
    /// <param name="scheme">小写归一后的传输 scheme / The lower-cased transport scheme</param>
    /// <param name="adapter">命中的适配器 / The matched adapter</param>
    /// <returns>命中返回 true；否则 false / true when matched; otherwise false</returns>
    public bool TryGet(string scheme, out ITransportProtocolAdapter adapter)
    {
        if (_universalAdapter != null)
        {
            adapter = _universalAdapter;
            return true;
        }

        return _adapters.TryGetValue(scheme, out adapter);
    }

    /// <summary>
    /// 获取全部已注册适配器（去重后的快照，用于 Dispose/Invalidate 广播）。
    /// </summary>
    /// <remarks>
    /// Gets all registered adapters (a de-duplicated snapshot, used for Dispose/Invalidate
    /// broadcasting). Exposed as a method rather than a property because every call
    /// allocates a fresh snapshot (Sonar S2365: properties should not copy collections).
    /// </remarks>
    /// <returns>去重后的适配器数组 / The de-duplicated adapter array</returns>
    public ITransportProtocolAdapter[] GetAdapters()
    {
        if (_universalAdapter != null)
        {
            return new[] { _universalAdapter };
        }

        var distinct = new HashSet<ITransportProtocolAdapter>();
        foreach (var adapter in _adapters.Values)
        {
            distinct.Add(adapter);
        }

        return distinct.ToArray();
    }
}
