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


using System.Collections.Concurrent;
using System.ComponentModel;
using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace GameFrameX.NetWork.HTTP;

/// <summary>
/// 自定义 Swagger 操作过滤器，用于处理动态路由和请求/响应文档。
/// </summary>
/// <remarks>
/// Custom Swagger operation filter for handling dynamic routing and request/response documentation.
/// Automatically generates OpenAPI documentation based on HTTP handler attributes.
/// </remarks>
public sealed class SwaggerOperationFilter : IOperationFilter
{
    /// <summary>
    /// HTTP 处理器列表。
    /// </summary>
    /// <remarks>
    /// List of HTTP handlers for generating documentation.
    /// </remarks>
    private readonly List<BaseHttpHandler> _handlers;

    /// <summary>
    /// 处理器类型到 <see cref="HttpMessageMappingAttribute"/> 的缓存。
    /// </summary>
    /// <remarks>
    /// Cache for mapping handler types to <see cref="HttpMessageMappingAttribute"/>.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, HttpMessageMappingAttribute> MappingAttributeCache = new();

    /// <summary>
    /// 处理器类型到 <see cref="HttpMessageRequestAttribute"/> 的缓存。
    /// </summary>
    /// <remarks>
    /// Cache for mapping handler types to <see cref="HttpMessageRequestAttribute"/>.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, HttpMessageRequestAttribute> RequestAttributeCache = new();

    /// <summary>
    /// 处理器类型到 <see cref="HttpMessageResponseAttribute"/> 的缓存。
    /// </summary>
    /// <remarks>
    /// Cache for mapping handler types to <see cref="HttpMessageResponseAttribute"/>.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, HttpMessageResponseAttribute> ResponseAttributeCache = new();

    /// <summary>
    /// 处理器类型到 <see cref="DescriptionAttribute"/> 的缓存。
    /// </summary>
    /// <remarks>
    /// Cache for mapping handler types to <see cref="DescriptionAttribute"/>.
    /// </remarks>
    private static readonly ConcurrentDictionary<Type, DescriptionAttribute> DescriptionAttributeCache = new();

    /// <summary>
    /// 初始化 <see cref="SwaggerOperationFilter"/> 的新实例。
    /// </summary>
    /// <remarks>
    /// Initializes a new instance of <see cref="SwaggerOperationFilter"/>.
    /// </remarks>
    /// <param name="handlers">HTTP 处理器列表 / HTTP handler list</param>
    public SwaggerOperationFilter(List<BaseHttpHandler> handlers)
    {
        _handlers = handlers;
    }

    /// <summary>
    /// 应用过滤器配置，生成 OpenAPI 操作文档。
    /// </summary>
    /// <remarks>
    /// Applies the filter configuration to generate OpenAPI operation documentation.
    /// </remarks>
    /// <param name="operation">OpenAPI 操作对象 / OpenAPI operation object</param>
    /// <param name="context">操作过滤器上下文 / Operation filter context</param>
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var routeTemplate = context.ApiDescription.RelativePath;
        if (string.IsNullOrEmpty(routeTemplate))
        {
            return;
        }

        operation.Parameters.Clear();

        // 找到匹配的处理器（使用缓存的特性）
        var handler = FindHandler(routeTemplate);
        if (handler == null)
        {
            return;
        }

        var handlerType = handler.GetType();

        // 获取请求和响应的消息类型（使用缓存）
        var mappingAttr = MappingAttributeCache.GetOrAdd(handlerType, t => t.GetCustomAttribute<HttpMessageMappingAttribute>());
        var requestAttr = RequestAttributeCache.GetOrAdd(handlerType, t => t.GetCustomAttribute<HttpMessageRequestAttribute>());
        var responseAttr = ResponseAttributeCache.GetOrAdd(handlerType, t => t.GetCustomAttribute<HttpMessageResponseAttribute>());

        // 判断是否为 GET 请求
        var isGetRequest = mappingAttr?.HttpMethod == HttpMethodType.GET;

        // 设置请求参数或请求体
        ApplyRequestBody(operation, context, isGetRequest, requestAttr);

        // 设置成功响应体
        ApplySuccessResponse(operation, context, responseAttr);

        // 添加操作描述（使用缓存）
        var descriptionAttr = DescriptionAttributeCache.GetOrAdd(handlerType, t => t.GetCustomAttribute<DescriptionAttribute>());
        operation.Summary = descriptionAttr?.Description ?? handlerType.Name;
        operation.Description = GetTypeDescription(handlerType);
    }

    /// <summary>
    /// 根据路由模板查找匹配的 HTTP 处理器。
    /// </summary>
    /// <remarks>
    /// Finds the HTTP handler whose cached <see cref="HttpMessageMappingAttribute"/> standard command matches the end of the route template.
    /// </remarks>
    /// <param name="routeTemplate">路由模板 / Route template</param>
    /// <returns>匹配的处理器，未找到时返回 null / The matching handler, or null when none matches</returns>
    private BaseHttpHandler FindHandler(string routeTemplate)
    {
        return _handlers.FirstOrDefault(h =>
        {
            var handlerType = h.GetType();
            var mappingAttr = MappingAttributeCache.GetOrAdd(handlerType, t => t.GetCustomAttribute<HttpMessageMappingAttribute>());
            return routeTemplate.EndsWith(mappingAttr?.StandardCmd ?? "", StringComparison.OrdinalIgnoreCase);
        });
    }

    /// <summary>
    /// 根据请求特性设置 OpenAPI 的请求参数或请求体。
    /// </summary>
    /// <remarks>
    /// Applies the OpenAPI request parameters (for GET) or request body (for other HTTP methods) based on the request message attribute. GET requests without a message type, and non-GET requests without a message type, are left untouched except for the generic object body fallback.
    /// </remarks>
    /// <param name="operation">OpenAPI 操作对象 / OpenAPI operation object</param>
    /// <param name="context">操作过滤器上下文 / Operation filter context</param>
    /// <param name="isGetRequest">是否为 GET 请求 / Whether the request is a GET</param>
    /// <param name="requestAttr">请求消息特性 / Request message attribute</param>
    private void ApplyRequestBody(OpenApiOperation operation, OperationFilterContext context, bool isGetRequest, HttpMessageRequestAttribute requestAttr)
    {
        // GET 请求且有消息类型：生成 Query 参数
        if (isGetRequest && requestAttr?.MessageType != null)
        {
            AddQueryParameters(operation, requestAttr.MessageType);
            return;
        }

        // 非 GET 请求且有消息类型：生成带修正属性名的 RequestBody
        if (requestAttr?.MessageType != null)
        {
            var requestSchema = context.SchemaGenerator.GenerateSchema(requestAttr.MessageType, context.SchemaRepository);
            CorrectSchemaPropertyNames(requestSchema, requestAttr.MessageType);
            operation.RequestBody = CreateJsonRequestBody(requestSchema);
            return;
        }

        // 非 GET 请求且无消息类型：使用通用对象 RequestBody
        if (!isGetRequest)
        {
            operation.RequestBody = CreateJsonRequestBody(new OpenApiSchema { Type = JsonSchemaType.Object, });
        }
    }

    /// <summary>
    /// 为 GET 请求的消息类型属性生成 Query 参数并加入操作。
    /// </summary>
    /// <remarks>
    /// Generates OpenAPI query parameters from the message type's properties and adds them to the operation (GET requests).
    /// </remarks>
    /// <param name="operation">OpenAPI 操作对象 / OpenAPI operation object</param>
    /// <param name="messageType">请求消息类型 / Request message type</param>
    private static void AddQueryParameters(OpenApiOperation operation, Type messageType)
    {
        foreach (var property in messageType.GetProperties())
        {
            var propDescriptionAttr = property.GetCustomAttribute<DescriptionAttribute>();

            operation.Parameters.Add(new OpenApiParameter
            {
                Name = property.Name,
                In = ParameterLocation.Query,
                Required = false, // 可根据 RequiredAttribute 判断
                Description = propDescriptionAttr?.Description ?? property.Name,
                Schema = GetSchemaForType(property.PropertyType),
            });
        }
    }

    /// <summary>
    /// 修正请求 Schema 中属性键的大小写，使其与消息类型的属性名一致。
    /// </summary>
    /// <remarks>
    /// Corrects the casing of request schema property keys so they match the message type's property names. The generated schema keys are lowercased; this maps them back to the original property names.
    /// </remarks>
    /// <param name="requestSchema">由 Schema 生成器生成的请求 Schema / The request schema produced by the schema generator</param>
    /// <param name="messageType">请求消息类型 / Request message type</param>
    private static void CorrectSchemaPropertyNames(IOpenApiSchema requestSchema, Type messageType)
    {
        if (requestSchema.Properties == null)
        {
            return;
        }

        var correctedProperties = new Dictionary<string, IOpenApiSchema>();

        foreach (var property in messageType.GetProperties())
        {
            var lowercaseKey = property.Name.ToLowerInvariant();
            if (requestSchema.Properties.TryGetValue(lowercaseKey, out var schemaProperty))
            {
                correctedProperties[property.Name] = schemaProperty;
            }
        }

        requestSchema.Properties.Clear();
        foreach (var prop in correctedProperties)
        {
            requestSchema.Properties[prop.Key] = prop.Value;
        }
    }

    /// <summary>
    /// 创建一个 application/json 的请求体定义。
    /// </summary>
    /// <remarks>
    /// Creates an OpenAPI request body definition carrying the provided schema as application/json content.
    /// </remarks>
    /// <param name="schema">请求 Schema / Request schema</param>
    /// <returns>构造完成的 <see cref="OpenApiRequestBody"/> / The constructed <see cref="OpenApiRequestBody"/></returns>
    private static OpenApiRequestBody CreateJsonRequestBody(IOpenApiSchema schema)
    {
        return new OpenApiRequestBody
        {
            Required = true,
            Description = "请求参数",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType
                {
                    Schema = schema,
                },
            },
        };
    }

    /// <summary>
    /// 构造并设置操作的成功响应（HTTP 200）。
    /// </summary>
    /// <remarks>
    /// Builds the success response schema (code/message/data) and assigns it to the operation's responses. The data field uses the response message type when available, otherwise a generic object.
    /// </remarks>
    /// <param name="operation">OpenAPI 操作对象 / OpenAPI operation object</param>
    /// <param name="context">操作过滤器上下文 / Operation filter context</param>
    /// <param name="responseAttr">响应消息特性 / Response message attribute</param>
    private static void ApplySuccessResponse(OpenApiOperation operation, OperationFilterContext context, HttpMessageResponseAttribute responseAttr)
    {
        var successResponseSchema = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["code"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.Integer,
                    Description = "响应状态码",
                    Example = JsonValue.Create(0),
                },
                ["message"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Description = "响应消息",
                    Example = JsonValue.Create("success"),
                },
            },
        };

        // 如果有响应类型，添加到 data 字段
        if (responseAttr?.MessageType != null)
        {
            successResponseSchema.Properties["data"] = context.SchemaGenerator.GenerateSchema(responseAttr.MessageType, context.SchemaRepository);
        }
        else
        {
            successResponseSchema.Properties["data"] = new OpenApiSchema { Type = JsonSchemaType.Object, };
        }

        operation.Responses = new OpenApiResponses
        {
            ["200"] = new OpenApiResponse
            {
                Description = "成功响应",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = successResponseSchema,
                    },
                },
            },
        };
    }

    /// <summary>
    /// 获取类型的描述信息。
    /// </summary>
    /// <remarks>
    /// Gets the description information of the type.
    /// </remarks>
    /// <param name="type">要获取描述的类型 / Type to get description for</param>
    /// <returns>类型的描述信息，如果没有描述特性则返回类型名称 / Type description, returns type name if no description attribute is present</returns>
    private string GetTypeDescription(Type type)
    {
        var summaryAttr = DescriptionAttributeCache.GetOrAdd(type, t => t.GetCustomAttribute<DescriptionAttribute>());
        return summaryAttr?.Description ?? type.Name;
    }

    /// <summary>
    /// CLR 类型到对应 OpenAPI Schema 工厂的映射表，用于避免冗长的类型判断分支。
    /// </summary>
    /// <remarks>
    /// Mapping from CLR types to OpenAPI schema factories, used by <see cref="GetSchemaForType"/> to avoid a long chain of type comparisons. Each factory reproduces the original schema initializer.
    /// </remarks>
    private static readonly Dictionary<Type, Func<IOpenApiSchema>> SchemaFactoryMap = new Dictionary<Type, Func<IOpenApiSchema>>
    {
        { typeof(string), () => new OpenApiSchema { Type = JsonSchemaType.String, } },
        { typeof(int), () => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", } },
        { typeof(int?), () => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int32", } },
        { typeof(long), () => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64", } },
        { typeof(long?), () => new OpenApiSchema { Type = JsonSchemaType.Integer, Format = "int64", } },
        { typeof(float), () => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "float", } },
        { typeof(float?), () => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "float", } },
        { typeof(double), () => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "double", } },
        { typeof(double?), () => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "double", } },
        { typeof(decimal), () => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "decimal", } },
        { typeof(decimal?), () => new OpenApiSchema { Type = JsonSchemaType.Number, Format = "decimal", } },
        { typeof(bool), () => new OpenApiSchema { Type = JsonSchemaType.Boolean, } },
        { typeof(bool?), () => new OpenApiSchema { Type = JsonSchemaType.Boolean, } },
        { typeof(DateTime), () => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time", } },
        { typeof(DateTime?), () => new OpenApiSchema { Type = JsonSchemaType.String, Format = "date-time", } },
        { typeof(Guid), () => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", } },
        { typeof(Guid?), () => new OpenApiSchema { Type = JsonSchemaType.String, Format = "uuid", } },
        { typeof(byte[]), () => new OpenApiSchema { Type = JsonSchemaType.String, Format = "byte", } },
    };

    /// <summary>
    /// 根据类型获取对应的 OpenAPI Schema。
    /// </summary>
    /// <remarks>
    /// Gets the corresponding OpenAPI Schema based on the type.
    /// </remarks>
    /// <param name="type">属性类型 / Property type</param>
    /// <returns>对应的 OpenAPI Schema / Corresponding OpenAPI Schema</returns>
    private static IOpenApiSchema GetSchemaForType(Type type)
    {
        if (SchemaFactoryMap.TryGetValue(type, out var factory))
        {
            return factory();
        }

        if (type.IsArray || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)))
        {
            return new OpenApiSchema { Type = JsonSchemaType.Array, };
        }

        return new OpenApiSchema { Type = JsonSchemaType.Object, };
    }
}
