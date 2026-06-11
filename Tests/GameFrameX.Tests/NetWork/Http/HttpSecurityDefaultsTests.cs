// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
//  
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//  
//  本项目采用 Apache License 2.0 单协议分发，
//  This project is licensed solely under the Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
//  
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//  
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Reflection;
using System.Text;
using GameFrameX.NetWork.HTTP;
using GameFrameX.StartUp;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Runtime;
using GameFrameX.Utility.Setting;
using Microsoft.AspNetCore.Http;

namespace GameFrameX.Tests.NetWork.Http;

[Collection(GameAppRuntimeCollection.Name)]
public class HttpSecurityDefaultsTests
{
    private static readonly object SettingsLock = new();

    [Fact]
    public void BaseHttpHandler_ShouldNotRequireSignatureByDefault()
    {
        var handler = new SecureTestHttpHandler();

        Assert.False(handler.IsCheckSign);
    }

    [Fact]
    public void BaseHttpHandler_ShouldRequireSignature_WhenExplicitlyMarked()
    {
        var handler = new SignedTestHttpHandler();

        Assert.True(handler.IsCheckSign);
    }

    [Fact]
    public void BaseHttpHandler_ShouldRequireSignature_WhenGlobalSwitchIsEnabled()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var original = setting.HttpRequireSign;
        try
        {
            setting.HttpRequireSign = true;
            var handler = new SecureTestHttpHandler();

            Assert.True(handler.IsCheckSign);
        }
        finally
        {
            setting.HttpRequireSign = original;
        }
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnUnauthorized_WhenSignatureIsMissing()
    {
        EnsureSettings();
        var handlerCalled = false;
        var context = CreateJsonContext("{}");

        await HttpHandler.HandleRequest(context, _ =>
        {
            return new SignedTestHttpHandler(() => handlerCalled = true);
        });

        Assert.False(handlerCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Fact]
    public void ResolveHttpCorsAllowedOrigins_ShouldDisableCorsInProduction_WhenWhitelistIsEmpty()
    {
        var origins = ResolveCorsOrigins(string.Empty, development: false);

        Assert.Empty(origins);
    }

    [Fact]
    public void ResolveHttpCorsAllowedOrigins_ShouldUseWhitelist_WhenConfigured()
    {
        var origins = ResolveCorsOrigins("https://admin.example.com; https://ops.example.com,https://admin.example.com", development: false);

        Assert.Equal(new[] { "https://admin.example.com", "https://ops.example.com" }, origins);
    }

    [Fact]
    public void ResolveHttpCorsAllowedOrigins_ShouldAllowLocalhostInDevelopment_WhenWhitelistIsEmpty()
    {
        var origins = ResolveCorsOrigins(string.Empty, development: true);

        Assert.Contains("http://localhost", origins);
        Assert.Contains("https://localhost", origins);
        Assert.DoesNotContain("*", origins);
    }

    private static DefaultHttpContext CreateJsonContext(string jsonBody)
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

    private static string[] ResolveCorsOrigins(string allowedOrigins, bool development)
    {
        var method = typeof(AppStartUpBase).GetMethod("ResolveHttpCorsAllowedOrigins", BindingFlags.NonPublic | BindingFlags.Static);
        return (string[])method!.Invoke(null, new object[] { allowedOrigins, development })!;
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

    private class SecureTestHttpHandler : BaseHttpHandler
    {
        private readonly Action _onAction;

        public SecureTestHttpHandler(Action onAction = null)
        {
            _onAction = onAction;
        }

        public override Task<string> Action(string ip, string url, Dictionary<string, object> paramMap)
        {
            _onAction?.Invoke();
            return Task.FromResult("{}");
        }
    }

    [RequireHttpSignature]
    private sealed class SignedTestHttpHandler : SecureTestHttpHandler
    {
        public SignedTestHttpHandler(Action onAction = null)
            : base(onAction)
        {
        }
    }
}
