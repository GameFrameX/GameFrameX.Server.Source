// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and other related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and by applicable laws and international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the full license text of the LICENSE file in the source code root directory.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or otherwise infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development,
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.ComponentModel.DataAnnotations;
using System.Reflection;
using System.Text;
using System.Text.Json;
using GameFrameX.NetWork.HTTP;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Runtime;
using GameFrameX.Utility.Setting;
using Microsoft.AspNetCore.Http;

namespace GameFrameX.Tests.NetWork.Http;

/// <summary>
/// C202 合并特性行为测试：<see cref="HttpMessageMappingAttribute"/> 的 RequestType / ResponseType 命名属性。
/// </summary>
[Collection(GameAppRuntimeCollection.Name)]
public class HttpMessageMappingAttributeTests
{
    private const string HandlerResultJson = "{\"ok\":true}";
    private static readonly object SettingsLock = new();

    // (a) POST JSON body → 强类型请求绑定成功执行
    [Fact]
    public async Task HandleRequest_ShouldBindTypedRequestFromJsonBody_WhenRequestTypeIsSet()
    {
        EnsureSettings();
        HttpActionContext capturedContext = null;
        var context = CreateJsonPostContext("{\"Name\":\"alice\",\"Age\":18,\"RequiredField\":\"ok\"}");

        await HttpHandler.HandleRequest(context, _ => new TypedPostHttpHandler(ctx =>
        {
            capturedContext = ctx;
            return HandlerResultJson;
        }));

        Assert.Equal(HandlerResultJson, GetResponseBody(context));
        Assert.NotNull(capturedContext);
        var request = Assert.IsType<TypedPostRequest>(capturedContext.Request);
        Assert.Equal("alice", request.Name);
        Assert.Equal(18, request.Age);
    }

    // (a) [Required] 字段缺失 → 校验错误响应（非 code 0），handler 不执行
    [Fact]
    public async Task HandleRequest_ShouldReturnValidationError_WhenRequiredFieldIsMissing()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateJsonPostContext("{\"Name\":\"bob\"}");

        await HttpHandler.HandleRequest(context, _ => new TypedPostHttpHandler(_ =>
        {
            handlerCalled = true;
            return HandlerResultJson;
        }));

        Assert.False(handlerCalled);
        var code = GetResponseCode(context);
        Assert.NotEqual(0, code);
        Assert.Equal(400, code);
    }

    // (b) GET + RequestType → Query 参数绑定到强类型请求并执行
    [Fact]
    public async Task HandleRequest_ShouldBindTypedRequestFromQuery_WhenGetRequestWithRequestType()
    {
        EnsureSettings();
        HttpActionContext capturedContext = null;
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Get;
        context.Request.Path = "/game/api/test";
        context.Request.QueryString = new QueryString("?Name=carol&Age=20&RequiredField=ok");
        context.Response.Body = new MemoryStream();

        await HttpHandler.HandleRequest(context, _ => new TypedPostHttpHandler(ctx =>
        {
            capturedContext = ctx;
            return HandlerResultJson;
        }));

        Assert.Equal(HandlerResultJson, GetResponseBody(context));
        Assert.NotNull(capturedContext);
        var request = Assert.IsType<TypedPostRequest>(capturedContext.Request);
        Assert.Equal("carol", request.Name);
        Assert.Equal(20, request.Age);
    }

    // (c) RequestType 位置参数传非 HttpMessageRequestBase 子类 → GetCustomAttribute 抛 InvalidCastException
    [Fact]
    public void GetCustomAttribute_ShouldThrowInvalidCastException_WhenRequestTypeIsNotMessageRequestSubclass()
    {
        Assert.Throws<InvalidCastException>(() =>
            typeof(BadRequestTypeHttpHandler).GetCustomAttribute<HttpMessageMappingAttribute>());
    }

    // (c) ResponseType 位置参数传非 HttpMessageResponseBase 子类 → GetCustomAttribute 抛 InvalidCastException
    [Fact]
    public void GetCustomAttribute_ShouldThrowInvalidCastException_WhenResponseTypeIsNotMessageResponseSubclass()
    {
        Assert.Throws<InvalidCastException>(() =>
            typeof(BadResponseTypeHttpHandler).GetCustomAttribute<HttpMessageMappingAttribute>());
    }

    // 合并形态的命名属性在特性上可正确读取
    [Fact]
    public void Attribute_ShouldExposeRequestTypeAndResponseType()
    {
        var attribute = typeof(TypedPostHttpHandler).GetCustomAttribute<HttpMessageMappingAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(typeof(TypedPostRequest), attribute.RequestType);
        Assert.Equal(typeof(TypedPostResponse), attribute.ResponseType);
    }

    // (d) 未设 RequestType → PlainJson 路径行为不变
    [Fact]
    public async Task HandleRequest_ShouldExecutePlainJsonPath_WhenRequestTypeIsNotSet()
    {
        EnsureSettings();
        HttpActionContext capturedContext = null;
        var context = CreateJsonPostContext("{\"Name\":\"dave\"}");

        await HttpHandler.HandleRequest(context, _ => new PlainJsonHttpHandler(ctx =>
        {
            capturedContext = ctx;
            return HandlerResultJson;
        }));

        Assert.Equal(HandlerResultJson, GetResponseBody(context));
        Assert.NotNull(capturedContext);
        Assert.Null(capturedContext.Request);
        Assert.Equal("dave", capturedContext.Parameters["Name"].ToString());
    }

    private static DefaultHttpContext CreateJsonPostContext(string jsonBody)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(jsonBody);
        var context = new DefaultHttpContext();
        context.Request.Method = HttpMethods.Post;
        context.Request.Path = "/game/api/test";
        context.Request.ContentType = "application/json";
        context.Request.ContentLength = bodyBytes.Length;
        context.Request.Body = new MemoryStream(bodyBytes);
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static string GetResponseBody(DefaultHttpContext context)
    {
        return Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
    }

    private static int GetResponseCode(DefaultHttpContext context)
    {
        using var document = JsonDocument.Parse(GetResponseBody(context));
        foreach (var property in document.RootElement.EnumerateObject())
        {
            if (string.Equals(property.Name, "code", StringComparison.OrdinalIgnoreCase)
                && property.Value.ValueKind == JsonValueKind.Number)
            {
                return property.Value.GetInt32();
            }
        }

        return -1;
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
                GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 1 });
            }
        }

        GameAppRuntime.MarkStarted(DateTime.UtcNow);
    }

    public sealed class TypedPostRequest : HttpMessageRequestBase
    {
        public string Name { get; set; }

        [Required]
        public string RequiredField { get; set; }

        public int Age { get; set; }
    }

    public sealed class TypedPostResponse : HttpMessageResponseBase
    {
        public string Name { get; set; }
    }

    [HttpMessageMapping(typeof(TypedPostHttpHandler), typeof(TypedPostRequest), typeof(TypedPostResponse))]
    private sealed class TypedPostHttpHandler : BaseHttpHandler
    {
        private readonly Func<HttpActionContext, string> _action;

        public TypedPostHttpHandler(Func<HttpActionContext, string> action)
        {
            _action = action;
        }

        public override Task<string> Action(HttpActionContext context)
        {
            return Task.FromResult(_action(context));
        }
    }

    [HttpMessageMapping(typeof(PlainJsonHttpHandler))]
    private sealed class PlainJsonHttpHandler : BaseHttpHandler
    {
        private readonly Func<HttpActionContext, string> _action;

        public PlainJsonHttpHandler(Func<HttpActionContext, string> action)
        {
            _action = action;
        }

        public override Task<string> Action(HttpActionContext context)
        {
            return Task.FromResult(_action(context));
        }
    }

    private sealed class NotAMessageRequest
    {
    }

    private sealed class NotAMessageResponse
    {
    }

    [HttpMessageMapping(typeof(BadRequestTypeHttpHandler), typeof(NotAMessageRequest))]
    private sealed class BadRequestTypeHttpHandler : BaseHttpHandler
    {
        public override Task<string> Action(HttpActionContext context)
        {
            return Task.FromResult("{}");
        }
    }

    [HttpMessageMapping(typeof(BadResponseTypeHttpHandler), null, typeof(NotAMessageResponse))]
    private sealed class BadResponseTypeHttpHandler : BaseHttpHandler
    {
        public override Task<string> Action(HttpActionContext context)
        {
            return Task.FromResult("{}");
        }
    }
}
