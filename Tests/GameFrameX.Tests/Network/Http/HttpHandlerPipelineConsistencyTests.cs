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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.ComponentModel.DataAnnotations;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http.Features;
using GameFrameX.Foundation.Http.Normalization;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Network.Messages;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.Proto.Proto;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Runtime;
using GameFrameX.Utility.Setting;
using Microsoft.AspNetCore.Http;

namespace GameFrameX.Tests.Network.Http;

/// <summary>
/// HTTP 请求管线正确性与响应一致性回归（GFX-1180 / C458）：
/// ProtoBuf Content-Type、大小写参数重复拒绝、非对象 JSON Body 参数错误、统一 JSON envelope、
/// DataAnnotations 业务码、AOP 拒绝响应契约、CheckSign 缺参本地化消息。
/// </summary>
[Collection(GameAppRuntimeCollection.Name)]
public class HttpHandlerPipelineConsistencyTests
{
    private static readonly object SettingsLock = new();

    [Fact]
    public async Task HandleRequest_ShouldSetProtoContentType_ForProtoBufSuccess()
    {
        EnsureSettings();
        EnsureProtoMessageRegistry();
        var handlerCalled = false;
        var body = BuildProtoBufRequestBody(new ReqHeartBeat { Timestamp = 12345, });
        var context = CreateContext("application/x-protobuf", new MemoryStream(body));

        await HttpHandler.HandleRequestAsync(context, _ => new ProtoBufEchoHttpHandler(() => handlerCalled = true));

        Assert.True(handlerCalled);
        Assert.NotNull(context.Response.ContentType);
        Assert.StartsWith("application/x-protobuf", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleRequest_ShouldKeepJsonContentType_ForJsonErrorPath()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("text/plain", new MemoryStream(Encoding.UTF8.GetBytes("plain")));

        await HttpHandler.HandleRequestAsync(context, _ =>
        {
            handlerCalled = true;
            return null;
        });

        Assert.False(handlerCalled);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(GameHttpStatusCode.ParamErr, envelope.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleRequest_ShouldRejectParameter_WhenBodyKeyDiffersOnlyByCasing()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes("{\"userId\":2}")));
        context.Request.QueryString = new QueryString("?UserId=1");

        await HttpHandler.HandleRequestAsync(context, _ => new PlainJsonProbeHttpHandler(() => handlerCalled = true));

        Assert.False(handlerCalled);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(GameHttpStatusCode.ParamErr, envelope.GetProperty("code").GetInt32());
        Assert.Contains("userId", envelope.GetProperty("message").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task HandleRequest_ShouldRejectParameter_WhenExactDuplicateKeyExists()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes("{\"userId\":2}")));
        context.Request.QueryString = new QueryString("?userId=1");

        await HttpHandler.HandleRequestAsync(context, _ => new PlainJsonProbeHttpHandler(() => handlerCalled = true));

        Assert.False(handlerCalled);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(GameHttpStatusCode.ParamErr, envelope.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleRequest_ShouldExecuteHandler_WhenBodyOnlyParameterProvided()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes("{\"userId\":2}")));

        await HttpHandler.HandleRequestAsync(context, _ => new PlainJsonProbeHttpHandler(() => handlerCalled = true));

        Assert.True(handlerCalled);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[1,2]")]
    [InlineData("123")]
    [InlineData("\"text\"")]
    [InlineData("")]
    public async Task HandleRequest_ShouldReturnParamError_WhenJsonBodyIsNotAnObject(string requestBody)
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes(requestBody)));

        await HttpHandler.HandleRequestAsync(context, _ => new PlainJsonProbeHttpHandler(() => handlerCalled = true));

        Assert.False(handlerCalled);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(GameHttpStatusCode.ParamErr, envelope.GetProperty("code").GetInt32());
        Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnJsonEnvelope_WhenContentTypeMissingOnPost()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext(null, Stream.Null);

        await HttpHandler.HandleRequestAsync(context, _ =>
        {
            handlerCalled = true;
            return null;
        });

        Assert.False(handlerCalled);
        Assert.StartsWith("application/json", context.Response.ContentType, StringComparison.OrdinalIgnoreCase);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(GameHttpStatusCode.ParamErr, envelope.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnParamErrCode_WhenDataAnnotationsValidationFails()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes("{\"optionalName\":\"x\"}")));

        await HttpHandler.HandleRequestAsync(context, _ => new DataAnnotationsProbeHttpHandler(() => handlerCalled = true));

        Assert.False(handlerCalled);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(GameHttpStatusCode.ParamErr, envelope.GetProperty("code").GetInt32());
    }

    [Fact]
    public async Task HandleRequest_ShouldWriteValidationError_WhenAopRejectsWithoutResponse()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes("{}")));

        await HttpHandler.HandleRequestAsync(context, _ => new PlainJsonProbeHttpHandler(() => handlerCalled = true), new List<IHttpAopHandler> { new RejectingAopHandler(), });

        Assert.False(handlerCalled);
        var envelope = await ReadEnvelopeAsync(context);
        Assert.Equal(400, envelope.GetProperty("code").GetInt32());
        Assert.False(string.IsNullOrEmpty(envelope.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task HandleRequest_ShouldNotAppendResponse_WhenAopWritesOwnResponse()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateContext("application/json", new MemoryStream(Encoding.UTF8.GetBytes("{}")));
        // DefaultHttpContext 不跟踪 HasStarted（WriteAsync/StartAsync 均不置位，真实服务器由宿主 feature 置位），
        // 以包装 feature 模拟真实服务器上“AOP 已写出响应”的状态
        context.Features.Set<IHttpResponseFeature>(new StartedResponseFeature(context.Features.Get<IHttpResponseFeature>()));

        await HttpHandler.HandleRequestAsync(context, _ => new PlainJsonProbeHttpHandler(() => handlerCalled = true), new List<IHttpAopHandler> { new RespondingRejectAopHandler(), });

        Assert.False(handlerCalled);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using (var reader = new StreamReader(context.Response.Body, Encoding.UTF8))
        {
            var responseBody = await reader.ReadToEndAsync();
            Assert.Equal("custom-response", responseBody);
        }
    }

    [Fact]
    public void CheckSign_ShouldReturnLocalizedMessage_WhenSignParametersMissing()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = true;
            setting.HttpSignSecret = "unit-test-secret";
            var handler = new PlainJsonProbeHttpHandler(() => { });

            var isValid = handler.CheckSign(new Dictionary<string, object>(), out var error);

            Assert.False(isValid);
            Assert.Contains(GlobalConst.HttpSignKey, error, StringComparison.Ordinal);
            Assert.Contains(GlobalConst.HttpTimestampKey, error, StringComparison.Ordinal);
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    private static DefaultHttpContext CreateContext(string contentType, Stream body)
    {
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/game/api/consistencyProbe";
        context.Request.ContentType = contentType;
        context.Request.Body = body;
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonElement> ReadEnvelopeAsync(DefaultHttpContext context)
    {
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using (var reader = new StreamReader(context.Response.Body, Encoding.UTF8))
        {
            var responseBody = await reader.ReadToEndAsync();
            return JsonDocument.Parse(responseBody).RootElement;
        }
    }

    private static byte[] BuildProtoBufRequestBody(MessageObject request)
    {
        var messageHttpObject = new MessageHttpObject
        {
            Id = MessageProtoHelper.GetMessageIdByType(request.GetType()),
            UniqueId = 7,
            Body = ProtoBufSerializerHelper.Serialize(request).ToArray(),
        };
        return ProtoBufSerializerHelper.Serialize(messageHttpObject).ToArray();
    }

    private static void EnsureProtoMessageRegistry()
    {
        // 惰性初始化消息注册表（对齐 SessionAuthenticationMiddlewareTests 模式），避免清空其他夹具的注册
        if (MessageProtoHelper.GetMessageTypeById(((10) << 16) + 10) == null)
        {
            MessageProtoHelper.Init(typeof(ReqHeartBeat).Assembly);
        }
    }

    private static void EnsureSettings()
    {
        if (GlobalSettings.CurrentSetting != null)
        {
            GameAppRuntime.MarkStarted(DateTime.UtcNow);
            return;
        }

        lock (SettingsLock)
        {
            if (GlobalSettings.CurrentSetting == null)
            {
                GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1, });
            }
        }

        GameAppRuntime.MarkStarted(DateTime.UtcNow);
    }

    /// <summary>
    /// ProtoBuf 回显处理器：原样返回请求消息，用于断言 ProtoBuf 成功路径的响应头。
    /// </summary>
    private sealed class ProtoBufEchoHttpHandler : BaseHttpHandler
    {
        private readonly Action _probe;

        public ProtoBufEchoHttpHandler(Action probe)
        {
            _probe = probe;
        }

        public override Task<MessageObject> ActionMessageObjectAsync(HttpActionContext context)
        {
            _probe();
            return Task.FromResult(context.MessageObject);
        }
    }

    /// <summary>
    /// Plain JSON 探针处理器。
    /// </summary>
    private sealed class PlainJsonProbeHttpHandler : BaseHttpHandler
    {
        private readonly Action _probe;

        public PlainJsonProbeHttpHandler(Action probe)
        {
            _probe = probe;
        }

        public override Task<string> ActionAsync(HttpActionContext context)
        {
            _probe();
            return Task.FromResult(HttpJsonResultData<string>.SuccessString());
        }
    }

    /// <summary>
    /// 带 [Required] 强类型请求的处理器：用于 DataAnnotations 校验失败业务码回归。
    /// </summary>
    [HttpMessageMapping(typeof(DataAnnotationsProbeHttpHandler), typeof(ValidationProbeRequest))]
    private sealed class DataAnnotationsProbeHttpHandler : BaseHttpHandler
    {
        private readonly Action _probe;

        public DataAnnotationsProbeHttpHandler(Action probe)
        {
            _probe = probe;
        }

        public override Task<string> ActionAsync(HttpActionContext context)
        {
            _probe();
            return Task.FromResult(HttpJsonResultData<string>.SuccessString());
        }
    }

    private sealed class ValidationProbeRequest : HttpMessageRequestBase
    {
        [Required]
        public string RequiredName { get; set; }

        public string OptionalName { get; set; }
    }

    private sealed class RejectingAopHandler : IHttpAopHandler
    {
        public int Priority
        {
            get { return 0; }
        }

        public bool Run(HttpContext httpContext, HttpActionContext context)
        {
            return false;
        }
    }

    private sealed class RespondingRejectAopHandler : IHttpAopHandler
    {
        public int Priority
        {
            get { return 0; }
        }

        public bool Run(HttpContext httpContext, HttpActionContext context)
        {
            httpContext.Response.WriteAsync("custom-response").GetAwaiter().GetResult();
            return false;
        }
    }

    /// <summary>
    /// 恒置位 HasStarted 的响应 feature 包装：DefaultHttpContext 不跟踪响应启动状态，
    /// 用于在内存测试中模拟真实服务器“响应已写出”的语义（见探测实验）。
    /// </summary>
    private sealed class StartedResponseFeature : IHttpResponseFeature
    {
        private readonly IHttpResponseFeature _inner;

        public StartedResponseFeature(IHttpResponseFeature inner)
        {
            _inner = inner;
        }

        public Stream Body
        {
            get { return _inner.Body; }
            set { _inner.Body = value; }
        }

        public void OnCompleted(Func<object, Task> callback, object state)
        {
            _inner.OnCompleted(callback, state);
        }

        public void OnStarting(Func<object, Task> callback, object state)
        {
            _inner.OnStarting(callback, state);
        }

        public int StatusCode
        {
            get { return _inner.StatusCode; }
            set { _inner.StatusCode = value; }
        }

        public string ReasonPhrase
        {
            get { return _inner.ReasonPhrase; }
            set { _inner.ReasonPhrase = value; }
        }

        public IHeaderDictionary Headers
        {
            get { return _inner.Headers; }
            set { _inner.Headers = value; }
        }

        public bool HasStarted
        {
            get { return true; }
        }
    }
}
