// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目以 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt public order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer, project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/

using System.Globalization;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Localization;
using GameFrameX.Utility.Math;

namespace GameFrameX.Tests.Localization;

/// <summary>
/// 双语资源占位符一致性回归测试（GFX-1156）。
/// </summary>
/// <remarks>
/// 验证中性（英文）与 zh-CN 两种文化下，修正后的模板占位符行为：
/// Swagger 路由前缀为固定值、Prometheus 内联端点日志携带实际 ip:port、
/// FP 值域异常消息不含字面占位符。
/// </remarks>
public class ResourcePlaceholderConsistencyTest : IDisposable
{
    private readonly CultureInfo _originalUICulture;

    public ResourcePlaceholderConsistencyTest()
    {
        _originalUICulture = CultureInfo.CurrentUICulture;
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalUICulture;
    }

    [Fact]
    public void SwaggerRoutePrefixIsFixedValueUnderInvariantCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        Assert.Equal("swagger", LocalizationService.GetString(Keys.StartUp.HttpServer.SwaggerRoutePrefix));
    }

    [Fact]
    public void SwaggerRoutePrefixIsFixedValueUnderChineseCulture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

        Assert.Equal("swagger", LocalizationService.GetString(Keys.StartUp.HttpServer.SwaggerRoutePrefix));
    }

    [Fact]
    public void ApiTitleIsFixedValueUnderInvariantCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        Assert.Equal("GameFrameX API", LocalizationService.GetString(Keys.StartUp.HttpServer.ApiTitle));
    }

    [Fact]
    public void PrometheusInlineEndpointLogContainsHostAndPortUnderInvariantCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        var message = LocalizationService.GetString(Keys.StartUp.PrometheusMetricsEndpointEnabledInline, "10.0.0.1", 8080);

        Assert.Equal("Prometheus metrics endpoint enabled inline: http://10.0.0.1:8080/metrics.", message);
        Assert.DoesNotContain("{", message);
    }

    [Fact]
    public void PrometheusInlineEndpointLogContainsHostAndPortUnderChineseCulture()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("zh-CN");

        var message = LocalizationService.GetString(Keys.StartUp.PrometheusMetricsEndpointEnabledInline, "10.0.0.1", 8080);

        Assert.Equal("内联 Prometheus 指标端点已启用: http://10.0.0.1:8080/metrics。", message);
        Assert.DoesNotContain("{", message);
    }

    [Fact]
    public void ValueOutOfRangeMessageContainsNoLiteralPlaceholderUnderInvariantCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        var message = LocalizationService.GetString(Keys.Utility.ExceptionMessages.ValueOutOfRange);

        Assert.Equal("Value must be between -FP.One and FP.One.", message);
        Assert.DoesNotContain("{", message);
    }

    [Fact]
    public void FpAcosDomainViolationMessageContainsNoLiteralPlaceholderUnderInvariantCulture()
    {
        CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => FP.Acos(2));

        Assert.DoesNotContain("{", exception.Message);
    }
}
