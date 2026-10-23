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

using System.Reflection;
using System.Text;
using GameFrameX.Foundation.Hash;
using GameFrameX.Network.HTTP;
using GameFrameX.StartUp;
using GameFrameX.Tests.Utility;
using GameFrameX.Utility.Runtime;
using GameFrameX.Utility.Setting;
using Microsoft.AspNetCore.Http;

namespace GameFrameX.Tests.Network.Http;

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

        await HttpHandler.HandleRequestAsync(context, _ =>
        {
            return new SignedTestHttpHandler(() => handlerCalled = true);
        });

        Assert.False(handlerCalled);
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }
    [Fact]
    public async Task HandleRequest_ShouldExecuteHandler_WhenHmacSignatureIsValid()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = true;
            setting.HttpSignSecret = "unit-test-secret";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var sign = HmacSha256Helper.Hash(timestamp, setting.HttpSignSecret);
            var handlerCalled = false;
            var context = CreateJsonContext("{}");
            context.Request.QueryString = new QueryString($"?sign={Uri.EscapeDataString(sign)}&timestamp={timestamp}");

            await HttpHandler.HandleRequestAsync(context, _ => new SignedTestHttpHandler(() => handlerCalled = true));

            Assert.True(handlerCalled);
            Assert.Equal(StatusCodes.Status200OK, context.Response.StatusCode);
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnUnauthorized_WhenSignatureUsesForgedLegacyScheme()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = true;
            setting.HttpSignSecret = "unit-test-secret";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            // 旧方案（已废除）：sign = c1 + md5("38848" + ts) + c2，c1/c2 自公开常量 88/66 累加——零秘密，任何读者可伪造。
            // 伪造者按旧算法构造签名，新实现必须拒绝。
            var sign = ForgeLegacySignature(timestamp);
            var handlerCalled = false;
            var context = CreateJsonContext("{}");
            context.Request.QueryString = new QueryString($"?sign={Uri.EscapeDataString(sign)}&timestamp={timestamp}");

            await HttpHandler.HandleRequestAsync(context, _ => new SignedTestHttpHandler(() => handlerCalled = true));

            Assert.False(handlerCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnUnauthorized_WhenSignatureMismatches()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = true;
            setting.HttpSignSecret = "unit-test-secret";
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            var handlerCalled = false;
            var context = CreateJsonContext("{}");
            context.Request.QueryString = new QueryString($"?sign=not-a-valid-signature&timestamp={timestamp}");

            await HttpHandler.HandleRequestAsync(context, _ => new SignedTestHttpHandler(() => handlerCalled = true));

            Assert.False(handlerCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    [Fact]
    public async Task HandleRequest_ShouldReturnUnauthorized_WhenTimestampIsExpired()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = true;
            setting.HttpSignSecret = "unit-test-secret";
            var timestamp = DateTimeOffset.UtcNow.AddMinutes(-10).ToUnixTimeSeconds().ToString();
            var sign = HmacSha256Helper.Hash(timestamp, setting.HttpSignSecret);
            var handlerCalled = false;
            var context = CreateJsonContext("{}");
            context.Request.QueryString = new QueryString($"?sign={Uri.EscapeDataString(sign)}&timestamp={timestamp}");

            await HttpHandler.HandleRequestAsync(context, _ => new SignedTestHttpHandler(() => handlerCalled = true));

            Assert.False(handlerCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    [Fact]
    public async Task HandleRequest_ShouldFailClosed_WhenSecretIsNotConfigured()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = false;
            setting.HttpSignSecret = string.Empty;
            var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
            // 即便按空密钥算出“正确”HMAC，密钥缺失时也一律拒绝（fail-closed）
            var sign = HmacSha256Helper.Hash(timestamp, string.Empty);
            var handlerCalled = false;
            var context = CreateJsonContext("{}");
            context.Request.QueryString = new QueryString($"?sign={Uri.EscapeDataString(sign)}&timestamp={timestamp}");

            await HttpHandler.HandleRequestAsync(context, _ => new SignedTestHttpHandler(() => handlerCalled = true));

            Assert.False(handlerCalled);
            Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    [Fact]
    public void CheckSign_ShouldFailClosed_WhenGlobalSignEnabledWithoutSecret()
    {
        EnsureSettings();
        var setting = GlobalSettings.CurrentSetting;
        var originalRequireSign = setting.HttpRequireSign;
        var originalSecret = setting.HttpSignSecret;
        try
        {
            setting.HttpRequireSign = true;
            setting.HttpSignSecret = "  ";
            var handler = new SecureTestHttpHandler();
            var paramMap = new Dictionary<string, object>
            {
                { GlobalConst.HttpSignKey, "anything" },
                { GlobalConst.HttpTimestampKey, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
            };

            var isValid = handler.CheckSign(paramMap, out var error);

            Assert.False(isValid);
            Assert.False(string.IsNullOrEmpty(error));
        }
        finally
        {
            setting.HttpRequireSign = originalRequireSign;
            setting.HttpSignSecret = originalSecret;
        }
    }

    [Fact]
    public async Task HandleRequest_ShouldNotLeakExceptionDetailsToClient()
    {
        EnsureSettings();
        const string secretMarker = "SECRET-INTERNAL-DETAIL-7f3a";
        var handler = new SecureTestHttpHandler(() => throw new InvalidOperationException(secretMarker));
        var context = CreateJsonContext("{}");

        await HttpHandler.HandleRequestAsync(context, _ => handler);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using (var reader = new StreamReader(context.Response.Body, Encoding.UTF8))
        {
            var responseBody = await reader.ReadToEndAsync();
            Assert.DoesNotContain(secretMarker, responseBody);
            Assert.DoesNotContain("InvalidOperationException", responseBody);
            Assert.DoesNotContain("StackTrace", responseBody);
        }
    }

    /// <summary>
    /// 按已废除的旧算法（旧 md5+校验码方案）伪造签名，用于回归断言：新实现必须拒绝旧方案伪造。
    /// </summary>
    private static string ForgeLegacySignature(string timestamp)
    {
        var md5 = Md5Helper.Hash("38848" + timestamp);
        var checkCode1 = 88;
        var checkCode2 = 66;
        foreach (var t in md5)
        {
            if (t >= 'a')
            {
                checkCode1 += t;
            }
            else
            {
                checkCode2 += t;
            }
        }

        return checkCode1 + md5 + checkCode2;
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

        public override Task<string> ActionAsync(HttpActionContext context)
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
