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


namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// 解析后的端点三元组（C143d D15：scheme / host / port）。
/// </summary>
/// <remarks>
/// The parsed endpoint triple (C143d D15: scheme / host / port).
/// Endpoints are stored unparsed everywhere and parsed only at connect time
/// (design source D15 "存储不解析、连接时才解析"); this type is the single
/// result shape of <see cref="EndpointParser.Parse"/>. The instance is immutable.
/// </remarks>
public sealed class ParsedEndpoint
{
    /// <summary>
    /// 初始化解析后的端点。
    /// </summary>
    /// <remarks>
    /// Initializes the parsed endpoint. Use <see cref="EndpointParser.Parse"/> instead of
    /// calling this constructor directly so every endpoint goes through the same validation.
    /// </remarks>
    /// <param name="scheme">协议 scheme（小写，如 tcp/kcp/ws/wss）/ The lower-cased scheme (e.g. tcp/kcp/ws/wss)</param>
    /// <param name="host">主机（域名/容器名/Kubernetes Service 名/IPv4/IPv6 字面量，IPv6 不带方括号）/ The host (domain/container/Kubernetes Service name/IPv4/IPv6 literal without brackets)</param>
    /// <param name="port">端口（1–65535）/ The port (1-65535)</param>
    /// <param name="addressKind">主机形态 / The host address kind</param>
    public ParsedEndpoint(string scheme, string host, int port, EndpointAddressKind addressKind)
    {
        Scheme = scheme;
        Host = host;
        Port = port;
        AddressKind = addressKind;
    }

    /// <summary>
    /// 获取协议 scheme（小写）。
    /// </summary>
    /// <remarks>
    /// Gets the lower-cased scheme.
    /// </remarks>
    /// <value>协议 scheme / The scheme</value>
    public string Scheme { get; }

    /// <summary>
    /// 获取主机（IPv6 字面量已去掉方括号）。
    /// </summary>
    /// <remarks>
    /// Gets the host (IPv6 literals have their brackets stripped).
    /// </remarks>
    /// <value>主机 / The host</value>
    public string Host { get; }

    /// <summary>
    /// 获取端口。
    /// </summary>
    /// <remarks>
    /// Gets the port.
    /// </remarks>
    /// <value>端口 / The port</value>
    public int Port { get; }

    /// <summary>
    /// 获取主机形态。
    /// </summary>
    /// <remarks>
    /// Gets the host address kind.
    /// </remarks>
    /// <value>主机形态 / The address kind</value>
    public EndpointAddressKind AddressKind { get; }

    /// <inheritdoc />
    public override string ToString()
    {
        if (AddressKind == EndpointAddressKind.IPv6)
        {
            return $"{Scheme}://[{Host}]:{Port}";
        }

        return $"{Scheme}://{Host}:{Port}";
    }
}
