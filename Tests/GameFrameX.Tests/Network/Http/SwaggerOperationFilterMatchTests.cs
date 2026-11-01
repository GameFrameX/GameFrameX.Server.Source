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
using GameFrameX.Foundation.Http.Normalization;
using GameFrameX.Network.HTTP;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameFrameX.Tests.Network.Http;

/// <summary>
/// Swagger 操作过滤器路由匹配回归（GFX-1180 / C458 CR-L03）：
/// 互为后缀的命令（login / req_login）按路由最后一段精确匹配不得错配文档；
/// 驼峰（OriginalCmd）路由同样可匹配；GET Query 参数必填性跟随 RequiredAttribute。
/// </summary>
public class SwaggerOperationFilterMatchTests
{
    [Fact]
    public void Apply_ShouldMatchExactCommand_WhenCommandsAreMutualSuffixes()
    {
        // LoginHttpHandler 排在列表首位：旧 EndsWith 实现会先命中它（"game/api/req_login".EndsWith("login")），
        // 新实现必须按最后一段精确匹配到 ReqLoginHttpHandler
        var filter = CreateFilter(new LoginHttpHandler(), new ReqLoginHttpHandler());

        var (operation, repository) = Apply(filter, "game/api/req_login", "POST");

        Assert.NotNull(operation.RequestBody);
        Assert.Contains("ReqLoginSwaggerRequest", repository.Schemas.Keys);
        Assert.DoesNotContain("LoginSwaggerRequest", repository.Schemas.Keys);
    }

    [Fact]
    public void Apply_ShouldMatchOriginalCmd_WhenRouteUsesCamelCaseCommand()
    {
        var filter = CreateFilter(new ReqPlayerListHttpHandler());

        var (operation, repository) = Apply(filter, "game/api/ReqPlayerList", "POST");

        Assert.NotNull(operation.RequestBody);
        Assert.Contains("ReqPlayerListSwaggerRequest", repository.Schemas.Keys);
    }

    [Fact]
    public void Apply_ShouldNotMatchHandler_WhenRouteCommandIsNotExact()
    {
        var filter = CreateFilter(new LoginHttpHandler());

        var (operation, repository) = Apply(filter, "game/api/req_login", "POST");

        Assert.DoesNotContain("LoginSwaggerRequest", repository.Schemas.Keys);
    }

    [Fact]
    public void Apply_ShouldMarkQueryParameterRequired_FromRequiredAttribute()
    {
        var filter = CreateFilter(new PlayerListQueryHttpHandler());

        var (operation, repository) = Apply(filter, "game/api/PlayerListQuery", "GET");

        var requiredParameter = Assert.Single(operation.Parameters, parameter => parameter.Name == "RequiredPlayerId");
        var optionalParameter = Assert.Single(operation.Parameters, parameter => parameter.Name == "OptionalKeyword");
        Assert.True(requiredParameter.Required);
        Assert.False(optionalParameter.Required);
    }

    private static SwaggerOperationFilter CreateFilter(params BaseHttpHandler[] handlers)
    {
        return new SwaggerOperationFilter(new List<BaseHttpHandler>(handlers));
    }

    private static (OpenApiOperation Operation, SchemaRepository Repository) Apply(SwaggerOperationFilter filter, string relativePath, string httpMethod)
    {
        var schemaRepository = new SchemaRepository();
        var schemaGenerator = new SchemaGenerator(new SchemaGeneratorOptions(), new JsonSerializerDataContractResolver(new System.Text.Json.JsonSerializerOptions()));
        var apiDescription = new ApiDescription { RelativePath = relativePath, HttpMethod = httpMethod, };
        var context = new OperationFilterContext(apiDescription, schemaGenerator, schemaRepository, null, null);
        var operation = new OpenApiOperation { Parameters = new List<IOpenApiParameter>(), };
        filter.Apply(operation, context);
        return (operation, schemaRepository);
    }

    private abstract class ProbeHttpHandlerBase : BaseHttpHandler
    {
        public override Task<string> ActionAsync(HttpActionContext context)
        {
            return Task.FromResult(HttpJsonResultData<string>.SuccessString());
        }
    }

    [HttpMessageMapping(typeof(LoginHttpHandler), typeof(LoginSwaggerRequest))]
    private sealed class LoginHttpHandler : ProbeHttpHandlerBase
    {
    }

    [HttpMessageMapping(typeof(ReqLoginHttpHandler), typeof(ReqLoginSwaggerRequest))]
    private sealed class ReqLoginHttpHandler : ProbeHttpHandlerBase
    {
    }

    [HttpMessageMapping(typeof(ReqPlayerListHttpHandler), typeof(ReqPlayerListSwaggerRequest))]
    private sealed class ReqPlayerListHttpHandler : ProbeHttpHandlerBase
    {
    }

    [HttpMessageMapping(typeof(PlayerListQueryHttpHandler), typeof(PlayerListQueryRequest), HttpMethod = HttpMethodType.GET)]
    private sealed class PlayerListQueryHttpHandler : ProbeHttpHandlerBase
    {
    }

    private sealed class LoginSwaggerRequest : HttpMessageRequestBase
    {
        public string LoginName { get; set; }
    }

    private sealed class ReqLoginSwaggerRequest : HttpMessageRequestBase
    {
        public string CredentialCode { get; set; }
    }

    private sealed class ReqPlayerListSwaggerRequest : HttpMessageRequestBase
    {
        public string PlayerListKeyword { get; set; }
    }

    private sealed class PlayerListQueryRequest : HttpMessageRequestBase
    {
        [Required]
        public int RequiredPlayerId { get; set; }

        public string OptionalKeyword { get; set; }
    }
}
