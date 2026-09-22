// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨 Role 路由决策失败异常（C143c D3）。
/// </summary>
/// <remarks>
/// Thrown when the cross-role routing seam cannot decide where a message must go
/// (empty target role, target role not hosted by this process with no remote forwarder
/// configured, or a local role hit with no local dispatcher configured).
/// The router never falls back silently: an undecidable route is always a loud failure (C143c risk mitigation).
/// </remarks>
public sealed class RouteNotFoundException : Exception
{
    /// <summary>
    /// 初始化路由决策失败异常。
    /// </summary>
    /// <remarks>
    /// Initializes the exception with the target role that could not be routed.
    /// </remarks>
    /// <param name="targetRole">无法路由的目标 Role 名（可能为 null 或空白）/ The target role that could not be routed (may be null or whitespace)</param>
    /// <param name="message">失败原因描述 / The failure description</param>
    public RouteNotFoundException(string targetRole, string message)
        : base(message)
    {
        TargetRole = targetRole;
    }

    /// <summary>
    /// 初始化路由决策失败异常（含内部异常）。
    /// </summary>
    /// <remarks>
    /// Initializes the exception with the target role, a failure description and an inner exception.
    /// </remarks>
    /// <param name="targetRole">无法路由的目标 Role 名（可能为 null 或空白）/ The target role that could not be routed (may be null or whitespace)</param>
    /// <param name="message">失败原因描述 / The failure description</param>
    /// <param name="innerException">内部异常 / The inner exception</param>
    public RouteNotFoundException(string targetRole, string message, Exception innerException)
        : base(message, innerException)
    {
        TargetRole = targetRole;
    }

    /// <summary>
    /// 获取无法路由的目标 Role 名。
    /// </summary>
    /// <remarks>
    /// Gets the target role that could not be routed; may be null or whitespace
    /// when the failure was an empty role name.
    /// </remarks>
    /// <value>目标 Role 名 / The target role name</value>
    public string TargetRole { get; }
}
