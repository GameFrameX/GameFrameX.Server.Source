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
/// 端点主机形态（C143d D15 addressKind 字段）。
/// </summary>
/// <remarks>
/// The host address kind carried by heartbeat documents (C143d D15 addressKind field).
/// The DNS name is the first-class default per D15: advertised addresses prefer
/// stable DNS names (container name / Kubernetes Service name / domain) over
/// ephemeral IP literals so restarts keep a routable identity.
/// </remarks>
public enum EndpointAddressKind
{
    /// <summary>
    /// 域名/容器名/Kubernetes Service 名（一等缺省形态）。
    /// </summary>
    /// <remarks>
    /// Domain, container name, or Kubernetes Service name (the first-class default).
    /// </remarks>
    DnsName = 1,

    /// <summary>
    /// IPv4 字面量。
    /// </summary>
    /// <remarks>
    /// An IPv4 literal.
    /// </remarks>
    IPv4 = 2,

    /// <summary>
    /// IPv6 字面量（方括号形式）。
    /// </summary>
    /// <remarks>
    /// An IPv6 literal (bracketed form).
    /// </remarks>
    IPv6 = 3,
}
