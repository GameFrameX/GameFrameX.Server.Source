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


using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Http.Normalization;
using GameFrameX.Foundation.Json;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.Messages;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.Foundation.Logger;
using GameFrameX.Utility.Runtime;
using GameFrameX.Utility.Setting;
using Microsoft.AspNetCore.Http;
using GameFrameX.Foundation.Localization.Core;

namespace GameFrameX.NetWork.HTTP;

/// <summary>
/// HTTP 处理器静态类，用于处理 HTTP 请求。
/// </summary>
/// <remarks>
/// HTTP handler static class for processing HTTP requests.
/// Provides unified request handling for JSON and ProtoBuf content types.
/// </remarks>
public static class HttpHandler
{
    private const string JsonContentType = "application/json; charset=utf-8";
    private const string ProtoBufContentType = "application/x-protobuf";
    private const int RequestBodyBufferSize = 81920;

    /// <summary>
    /// 处理 HTTP 请求。
    /// </summary>
    /// <remarks>
    /// Handles HTTP requests with support for JSON and ProtoBuf content types.
    /// </remarks>
    /// <param name="context">HTTP 上下文 / HTTP context</param>
    /// <param name="baseHandler">基础 HTTP 处理器工厂方法，根据命令名称返回对应的处理器 / Base HTTP handler factory method that returns the corresponding handler based on command name</param>
    /// <param name="aopHandlerTypes">AOP 处理器列表，可选 / AOP handler list, optional</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    public static async Task HandleRequest(HttpContext context, Func<string, BaseHttpHandler> baseHandler, List<IHttpAopHandler> aopHandlerTypes = null)
    {
        var ip = context.Connection.RemoteIpAddress?.ToString();
        string url = context.Request.PathBase + context.Request.Path;
        var command = context.Request.Path.ToString().Substring(GlobalSettings.CurrentSetting.HttpUrl.Length);
        var logHeader = LocalizationService.GetString(Localization.Keys.NetWorkHttp.RequestLogHeader, context.TraceIdentifier, ip, url);
        if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp && GlobalSettings.CurrentSetting.IsDebugHttpRequest)
        {
            LogHelper.Debug("HTTP RequestMethod {logHeader} {method}", logHeader, context.Request.Method);
        }

        try
        {
            var paramMap = new Dictionary<string, object>();
            var queryStringParamCount = ExtractQueryParameters(context, paramMap);

            context.Response.Headers.ContentType = JsonContentType;
            var isGetRequest = HttpMethods.IsGet(context.Request.Method);

            var parseResult = await TryParseRequestBodyAsync(context, paramMap, isGetRequest);
            if (parseResult.ShouldAbort)
            {
                return;
            }

            var message = parseResult.Message;
            var jsonBody = parseResult.JsonBody;
            var isProtoBuf = parseResult.IsProtoBuf;

            // 记录请求参数
            if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp && GlobalSettings.CurrentSetting.IsDebugHttpRequest && paramMap.Count > 0)
            {
                LogHelper.Debug<string>("HTTP RequestParameters {parameters}", JsonHelper.Serialize(paramMap));
            }

            var validation = await TryValidateAndResolveHandlerAsync(context, baseHandler, command, ip, url, paramMap, aopHandlerTypes);
            if (!validation.IsValid)
            {
                return;
            }

            var handler = validation.Handler;

            // 执行处理器逻辑
            if (isProtoBuf)
            {
                await ExecuteProtoBufAsync(context, handler, ip, url, paramMap, message, logHeader);
            }
            else
            {
                var httpRequestAttr = handler.GetType().GetCustomAttribute<HttpMessageRequestAttribute>();
                if (httpRequestAttr == null)
                {
                    await ExecutePlainJsonAsync(context, handler, ip, url, paramMap, logHeader);
                }
                else
                {
                    var messageRequest = BuildHttpMessageRequest(httpRequestAttr, paramMap, jsonBody, queryStringParamCount);
                    await ExecuteJsonMessageRequestAsync(context, handler, ip, url, messageRequest, logHeader);
                }
            }
        }
        catch (Exception e)
        {
            LogHelper.Error("HTTP JSON ExceptionOccurred {logHeader} {message} {stackTrace}", logHeader, e.Message, e.StackTrace);
            await context.Response.WriteAsync(HttpJsonResultData<string>.FailString(e.Message));
        }
    }

    // 从查询字符串中提取参数并返回参数个数 / Extract parameters from the query string and return the count.
    private static int ExtractQueryParameters(HttpContext context, Dictionary<string, object> paramMap)
    {
        var queryStringParamCount = 0;
        foreach (var keyValuePair in context.Request.Query)
        {
            paramMap.Add(keyValuePair.Key, keyValuePair.Value.ToString());
            queryStringParamCount++;
        }

        return queryStringParamCount;
    }

    // 解析请求体：GET 跳过 Body；ProtoBuf 反序列化；JSON 合并参数。返回解析结果，ShouldAbort=true 表示已写出错误响应、调用方应直接 return。
    // Parse the request body: GET skips the body; ProtoBuf deserializes; JSON merges parameters. ShouldAbort=true means an error response was written and the caller should return.
    private static async Task<(bool ShouldAbort, MessageObject Message, string JsonBody, bool IsProtoBuf)> TryParseRequestBodyAsync(HttpContext context, Dictionary<string, object> paramMap, bool isGetRequest)
    {
        var contentType = context.Request.ContentType;

        // GET 请求允许没有 ContentType
        if (contentType.IsNullOrWhiteSpace() && !isGetRequest)
        {
            await context.Response.WriteAsync(LocalizationService.GetString(Localization.Keys.NetWorkHttp.HttpHeaderContentTypeNull));
            return (true, null, null, false);
        }

        var isProtoBuf = !isGetRequest && contentType.Equals(ProtoBufContentType, StringComparison.OrdinalIgnoreCase);

        // GET 请求只使用 Query String 参数，不需要处理 Body
        if (isGetRequest)
        {
            return (false, null, null, false);
        }

        if (isProtoBuf)
        {
            var maxBodyBytes = GetEffectiveRequestBodyLimit(GlobalSettings.CurrentSetting.HttpMaxProtoBodyBytes);
            var readResult = await TryReadRequestBodyBytes(context, maxBodyBytes);
            if (!readResult.Success)
            {
                return (true, null, null, false);
            }

            var buffer = readResult.Buffer;
            var messageObjectHttp = ProtoBufSerializerHelper.Deserialize<MessageHttpObject>(buffer);
            var messageType = MessageProtoHelper.GetMessageTypeById(messageObjectHttp.Id);
            var message = (MessageObject)ProtoBufSerializerHelper.Deserialize(messageObjectHttp.Body, messageType);
            message.SetMessageId(messageObjectHttp.Id);
            message.SetUniqueId(messageObjectHttp.UniqueId);
            return (false, message, null, true);
        }

        if (!context.Request.HasJsonContentType())
        {
            await context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(GameHttpStatusCode.ParamErr, LocalizationService.GetString(Localization.Keys.NetWorkHttp.UnsupportedContentType, contentType)));
            return (true, null, null, false);
        }

        var maxJsonBodyBytes = GetEffectiveRequestBodyLimit(GlobalSettings.CurrentSetting.HttpMaxJsonBodyBytes);
        var jsonReadResult = await TryReadRequestBodyBytes(context, maxJsonBodyBytes);
        if (!jsonReadResult.Success)
        {
            return (true, null, null, false);
        }

        var jsonBody = Encoding.UTF8.GetString(jsonReadResult.Buffer);
        var jsonKv = JsonHelper.Deserialize<Dictionary<string, object>>(jsonBody);
        foreach (var keyValuePair in jsonKv)
        {
            if (!paramMap.TryAdd(keyValuePair.Key, keyValuePair.Value))
            {
                // 参数Key发生重复
                await context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(GameHttpStatusCode.ParamErr, LocalizationService.GetString(Localization.Keys.NetWorkHttp.ParameterDuplicate, keyValuePair.Key)));
                return (true, null, null, false);
            }
        }

        return (false, null, jsonBody, false);
    }

    // 依次执行 AOP 处理器，任一返回 false 则停止并返回 false（等价于原 return 短路）/ Run AOP handlers in order; return false on the first failure (equivalent to the original short-circuit return).
    private static bool RunAopHandlers(HttpContext context, string ip, string url, Dictionary<string, object> paramMap, List<IHttpAopHandler> aopHandlerTypes)
    {
        if (aopHandlerTypes is { Count: > 0, })
        {
            foreach (var httpAopHandler in aopHandlerTypes)
            {
                if (!httpAopHandler.Run(context, ip, url, paramMap))
                {
                    return false;
                }
            }
        }

        return true;
    }

    // 指令空校验 + 运行态校验 + AOP + 处理器解析 + 签名校验：任一失败即写出对应错误响应并返回 (false, null)，全通过返回 (true, handler)。
    // Validate command + runtime state, run AOP, resolve handler, verify signature: on any failure write the corresponding error response and return (false, null); on full pass return (true, handler).
    private static async Task<(bool IsValid, BaseHttpHandler Handler)> TryValidateAndResolveHandlerAsync(HttpContext context, Func<string, BaseHttpHandler> baseHandler, string command, string ip, string url, Dictionary<string, object> paramMap, List<IHttpAopHandler> aopHandlerTypes)
    {
        // 检查指令是否有效
        if (command.IsNullOrEmptyOrWhiteSpace())
        {
            await context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(GameHttpStatusCode.Undefined, HttpStatusMessage.UndefinedCommand));
            return (false, null);
        }

        if (!GameAppRuntime.IsRunning)
        {
            await context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(GameHttpStatusCode.ActionFailed, LocalizationService.GetString(Localization.Keys.NetWorkHttp.ServerStatusError)));
            return (false, null);
        }

        #region AOP

        // 执行AOP处理器
        if (!RunAopHandlers(context, ip, url, paramMap, aopHandlerTypes))
        {
            return (false, null);
        }

        #endregion

        // 获取并执行对应的HTTP处理器
        var handler = baseHandler(command);
        if (handler == null)
        {
            LogHelper.Warning<string>("HTTP CommandHandlerNotFound {command}", LocalizationService.GetString(Localization.Keys.NetWorkHttp.CommandHandlerNotFound, command));
            await context.Response.WriteAsync(HttpJsonResultData<string>.NotFoundString());
            return (false, null);
        }

        // 验证签名
        var isChecked = handler.CheckSign(paramMap, out var error);
        if (isChecked == false)
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsync(error);
            return (false, null);
        }

        return (true, handler);
    }

    // ProtoBuf 执行：调用处理器 + 调试执行耗时日志 + result 非空时序列化 MessageHttpObject 写出（含编码异常 try/catch 日志）。
    // ProtoBuf execution: invoke handler + debug timing log + when result is non-null serialize MessageHttpObject and write (with encoding-exception try/catch logging).
    private static async Task ExecuteProtoBufAsync(HttpContext context, BaseHttpHandler handler, string ip, string url, Dictionary<string, object> paramMap, MessageObject message, string logHeader)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var result = await handler.Action(ip, url, paramMap, message);
        stopwatch.Stop();
        if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp && GlobalSettings.CurrentSetting.IsDebugHttpResponse)
        {
            LogHelper.Debug("HTTP ProtoBuf ExecutionTime {logHeader} {elapsedMilliseconds} {result}", logHeader, stopwatch.ElapsedMilliseconds, result);
        }
        else if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp)
        {
            LogHelper.Debug("HTTP ProtoBuf ExecutionTime {logHeader} {elapsedMilliseconds}", logHeader, stopwatch.ElapsedMilliseconds);
        }

        if (result.IsNotNull())
        {
            try
            {
                ReadOnlyMemory<byte> body = ProtoBufSerializerHelper.Serialize(result);
                var messageHttpObject = new MessageHttpObject { Id = MessageProtoHelper.GetMessageIdByType(result), UniqueId = message.UniqueId, Body = body.ToArray(), };
                var resultResponse = ProtoBufSerializerHelper.Serialize(messageHttpObject);
                context.Response.ContentLength = resultResponse.Length;
                await context.Response.BodyWriter.WriteAsync(resultResponse);
            }
            catch (Exception e)
            {
                LogHelper.Error<string>("HTTP ProtoBuf MessageEncodingException {exception}", e.ToString());
            }
        }
    }

    // 按是否有 Query String 参数 / 原始 JSON Body 选择反序列化路径，构造请求消息基类 / Choose the deserialization path based on query-string params and raw JSON body, building the request message base.
    private static HttpMessageRequestBase BuildHttpMessageRequest(HttpMessageRequestAttribute httpRequestAttr, Dictionary<string, object> paramMap, string jsonBody, int queryStringParamCount)
    {
        // 优化：如果没有 Query String 参数，直接使用原始 JSON 字符串反序列化，避免重复序列化
        if (queryStringParamCount == 0 && !string.IsNullOrEmpty(jsonBody))
        {
            // 直接使用原始 JSON 字符串反序列化
            return (HttpMessageRequestBase)JsonHelper.Deserialize(jsonBody, httpRequestAttr.MessageType);
        }

        // 有 Query String 参数时，需要合并参数
        return (HttpMessageRequestBase)JsonHelper.Deserialize(JsonHelper.Serialize(paramMap), httpRequestAttr.MessageType);
    }

    // 标注 HttpMessageRequestAttribute 的 JSON 执行：Validator 校验，通过则执行 + 日志 + 写出，否则写出校验错误 / JSON execution for handlers annotated with HttpMessageRequestAttribute: validate, on pass execute + log + write, otherwise write validation errors.
    private static async Task ExecuteJsonMessageRequestAsync(HttpContext context, BaseHttpHandler handler, string ip, string url, HttpMessageRequestBase httpMessageRequestBase, string logHeader)
    {
        var validationResults = new List<ValidationResult>();
        var validationContext = new ValidationContext(httpMessageRequestBase, null, null);
        var isValid = Validator.TryValidateObject(httpMessageRequestBase, validationContext, validationResults, true);
        if (isValid)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var result = await handler.Action(ip, url, httpMessageRequestBase);
            stopwatch.Stop();
            if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp && GlobalSettings.CurrentSetting.IsDebugHttpResponse)
            {
                LogHelper.Debug("HTTP JSON ExecutionTime {logHeader} {elapsedMilliseconds} {result}", logHeader, stopwatch.ElapsedMilliseconds, result);
            }
            else if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp)
            {
                LogHelper.Debug("HTTP JSON ExecutionTime {logHeader} {elapsedMilliseconds}", logHeader, stopwatch.ElapsedMilliseconds);
            }

            await context.Response.WriteAsync(result);
        }
        else
        {
            if (validationResults.Count > 0)
            {
                await context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(400, validationResults[0].ErrorMessage));
            }
            else
            {
                await context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(400, LocalizationService.GetString(Localization.Keys.NetWorkHttp.DataVerificationFailed)));
            }
        }
    }

    // 未标注特性的 JSON 执行：调用处理器 + 调试执行耗时日志 + 写出 / Plain JSON execution (no attribute): invoke handler + debug timing log + write.
    private static async Task ExecutePlainJsonAsync(HttpContext context, BaseHttpHandler handler, string ip, string url, Dictionary<string, object> paramMap, string logHeader)
    {
        var stopwatch = new Stopwatch();
        stopwatch.Start();
        var result = await handler.Action(ip, url, paramMap);
        stopwatch.Stop();
        if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp && GlobalSettings.CurrentSetting.IsDebugHttpResponse)
        {
            LogHelper.Debug("HTTP JSON ExecutionTime {logHeader} {elapsedMilliseconds} {result}", logHeader, stopwatch.ElapsedMilliseconds, result);
        }
        else if (GlobalSettings.CurrentSetting.IsDebug && GlobalSettings.CurrentSetting.IsDebugHttp)
        {
            LogHelper.Debug("HTTP JSON ExecutionTime {logHeader} {elapsedMilliseconds}", logHeader, stopwatch.ElapsedMilliseconds);
        }

        await context.Response.WriteAsync(result);
    }

    private static long GetEffectiveRequestBodyLimit(long contentTypeLimit)
    {
        var requestLimit = GlobalSettings.CurrentSetting.HttpMaxRequestBodyBytes;
        if (requestLimit <= 0 || contentTypeLimit <= 0)
        {
            return 0;
        }

        return Math.Min(requestLimit, contentTypeLimit);
    }

    private static async Task<(bool Success, byte[] Buffer)> TryReadRequestBodyBytes(HttpContext context, long maxBodyBytes)
    {
        if (context.Request.ContentLength > maxBodyBytes)
        {
            await WriteRequestBodyTooLarge(context, maxBodyBytes);
            return (false, null);
        }

        using var memoryStream = new MemoryStream();
        var readBuffer = new byte[RequestBodyBufferSize];
        long totalRead = 0;
        while (true)
        {
            var read = await context.Request.Body.ReadAsync(readBuffer);
            if (read == 0)
            {
                break;
            }

            totalRead += read;
            if (totalRead > maxBodyBytes)
            {
                await WriteRequestBodyTooLarge(context, maxBodyBytes);
                return (false, null);
            }

            memoryStream.Write(readBuffer, 0, read);
        }

        return (true, memoryStream.ToArray());
    }

    private static Task WriteRequestBodyTooLarge(HttpContext context, long maxBodyBytes)
    {
        context.Response.StatusCode = StatusCodes.Status413PayloadTooLarge;
        return context.Response.WriteAsync(HttpJsonResultData<string>.ErrorString(StatusCodes.Status413PayloadTooLarge, $"HTTP request body is too large. Max allowed bytes: {maxBodyBytes}."));
    }
}
