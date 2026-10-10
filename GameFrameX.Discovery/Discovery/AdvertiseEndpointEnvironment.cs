// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Net;
using System.Net.Sockets;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;

namespace GameFrameX.Discovery;

/// <summary>
/// 广播端点环境引导（驱动无关单一事实源，C166 依赖纠偏抽取）。
/// </summary>
/// <remarks>
/// Advertise-endpoint environment bootstrap — the driver-neutral single source of truth shared by the
/// Mongo and PostgreSQL heartbeat registries. Both database
/// implementation assemblies reference RemoteMessaging (never the reverse), so this pure environment
/// probing logic lives here and both <c>*EndpointRegistry.CreateSelfDescriptorFromEnvironment</c>
/// implementations delegate to it, keeping their behaviour identical by construction.
/// <para>环境变量：<c>GameFrameX__AdvertiseHost</c> / <c>GameFrameX__AdvertisePort</c> / <c>GameFrameX__RoleInstanceId</c>（稳定运维契约）。</para>
/// </remarks>
public static class AdvertiseEndpointEnvironment
{
    /// <summary>
    /// 广播主机环境变量名。
    /// </summary>
    public const string AdvertiseHostEnvironmentVariable = "GameFrameX__AdvertiseHost";

    /// <summary>
    /// 广播端口环境变量名。
    /// </summary>
    public const string AdvertisePortEnvironmentVariable = "GameFrameX__AdvertisePort";

    /// <summary>
    /// 实例 ID 环境变量名。
    /// </summary>
    public const string RoleInstanceIdEnvironmentVariable = "GameFrameX__RoleInstanceId";

    /// <summary>
    /// 从环境变量构建本进程的广播描述符；未配置广播端口时返回 null（该进程只观察拓扑、不写心跳）。
    /// </summary>
    /// <remarks>
    /// Builds this process's advertise descriptor from environment variables; returns <c>null</c> when no
    /// advertise port is configured (the process only observes the topology instead of writing heartbeats).
    /// Host fallback order: explicit <c>GameFrameX__AdvertiseHost</c> → UDP egress probe (no packet sent) →
    /// DNS host table → machine name with a warning.
    /// </remarks>
    /// <param name="roleName">角色名 / Role name</param>
    /// <returns>广播描述符，或 null / The descriptor, or null</returns>
    public static InstanceDescriptor CreateSelfDescriptorFromEnvironment(string roleName)
    {
        var advertisePortText = Environment.GetEnvironmentVariable(AdvertisePortEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(advertisePortText) || !int.TryParse(advertisePortText, out var advertisePort) || advertisePort < 1 || advertisePort > 65535)
        {
            return null;
        }

        var advertiseHostText = Environment.GetEnvironmentVariable(AdvertiseHostEnvironmentVariable);
        string advertiseHost;
        EndpointAddressKind addressKind;
        if (!string.IsNullOrWhiteSpace(advertiseHostText))
        {
            advertiseHost = advertiseHostText.Trim();
            addressKind = EndpointParser.Parse($"tcp://{advertiseHost}:{advertisePort}").AddressKind;
        }
        else
        {
            (advertiseHost, addressKind) = DetectEgressAddress();
        }

        var instanceId = Environment.GetEnvironmentVariable(RoleInstanceIdEnvironmentVariable);
        if (string.IsNullOrWhiteSpace(instanceId))
        {
            instanceId = $"{roleName}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds():x}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        }

        var incarnation = DateTimeOffset.UtcNow.UtcTicks;
        // URI authority 中的 IPv6 host 必须方括号包裹：出口探测返回裸 IPv6 文本，显式配置且已带方括号的输入原样保留。
        // IPv6 hosts in a URI authority must be bracket-wrapped: the egress probe returns bare IPv6 text; explicitly configured inputs keep their brackets.
        var authorityHost = addressKind == EndpointAddressKind.IPv6 && !advertiseHost.StartsWith("[", StringComparison.Ordinal)
            ? $"[{advertiseHost}]"
            : advertiseHost;
        return new InstanceDescriptor(roleName, instanceId.Trim(), $"tcp://{authorityHost}:{advertisePort}", InstanceStatus.Booting, 0, addressKind, incarnation, DateTime.UtcNow);
    }

    /// <summary>
    /// 出口地址探测：UDP 连接外网地址（不发包）取本端 IPv4 → DNS 主机地址表 → 本机名（warning 兜底）。
    /// </summary>
    /// <returns>主机与地址类型 / The host and address kind</returns>
    private static (string Host, EndpointAddressKind AddressKind) DetectEgressAddress()
    {
        try
        {
            using (var probeSocket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp))
            {
                probeSocket.Connect("8.8.8.8", 65530);
                if (probeSocket.LocalEndPoint is IPEndPoint ipv4EndPoint && !string.IsNullOrEmpty(ipv4EndPoint.Address.ToString()))
                {
                    return (ipv4EndPoint.Address.ToString(), EndpointAddressKind.IPv4);
                }
            }
        }
        catch (Exception)
        {
            // 无外网路由（隔离网络/CI）：走主机地址表回退。
        }

        try
        {
            var hostAddresses = Dns.GetHostAddresses(Dns.GetHostName());
            foreach (var address in hostAddresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetwork)
                {
                    return (address.ToString(), EndpointAddressKind.IPv4);
                }
            }

            foreach (var address in hostAddresses)
            {
                if (address.AddressFamily == AddressFamily.InterNetworkV6)
                {
                    return (address.ToString(), EndpointAddressKind.IPv6);
                }
            }
        }
        catch (Exception)
        {
            // 主机名解析失败：最后回退本机名。
        }

        var machineName = Dns.GetHostName();
        // Localization: Discovery.AdvertiseEndpoint.EgressDetectionFallbackMachineName - [AdvertiseEndpointEnvironment] 出口地址探测失败；回退使用机器名 {0} 作为广播主机。需要跨进程路由时请显式设置 {1}
        LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Discovery.AdvertiseEndpoint.EgressDetectionFallbackMachineName, machineName, AdvertiseHostEnvironmentVariable));
        return (machineName, EndpointAddressKind.DnsName);
    }
}
