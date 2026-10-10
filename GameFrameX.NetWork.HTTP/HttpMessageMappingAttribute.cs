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

using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.NetWork.HTTP;

/// <summary>
/// HTTP 消息处理器特性，用于标记 HTTP 消息处理器类。
/// </summary>
/// <remarks>
/// HTTP message handler attribute for marking HTTP message handler classes.
/// 本特性是 HTTP 处理器的唯一声明入口：路由命令派生、HTTP 方法、强类型请求 / 响应消息类型均在此声明（请求 / 响应为可选命名属性）。
/// </remarks>
[AttributeUsage(AttributeTargets.Class)]
public sealed class HttpMessageMappingAttribute : Attribute
{
    /// <summary>
    /// 处理器命名前缀常量。
    /// </summary>
    /// <remarks>
    /// Handler naming prefix constant.
    /// </remarks>
    /// <value>处理器命名前缀 / Handler naming prefix</value>
    public const string HTTPprefix = "";

    /// <summary>
    /// 处理器命名后缀常量。
    /// </summary>
    /// <remarks>
    /// Handler naming suffix constant.
    /// </remarks>
    /// <value>处理器命名后缀 / Handler naming suffix</value>
    public const string HTTPsuffix = "HttpHandler";

    /// <summary>
    /// 初始化 <see cref="HttpMessageMappingAttribute"/> 的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of <see cref="HttpMessageMappingAttribute"/>.
    /// 消息类型校验在构造函数内完成（特性物化即触发，启动期路由注册 / Swagger 扫描 fail-fast，异常不被反射包装）。
    /// </remarks>
    /// <param name="classType">处理器类的类型 / Handler class type</param>
    /// <param name="requestType">强类型请求消息类型，可选，须继承 <see cref="HttpMessageRequestBase"/>；仅声明响应时传 <c>null</c> 占位 / Typed request message type, optional, must inherit from <see cref="HttpMessageRequestBase"/>; pass <c>null</c> as placeholder when only a response type is declared</param>
    /// <param name="responseType">强类型响应消息类型，可选，须继承 <see cref="HttpMessageResponseBase"/> / Typed response message type, optional, must inherit from <see cref="HttpMessageResponseBase"/></param>
    /// <exception cref="ArgumentNullException">当 <paramref name="classType"/> 为 <c>null</c> 时抛出 / Thrown when <paramref name="classType"/> is <c>null</c></exception>
    /// <exception cref="InvalidOperationException">当 <paramref name="classType"/> 不是密封类或不以 <see cref="HTTPsuffix"/> 结尾时抛出 / Thrown when <paramref name="classType"/> is not sealed or does not end with <see cref="HTTPsuffix"/></exception>
    /// <exception cref="InvalidCastException">当 <paramref name="requestType"/> 未继承 <see cref="HttpMessageRequestBase"/> 或 <paramref name="responseType"/> 未继承 <see cref="HttpMessageResponseBase"/> 时抛出 / Thrown when <paramref name="requestType"/> does not inherit from <see cref="HttpMessageRequestBase"/> or <paramref name="responseType"/> does not inherit from <see cref="HttpMessageResponseBase"/></exception>
    public HttpMessageMappingAttribute(Type classType, Type requestType = null, Type responseType = null)
    {
        ArgumentNullException.ThrowIfNull(classType);
        var className = classType.Name;
        if (!classType.IsSealed)
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.NetWorkHttp.ClassMustBeSealed, className));
        }

        if (!className.EndsWith(HTTPsuffix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.NetWorkHttp.ClassMustEndWithSuffix, className, HTTPsuffix));
        }

        OriginalCmd = className.Substring(HTTPprefix.Length, className.Length - HTTPprefix.Length - HTTPsuffix.Length);
        StandardCmd = OriginalCmd.ConvertToSnakeCase();

        if (requestType != null && !requestType.IsSubclassOf(typeof(HttpMessageRequestBase)))
        {
            throw new InvalidCastException(LocalizationService.GetString(Localization.Keys.NetWorkHttp.MessageTypeInheritanceError, requestType.Name));
        }

        if (responseType != null && !responseType.IsSubclassOf(typeof(HttpMessageResponseBase)))
        {
            throw new InvalidCastException(LocalizationService.GetString(Localization.Keys.NetWorkHttp.ResponseMessageTypeInheritanceError, responseType.Name));
        }

        RequestType = requestType;
        ResponseType = responseType;
    }

    /// <summary>
    /// 获取原始命令名称。
    /// </summary>
    /// <remarks>
    /// Gets the original command name extracted from the handler class name.
    /// </remarks>
    /// <value>原始命令名称 / Original command name</value>
    public string OriginalCmd { get; }

    /// <summary>
    /// 获取标准化后的命令名称（蛇形命名）。
    /// </summary>
    /// <remarks>
    /// Gets the standardized command name (snake case).
    /// </remarks>
    /// <value>标准化后的命令名称 / Standardized command name</value>
    public string StandardCmd { get; }

    /// <summary>
    /// 获取或设置 HTTP 请求方法类型，默认为 POST。
    /// </summary>
    /// <remarks>
    /// Gets or sets the HTTP request method type, defaults to POST.
    /// </remarks>
    /// <value>HTTP 请求方法类型 / HTTP request method type</value>
    public HttpMethodType HttpMethod { get; init; } = HttpMethodType.POST;

    /// <summary>
    /// 获取强类型请求消息的类型；未设置（<c>null</c>）时处理器走普通 JSON 执行路径，设置后走请求绑定 + DataAnnotations 校验路径。
    /// </summary>
    /// <remarks>
    /// Gets the typed request message type. When <c>null</c> (default) the handler takes the plain JSON execution path; when set, the typed request-binding path with DataAnnotations validation applies. 类型合法性在构造函数校验。
    /// </remarks>
    /// <value>请求消息类型 / Request message type</value>
    public Type RequestType { get; }

    /// <summary>
    /// 获取强类型响应消息的类型；未设置（<c>null</c>）时 Swagger 文档的 data 字段使用通用对象。
    /// </summary>
    /// <remarks>
    /// Gets the typed response message type. When <c>null</c> (default) the Swagger document falls back to a generic object for the data field. 本类型仅用于 OpenAPI 文档生成，运行时不消费；类型合法性在构造函数校验。
    /// </remarks>
    /// <value>响应消息类型 / Response message type</value>
    public Type ResponseType { get; }
}