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
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.NetWork.Messages;

namespace GameFrameX.NetWork.HTTP;

/// <summary>
/// HTTP 处理管线统一请求上下文，承载客户端 IP、请求 URL、参数字典、消息对象与请求消息对象。
/// </summary>
/// <remarks>
/// Unified request context for the HTTP handling pipeline, carrying the client IP, request URL, parameter dictionary, message object, and request message object.
/// </remarks>
public sealed class HttpActionContext
{
    /// <summary>
    /// 获取客户端 IP 地址。
    /// </summary>
    /// <remarks>
    /// Gets the client IP address.
    /// </remarks>
    /// <value>客户端 IP 地址 / Client IP address</value>
    public string Ip { get; init; }

    /// <summary>
    /// 获取请求的 URL。
    /// </summary>
    /// <remarks>
    /// Gets the request URL.
    /// </remarks>
    /// <value>请求的 URL / Request URL</value>
    public string Url { get; init; }

    /// <summary>
    /// 获取请求参数字典，键为参数名，值为参数值。
    /// </summary>
    /// <remarks>
    /// Gets the request parameter dictionary with parameter names as keys and parameter values as values.
    /// </remarks>
    /// <value>请求参数字典 / Request parameter dictionary</value>
    public Dictionary<string, object> Parameters { get; init; }

    /// <summary>
    /// 获取 ProtoBuf 形态的消息对象。
    /// </summary>
    /// <remarks>
    /// Gets the message object of the ProtoBuf pipeline form.
    /// </remarks>
    /// <value>消息对象 / Message object</value>
    public MessageObject MessageObject { get; init; }

    /// <summary>
    /// 获取标注 <see cref="HttpMessageRequestAttribute"/> 形态的请求消息对象。
    /// </summary>
    /// <remarks>
    /// Gets the request message object of the form annotated with <see cref="HttpMessageRequestAttribute"/>.
    /// </remarks>
    /// <value>请求消息对象 / Request message object</value>
    public HttpMessageRequestBase Request { get; init; }
}
