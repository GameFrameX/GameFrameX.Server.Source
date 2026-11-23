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

using GameFrameX.Network.Messages;

namespace GameFrameX.Network.HTTP;

/// <summary>
/// HTTP 处理管线统一请求上下文，承载客户端 IP、请求 URL、参数字典、消息对象与请求消息对象，
/// 以及请求头快照、HTTP 方法、Content-Type、追踪标识、原始查询串、Cookie、Scheme、本机判定与请求中断令牌。
/// </summary>
/// <remarks>
/// Unified request context for the HTTP handling pipeline, carrying the client IP, request URL, parameter dictionary, message object, and request message object,
/// plus the request header snapshot, HTTP method, Content-Type, trace identifier, raw query string, cookies, scheme, loopback flag, and request-abort token.
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
    /// 获取请求头快照（键不区分大小写；同名多值以逗号合并），供业务处理器读取鉴权头、User-Agent 等原样信息。
    /// </summary>
    /// <remarks>
    /// Gets the request header snapshot (case-insensitive keys; multiple values of the same name joined by comma), allowing business handlers to read authorization headers, User-Agent, etc. verbatim.
    /// </remarks>
    /// <value>请求头快照 / Request header snapshot</value>
    public Dictionary<string, string> Headers { get; init; }

    /// <summary>
    /// 获取 HTTP 请求方法（GET/POST 等）。
    /// </summary>
    /// <remarks>
    /// Gets the HTTP request method (GET/POST, etc.).
    /// </remarks>
    /// <value>HTTP 请求方法 / HTTP request method</value>
    public string Method { get; init; }

    /// <summary>
    /// 获取请求体的 Content-Type；无请求体时为 null。
    /// </summary>
    /// <remarks>
    /// Gets the Content-Type of the request body; null when the request has no body.
    /// </remarks>
    /// <value>请求体 Content-Type / Request body Content-Type</value>
    public string ContentType { get; init; }

    /// <summary>
    /// 获取请求的追踪标识（关联标识），用于日志与链路追踪对齐。
    /// </summary>
    /// <remarks>
    /// Gets the trace identifier (correlation identifier) of the request, used for log and tracing alignment.
    /// </remarks>
    /// <value>追踪标识 / Trace identifier</value>
    public string TraceIdentifier { get; init; }

    /// <summary>
    /// 获取原始查询串（含前导 '?'；无查询参数时为空串），供签名校验与原样日志使用。
    /// </summary>
    /// <remarks>
    /// Gets the raw query string (with leading '?'; empty when absent), for signature validation and verbatim logging.
    /// </remarks>
    /// <value>原始查询串 / Raw query string</value>
    public string QueryString { get; init; }

    /// <summary>
    /// 获取解析后的请求 Cookie（键不区分大小写）。
    /// </summary>
    /// <remarks>
    /// Gets the parsed request cookies (case-insensitive keys).
    /// </remarks>
    /// <value>请求 Cookie 字典 / Request cookie dictionary</value>
    public IReadOnlyDictionary<string, string> Cookies { get; init; }

    /// <summary>
    /// 获取请求协议方案（http/https），供构造回调绝对地址。
    /// </summary>
    /// <remarks>
    /// Gets the request scheme (http/https), for building absolute callback URLs.
    /// </remarks>
    /// <value>请求方案 / Request scheme</value>
    public string Scheme { get; init; }

    /// <summary>
    /// 获取请求是否来自本机回环地址（GM / 运维端点信任本机的判定依据）。
    /// </summary>
    /// <remarks>
    /// Gets whether the request originates from a loopback address (basis for trusting local GM / operation endpoints).
    /// </remarks>
    /// <value>是否本机请求 / Whether the request is local</value>
    public bool IsLocal { get; init; }

    /// <summary>
    /// 获取请求中断令牌：客户端断开连接时触发，长轮询 / 慢处理器应观测该令牌提前释放资源。
    /// </summary>
    /// <remarks>
    /// Gets the request-abort token: fired when client disconnects; long-poll or slow handlers should observe it to release early.
    /// </remarks>
    /// <value>请求中断令牌 / Request abort token</value>
    public CancellationToken RequestAborted { get; init; }

    /// <summary>
    /// 获取 ProtoBuf 形态的消息对象。
    /// </summary>
    /// <remarks>
    /// Gets the message object of the ProtoBuf pipeline form.
    /// </remarks>
    /// <value>消息对象 / Message object</value>
    public MessageObject MessageObject { get; init; }

    /// <summary>
    /// 获取 <see cref="HttpMessageMappingAttribute.RequestType"/> 声明的强类型请求消息对象。
    /// </summary>
    /// <remarks>
    /// Gets the request message object declared by <see cref="HttpMessageMappingAttribute.RequestType"/>.
    /// </remarks>
    /// <value>请求消息对象 / Request message object</value>
    public IHttpMessageRequest Request { get; init; }
}
