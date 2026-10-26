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


namespace GameFrameX.Discovery;

/// <summary>
/// 服务实例描述符（路由表与心跳文档共用的数据模型）。
/// </summary>
/// <remarks>
/// The data model shared by heartbeat documents and the dual-view route table:
/// one descriptor identifies one process hosting one role: who it is
/// (<see cref="Role"/> + <see cref="InstanceId"/>), where it is reachable
/// (<see cref="AdvertiseEndpoint"/>, stored unparsed by design), what state it is in
/// (<see cref="Status"/>), how loaded it is (<see cref="Load"/>), which host shape the
/// address uses (<see cref="AddressKind"/>), and the restart epoch
/// (<see cref="Incarnation"/> — changed on restart so the watcher can tell
/// "same instance id came back" from "the old instance recovered").
/// The descriptor is immutable after construction.
/// </remarks>
public sealed class InstanceDescriptor
{
    /// <summary>
    /// 初始化服务实例描述符。
    /// </summary>
    /// <remarks>
    /// Initializes the descriptor.
    /// </remarks>
    /// <param name="role">承载的 Role 名（服务器类型名）/ The hosted role (server type name)</param>
    /// <param name="instanceId">实例唯一标识 / The unique instance id</param>
    /// <param name="advertiseEndpoint">对外可达端点（未解析的 scheme://host:port，TCP 主端点、必填）/ The reachable endpoint (unparsed scheme://host:port, the required tcp primary endpoint)</param>
    /// <param name="status">实例状态 / The instance status</param>
    /// <param name="load">负载值（0–100）/ The load value (0-100)</param>
    /// <param name="addressKind">地址形态 / The address kind</param>
    /// <param name="incarnation">代数（实例身份的重启纪元；重启后变化）/ The incarnation (restart epoch of the instance identity; changes on restart)</param>
    /// <param name="lastHeartbeatUtc">最后一次心跳时间（UTC）/ The last heartbeat time (UTC)</param>
    /// <param name="additionalAdvertiseEndpoints">附加广播端点（只承载非 tcp scheme，如 kcp；null 或空集合归一化为空集合；结构非法或 tcp scheme 的条目被跳过，同 scheme 首条胜出）/ Additional advertise endpoints (non-tcp schemes only, e.g. kcp; null or an empty collection normalizes to an empty collection; structurally invalid or tcp-scheme entries are skipped, first entry per scheme wins)</param>
    public InstanceDescriptor(string role, string instanceId, string advertiseEndpoint, InstanceStatus status, int load, EndpointAddressKind addressKind, long incarnation, DateTime lastHeartbeatUtc, IReadOnlyList<string> additionalAdvertiseEndpoints = null)
    {
        Role = role;
        InstanceId = instanceId;
        AdvertiseEndpoint = advertiseEndpoint;
        Status = status;
        Load = load;
        AddressKind = addressKind;
        Incarnation = incarnation;
        LastHeartbeatUtc = lastHeartbeatUtc;
        AdditionalAdvertiseEndpoints = NormalizeAdditionalAdvertiseEndpoints(additionalAdvertiseEndpoints);
    }

    /// <summary>
    /// 获取承载的 Role 名。
    /// </summary>
    /// <remarks>
    /// Gets the hosted role name.
    /// </remarks>
    /// <value>Role 名 / The role name</value>
    public string Role { get; }

    /// <summary>
    /// 获取实例唯一标识。
    /// </summary>
    /// <remarks>
    /// Gets the unique instance id.
    /// </remarks>
    /// <value>实例标识 / The instance id</value>
    public string InstanceId { get; }

    /// <summary>
    /// 获取对外可达端点（未解析）。
    /// </summary>
    /// <remarks>
    /// Gets the reachable advertise endpoint, stored unparsed by design.
    /// </remarks>
    /// <value>scheme://host:port / The endpoint string</value>
    public string AdvertiseEndpoint { get; }

    /// <summary>
    /// 获取实例状态。
    /// </summary>
    /// <remarks>
    /// Gets the instance status.
    /// </remarks>
    /// <value>实例状态 / The status</value>
    public InstanceStatus Status { get; }

    /// <summary>
    /// 获取负载值（0–100）。
    /// </summary>
    /// <remarks>
    /// Gets the load value (0-100).
    /// </remarks>
    /// <value>负载值 / The load value</value>
    public int Load { get; }

    /// <summary>
    /// 获取地址形态。
    /// </summary>
    /// <remarks>
    /// Gets the address kind of <see cref="AdvertiseEndpoint"/>.
    /// </remarks>
    /// <value>地址形态 / The address kind</value>
    public EndpointAddressKind AddressKind { get; }

    /// <summary>
    /// 获取代数（重启纪元）。
    /// </summary>
    /// <remarks>
    /// Gets the incarnation. The same <see cref="InstanceId"/> with a different
    /// incarnation means the process restarted; the watcher then emits
    /// Offline+Online instead of Recovered (incarnation rule).
    /// </remarks>
    /// <value>代数 / The incarnation</value>
    public long Incarnation { get; }

    /// <summary>
    /// 获取最后一次心跳时间（UTC）。
    /// </summary>
    /// <remarks>
    /// Gets the last heartbeat time in UTC; the watcher judges liveness against it.
    /// </remarks>
    /// <value>最后心跳时间 / The last heartbeat time</value>
    public DateTime LastHeartbeatUtc { get; }

    /// <summary>
    /// 获取附加广播端点集合（永不为 null；只含非 tcp scheme，每种 scheme 至多一条）。
    /// </summary>
    /// <remarks>
    /// Gets the additional advertise endpoints (never null; non-tcp schemes only, at most one
    /// entry per scheme). <see cref="AdvertiseEndpoint"/> stays the tcp primary endpoint consumed
    /// by the envelope forwarding path; the additional endpoints extend the dial-time scheme
    /// selection of the RPC resolver (kcp today).
    /// </remarks>
    /// <value>附加广播端点只读集合 / The read-only additional advertise endpoints</value>
    public IReadOnlyList<string> AdditionalAdvertiseEndpoints { get; }

    /// <summary>
    /// 归一化附加广播端点集合：跳过结构非法与 tcp scheme 条目，同 scheme 首条胜出，产出永不为 null 的只读集合。
    /// </summary>
    /// <remarks>
    /// Normalizes the additional advertise endpoints: structurally invalid and tcp-scheme
    /// entries are skipped (defensive against another version's writes), the first entry per
    /// scheme wins, and the result is never null.
    /// </remarks>
    /// <param name="additionalAdvertiseEndpoints">原始附加端点集合（可为 null）/ The raw additional endpoints (may be null)</param>
    /// <returns>归一化后的只读集合 / The normalized read-only collection</returns>
    private static IReadOnlyList<string> NormalizeAdditionalAdvertiseEndpoints(IReadOnlyList<string> additionalAdvertiseEndpoints)
    {
        if (additionalAdvertiseEndpoints == null || additionalAdvertiseEndpoints.Count == 0)
        {
            return Array.Empty<string>();
        }

        List<string> normalized = null;
        var seenSchemes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var endpoint in additionalAdvertiseEndpoints)
        {
            if (string.IsNullOrWhiteSpace(endpoint))
            {
                continue;
            }

            string scheme;
            try
            {
                scheme = EndpointParser.Parse(endpoint).Scheme;
            }
            catch (EndpointFormatException)
            {
                // 防御性解析：另一个版本写入的结构非法端点不进入端点集合（与 watcher 的防御解析同口径）。
                // Defensive parsing: a structurally invalid endpoint written by another version never enters the collection (same stance as the watcher's defensive parsing).
                continue;
            }

            if (string.Equals(scheme, "tcp", StringComparison.OrdinalIgnoreCase) || !seenSchemes.Add(scheme))
            {
                continue;
            }

            normalized ??= new List<string>();
            normalized.Add(endpoint);
        }

        return (IReadOnlyList<string>)normalized ?? Array.Empty<string>();
    }
}
