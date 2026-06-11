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


using GameFrameX.Foundation.Json;
using GameFrameX.Foundation.Options.Attributes;

namespace GameFrameX.Utility.Setting;

/// <summary>
/// 应用程序配置类
/// </summary>
/// <remarks>
/// Application configuration class containing server settings, network options, and other configurable parameters.
/// </remarks>
public class AppSetting
{
    private string _serverType;

    /// <summary>
    /// 构造函数
    /// </summary>
    /// <remarks>
    /// Constructor that initializes default values.
    /// </remarks>
    public AppSetting()
    {
#if DEBUG
        IsDebug = true;
        IsDebugReceive = true;
        IsDebugSend = true;
        IsDebugSendHeartBeat = true;
        IsDebugReceiveHeartBeat = true;
#endif
    }

    /// <summary>
    /// 获取或设置服务器类型
    /// </summary>
    /// <remarks>
    /// Gets or sets the server type.
    /// </remarks>
    [Option(nameof(ServerType), Description = "服务器类型。单进程传单值(如 Game)，多进程传逗号分隔值(如 Game,Social,Chat)")]
    [GrafanaLokiLabelTag]
    public string ServerType
    {
        get { return _serverType; }
        init
        {
            _serverType = value;
            ServerName = value;
        }
    }

    /// <summary>
    /// 判断指定的服务ID是否为本地服务
    /// </summary>
    /// <remarks>
    /// Determines whether the specified server ID is a local service.
    /// </remarks>
    /// <param name="serverId">服务ID / Server ID</param>
    /// <returns>返回是否是本地服务 / Returns true if it is a local service</returns>
    public bool IsLocal(int serverId)
    {
        return serverId == ServerId;
    }

    /// <summary>
    /// 将对象序列化为JSON字符串
    /// </summary>
    /// <remarks>
    /// Serializes the object to a JSON string.
    /// </remarks>
    /// <returns>JSON字符串 / JSON string</returns>
    public override string ToString()
    {
        return JsonHelper.Serialize(this);
    }

    /// <summary>
    /// 将对象序列化为格式化的JSON字符串
    /// </summary>
    /// <remarks>
    /// Serializes the object to a formatted JSON string.
    /// </remarks>
    /// <returns>格式化的JSON字符串 / Formatted JSON string</returns>
    public string ToFormatString()
    {
        return JsonHelper.SerializeFormat(this);
    }

    #region 从配置文件读取的属性

    /// <summary>
    /// 是否启用指标收集功能,需要IsOpenTelemetry为true时有效
    /// <para>用于收集和监控应用程序的性能指标数据</para>
    /// <para>默认值为false</para>
    /// </summary>
    /// <remarks>
    /// Whether to enable metrics collection, effective when IsOpenTelemetry is true.
    /// Used for collecting and monitoring application performance metrics data.
    /// Default value is false.
    /// </remarks>
    [Option(nameof(IsOpenTelemetryMetrics), DefaultValue = false, Description = "是否启用指标收集功能,需要 IsOpenTelemetry 为true时有效,默认值为false")]
    public bool IsOpenTelemetryMetrics { get; set; }

    /// <summary>
    /// 是否启用分布式追踪功能,需要IsOpenTelemetry为true时有效
    /// <para>用于跟踪和分析分布式系统中的请求流程</para>
    /// <para>默认值为false</para>
    /// </summary>
    /// <remarks>
    /// Whether to enable distributed tracing, effective when IsOpenTelemetry is true.
    /// Used for tracking and analyzing request flow in distributed systems.
    /// Default value is false.
    /// </remarks>
    [Option(nameof(IsOpenTelemetryTracing), DefaultValue = false, Description = "是否启用分布式追踪功能,需要 IsOpenTelemetry为true时有效,默认值为false")]
    public bool IsOpenTelemetryTracing { get; set; }

    /// <summary>
    /// 是否启用OpenTelemetry遥测功能
    /// <para>OpenTelemetry是一个开源的可观测性框架</para>
    /// <para>启用后可以统一管理指标、追踪和日志等可观测性数据</para>
    /// <para>默认值为false</para>
    /// </summary>
    /// <remarks>
    /// Whether to enable OpenTelemetry telemetry.
    /// OpenTelemetry is an open-source observability framework.
    /// When enabled, it provides unified management of metrics, traces, and logs.
    /// Default value is false.
    /// </remarks>
    [Option(nameof(IsOpenTelemetry), DefaultValue = false, Description = "是否启用OpenTelemetry遥测功能,默认值为false")]
    public bool IsOpenTelemetry { get; set; }

    /// <summary>
    /// 是否是Debug打印日志模式,默认值为false
    /// </summary>
    /// <remarks>
    /// Whether to enable debug log mode. Default value is false.
    /// </remarks>
    [Option(nameof(IsDebug), DefaultValue = false, Description = "是否是Debug打印日志模式,默认值为false")]
    public bool IsDebug { get; set; }

    /// <summary>
    /// 是否打印超时日志
    /// </summary>
    /// <remarks>
    /// Whether to print timeout logs.
    /// </remarks>
    [Option(nameof(IsMonitorMessageTimeOut), DefaultValue = false, Description = "是否打印超时日志,默认值为false")]
    public bool IsMonitorMessageTimeOut { get; set; }

    /// <summary>
    /// 处理器超时时间（秒）,默认值为1秒
    /// </summary>
    /// <remarks>
    /// Handler timeout in seconds. Default value is 1 second.
    /// </remarks>
    [Option(nameof(MonitorMessageTimeOutSeconds), DefaultValue = 1, Description = "处理器超时时间（秒）,默认值为1秒,只有IsMonitorMessageTimeOut为true时有效")]
    public int MonitorMessageTimeOutSeconds { get; set; } = 1;

    /// <summary>
    /// 网络发送等待超时时间（秒）,默认值为5秒
    /// </summary>
    /// <remarks>
    /// Network send timeout in seconds. Default value is 5 seconds.
    /// </remarks>
    [Option(nameof(NetWorkSendTimeOutSeconds), DefaultValue = 5, Description = "网络发送等待超时时间（秒）,默认值为5秒,最小值为1秒")]
    public int NetWorkSendTimeOutSeconds { get; set; } = 5;

    /// <summary>
    /// 是否打印发送数据,只有在IsDebug为true时有效,默认值为false
    /// </summary>
    /// <remarks>
    /// Whether to print sent data, effective when IsDebug is true. Default value is false.
    /// </remarks>
    [Option(nameof(IsDebugSend), DefaultValue = false, Description = "是否打印发送数据,只有在IsDebug为true时有效,默认值为false")]
    public bool IsDebugSend { get; set; }

    /// <summary>
    /// 是否打印发送的心跳数据,只有在IsDebugSend为true时有效,默认值为false
    /// </summary>
    /// <remarks>
    /// Whether to print sent heartbeat data, effective when IsDebugSend is true. Default value is false.
    /// </remarks>
    [Option(nameof(IsDebugSendHeartBeat), DefaultValue = false, Description = "是否打印发送的心跳数据,只有在IsDebugSend为true时有效,默认值为false")]
    public bool IsDebugSendHeartBeat { get; set; }

    /// <summary>
    /// 是否打印接收数据,只有在IsDebug为true时有效,默认值为false
    /// </summary>
    /// <remarks>
    /// Whether to print received data, effective when IsDebug is true. Default value is false.
    /// </remarks>
    [Option(nameof(IsDebugReceive), DefaultValue = false, Description = "是否打印接收数据,只有在IsDebug为true时有效,默认值为false")]
    public bool IsDebugReceive { get; set; }

    /// <summary>
    /// 是否打印接收的心跳数据,只有在IsDebugReceive为true时有效,默认值为false
    /// </summary>
    /// <remarks>
    /// Whether to print received heartbeat data, effective when IsDebugReceive is true. Default value is false.
    /// </remarks>
    [Option(nameof(IsDebugReceiveHeartBeat), DefaultValue = false, Description = "是否打印接收的心跳数据,只有在IsDebugReceive为true时有效,默认值为false")]
    public bool IsDebugReceiveHeartBeat { get; set; }

    /// <summary>
    /// 是否启用HTTP调试日志总开关
    /// <para>只有在IsDebug为true时有效</para>
    /// <para>默认值为true</para>
    /// </summary>
    /// <remarks>
    /// Whether to enable HTTP debug log master switch.
    /// Effective when IsDebug is true. Default value is true.
    /// </remarks>
    [Option(nameof(IsDebugHttp), DefaultValue = true, Description = "是否启用HTTP调试日志总开关,只有在IsDebug为true时有效,默认值为true")]
    public bool IsDebugHttp { get; set; } = true;

    /// <summary>
    /// 是否打印HTTP请求参数日志
    /// <para>包括请求方法和参数内容</para>
    /// <para>只有在IsDebugHttp为true时有效</para>
    /// <para>默认值为true</para>
    /// </summary>
    /// <remarks>
    /// Whether to print HTTP request parameter logs.
    /// Includes request method and parameter content.
    /// Effective when IsDebugHttp is true. Default value is true.
    /// </remarks>
    [Option(nameof(IsDebugHttpRequest), DefaultValue = true, Description = "是否打印HTTP请求参数日志,只有在IsDebugHttp为true时有效,默认值为true")]
    public bool IsDebugHttpRequest { get; set; } = true;

    /// <summary>
    /// 是否打印HTTP响应结果日志
    /// <para>在执行时间日志中包含结果内容</para>
    /// <para>只有在IsDebugHttp为true时有效</para>
    /// <para>默认值为true</para>
    /// </summary>
    /// <remarks>
    /// Whether to print HTTP response result logs.
    /// Includes result content in execution time logs.
    /// Effective when IsDebugHttp is true. Default value is true.
    /// </remarks>
    [Option(nameof(IsDebugHttpResponse), DefaultValue = true, Description = "是否打印HTTP响应结果日志,只有在IsDebugHttp为true时有效,默认值为true")]
    public bool IsDebugHttpResponse { get; set; } = true;

    /// <summary>
    /// 服务器ID
    /// </summary>
    /// <remarks>
    /// Server ID.
    /// </remarks>
    [Option(nameof(ServerId), Description = "服务器ID-如果需要合服，请确保不同服的ServerId一样。不然合服后数据会无法处理用户数据")]
    [GrafanaLokiLabelTag]
    public int ServerId { get; set; }

    /// <summary>
    /// 服务器实例ID
    /// </summary>
    /// <remarks>
    /// Server instance ID.
    /// </remarks>
    [Option(nameof(ServerInstanceId), Description = "服务器实例ID-用于区分同一服务器的不同实例")]
    [GrafanaLokiLabelTag]
    public long ServerInstanceId { get; set; }

    /// <summary>
    /// 服务器名称
    /// </summary>
    /// <remarks>
    /// Server name.
    /// </remarks>
    public string ServerName { get; set; }

    /// <summary>
    /// 标记名称
    /// </summary>
    /// <remarks>
    /// Tag name.
    /// </remarks>
    [Option(nameof(TagName), DefaultValue = "", Description = "标签名称-用于区分不同环境的服务器,没有实际用途,只是方便运维管理")]
    [GrafanaLokiLabelTag]
    public string TagName { get; set; }

    /// <summary>
    /// 保存数据的时间间隔（毫秒）
    /// </summary>
    /// <remarks>
    /// Data save interval in milliseconds. Default is 30,000 (30 seconds).
    /// </remarks>
    [Option(nameof(SaveDataInterval), DefaultValue = 30_000, Description = "保存数据间隔,单位毫秒,默认30000毫秒(30秒),最小值为5秒(5000毫秒)")]
    public int SaveDataInterval { get; set; } = 30_000;

    /// <summary>
    /// 保存数据的批量数量长度，默认为500
    /// </summary>
    /// <remarks>
    /// Batch count for saving data. Default is 500.
    /// </remarks>
    [Option(nameof(SaveDataBatchCount), DefaultValue = 500, Description = "保存数据的批量数量长度,默认为500")]
    public int SaveDataBatchCount { get; set; } = 500;

    /// <summary>
    /// 保存数据的超时时间（毫秒）,默认值为30秒
    /// </summary>
    /// <remarks>
    /// Data save batch timeout in milliseconds. Default is 30,000 (30 seconds).
    /// </remarks>
    [Option(nameof(SaveDataBatchTimeOut), DefaultValue = 30_000, Description = "保存数据的超时时间(毫秒),默认值为30秒")]
    public int SaveDataBatchTimeOut { get; set; } = 30_000;

    /// <summary>
    /// Actor 执行任务超时时间（毫秒）,默认值为30秒
    /// </summary>
    /// <remarks>
    /// Actor task execution timeout in milliseconds. Default is 30,000 (30 seconds).
    /// </remarks>
    [Option(nameof(ActorTimeOut), DefaultValue = 30_000, Description = "Actor 执行任务超时时间(毫秒),默认值为30秒")]
    public int ActorTimeOut { get; set; } = 30_000;

    /// <summary>
    /// Actor 空闲多久回收,单位分钟,默认值为15分钟
    /// </summary>
    /// <remarks>
    /// Actor idle recycle time in minutes. Default is 15 minutes.
    /// </remarks>
    [Option(nameof(ActorRecycleTime), DefaultValue = 15, Description = "Actor 空闲多久回收,单位分钟,默认值为15分钟,最小值为1分钟,小于1则强制设置为5分钟")]
    public int ActorRecycleTime { get; set; } = 15;

    /// <summary>
    /// Actor 执行任务队列超时时间（毫秒）,默认值为30秒
    /// </summary>
    /// <remarks>
    /// Actor queue timeout in milliseconds. Default is 30,000 (30 seconds).
    /// </remarks>
    [Option(nameof(ActorQueueTimeOut), DefaultValue = 30_000, Description = "Actor 执行任务队列超时时间(毫秒),默认值为30秒")]
    public int ActorQueueTimeOut { get; set; } = 30_000;

    /// <summary>
    /// 是否启用TCP
    /// </summary>
    /// <remarks>
    /// Whether to enable TCP.
    /// </remarks>
    [Option(nameof(IsEnableTcp), DefaultValue = true, Description = "是否启用 TCP 服务，默认值为 true")]
    public bool IsEnableTcp { get; set; } = true;

    /// <summary>
    /// 是否启用UDP
    /// </summary>
    /// <remarks>
    /// Whether to enable UDP. Default is false.
    /// </remarks>
    [Option(nameof(IsEnableUdp), DefaultValue = false, Description = "是否启用 UDP 服务，默认值为 false")]
    public bool IsEnableUdp { get; set; } = false;

    /// <summary>
    /// 是否启用KCP
    /// </summary>
    /// <remarks>
    /// Whether to enable KCP. Default is false.
    /// </remarks>
    public bool IsEnableKcp { get; set; } = false;

    /// <summary>
    /// KCP端口
    /// </summary>
    /// <remarks>
    /// KCP server port. Default is 0 (uses same port as TCP).
    /// </remarks>
    public int KcpPort { get; set; } = 0;

    /// <summary>
    /// 内部主机地址
    /// </summary>
    /// <remarks>
    /// Internal host address.
    /// </remarks>
    [Option(nameof(InnerHost), DefaultValue = "0.0.0.0", Description = "内部IP")]
    public string InnerHost { get; set; } = "0.0.0.0";

    /// <summary>
    /// 内部端口
    /// </summary>
    /// <remarks>
    /// Internal port.
    /// </remarks>
    [Option(nameof(InnerPort), DefaultValue = 8888, Description = "内部端口")]
    public ushort InnerPort { get; set; } = 8888;

    /// <summary>
    /// 雪花ID的工作ID
    /// </summary>
    /// <remarks>
    /// Snowflake ID worker ID.
    /// </remarks>
    [Option(nameof(WorkerId), DefaultValue = 1, Description = "雪花ID的工作ID,默认为1")]
    public ushort WorkerId { get; set; } = 1;

    /// <summary>
    /// 雪花ID的数据中心ID
    /// </summary>
    /// <remarks>
    /// Snowflake ID data center ID.
    /// </remarks>
    [Option(nameof(DataCenterId), DefaultValue = 1, Description = "雪花ID的数据中心ID,默认为1")]
    public ushort DataCenterId { get; set; } = 1;

    /// <summary>
    /// 外部主机地址
    /// </summary>
    /// <remarks>
    /// External host address.
    /// </remarks>
    [Option(nameof(OuterHost), DefaultValue = "0.0.0.0", Description = "外部IP")]
    public string OuterHost { get; set; } = "0.0.0.0";

    /// <summary>
    /// 外部端口
    /// </summary>
    /// <remarks>
    /// External port.
    /// </remarks>
    [Option(nameof(OuterPort), Description = "外部端口")]
    public ushort OuterPort { get; set; }

    /// <summary>
    /// HTTP地址
    /// </summary>
    /// <remarks>
    /// HTTP URL path.
    /// </remarks>
    [Option(nameof(HttpUrl), DefaultValue = "/game/api/", Description = "API接口根路径,必须以/开头和以/结尾,默认为[/game/api/]")]
    public string HttpUrl { get; set; } = "/game/api/";

    /// <summary>
    /// 是否启用 HTTP 服务
    /// </summary>
    /// <remarks>
    /// Whether to enable HTTP service.
    /// </remarks>
    [Option(nameof(IsEnableHttp), DefaultValue = true, Description = "是否启用 HTTP 服务，默认值为 true")]
    public bool IsEnableHttp { get; set; } = true;

    /// <summary>
    /// HTTP 是否是开发模式
    /// </summary>
    /// <remarks>
    /// Whether HTTP is in development mode.
    /// </remarks>
    [Option(nameof(HttpIsDevelopment), DefaultValue = false, Description = "HTTP 是否是开发模式,当是开发模式的时候将会启用Swagger")]
    public bool HttpIsDevelopment { get; set; }

    /// <summary>
    /// HTTP端口
    /// </summary>
    /// <remarks>
    /// HTTP port.
    /// </remarks>
    [Option(nameof(HttpPort), DefaultValue = 8080, Description = "HTTP 端口")]
    public ushort HttpPort { get; set; } = 8080;

    /// <summary>
    /// HTTPS端口
    /// </summary>
    /// <remarks>
    /// HTTPS port.
    /// </remarks>
    [Option(nameof(HttpsPort), Description = "HTTPS 端口")]
    public ushort HttpsPort { get; set; }

    /// <summary>
    /// HTTP 请求体最大字节数。
    /// </summary>
    /// <remarks>
    /// Maximum HTTP request body size in bytes.
    /// </remarks>
    [Option(nameof(HttpMaxRequestBodyBytes), DefaultValue = 1048576L, Description = "HTTP 请求体最大字节数,默认 1MB")]
    public long HttpMaxRequestBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// HTTP JSON 请求体最大字节数。
    /// </summary>
    /// <remarks>
    /// Maximum HTTP JSON request body size in bytes.
    /// </remarks>
    [Option(nameof(HttpMaxJsonBodyBytes), DefaultValue = 1048576L, Description = "HTTP JSON 请求体最大字节数,默认 1MB")]
    public long HttpMaxJsonBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// HTTP ProtoBuf 请求体最大字节数。
    /// </summary>
    /// <remarks>
    /// Maximum HTTP ProtoBuf request body size in bytes.
    /// </remarks>
    [Option(nameof(HttpMaxProtoBodyBytes), DefaultValue = 1048576L, Description = "HTTP ProtoBuf 请求体最大字节数,默认 1MB")]
    public long HttpMaxProtoBodyBytes { get; set; } = 1024 * 1024;

    /// <summary>
    /// HTTP 是否全局要求签名校验。
    /// </summary>
    /// <remarks>
    /// Whether all HTTP handlers require signature validation globally.
    /// </remarks>
    [Option(nameof(HttpRequireSign), DefaultValue = false, Description = "HTTP是否全局要求签名校验,默认false。生产环境可开启")]
    public bool HttpRequireSign { get; set; }

    /// <summary>
    /// HTTP CORS 允许的 Origin 白名单，多个值用逗号或分号分隔。
    /// </summary>
    /// <remarks>
    /// HTTP CORS allowed origin whitelist, separated by comma or semicolon.
    /// </remarks>
    [Option(nameof(HttpCorsAllowedOrigins), DefaultValue = "", Description = "HTTP CORS允许的Origin白名单,多个值用逗号或分号分隔。生产环境空值时不启用CORS")]
    public string HttpCorsAllowedOrigins { get; set; }

    /// <summary>
    /// Prometheus指标端口（如果为0则使用HTTP端口）
    /// </summary>
    /// <remarks>
    /// Prometheus metrics port (uses HTTP port if 0).
    /// </remarks>
    [Option(nameof(MetricsPort), Description = "Metrics 端口")]
    public ushort MetricsPort { get; set; }

    /// <summary>
    /// 是否启用 WebSocket 服务
    /// <para>开启后服务器将监听 WebSocket 端口，允许客户端通过 WebSocket 协议进行连接</para>
    /// <para>默认值为 false，即不启用</para>
    /// </summary>
    /// <remarks>
    /// Whether to enable WebSocket service.
    /// When enabled, the server will listen on WebSocket port for client connections.
    /// Default value is false.
    /// </remarks>
    [Option(nameof(IsEnableWebSocket), DefaultValue = false, Description = "是否启用 WebSocket 服务，默认值为 false")]
    public bool IsEnableWebSocket { get; set; } = false;

    /// <summary>
    /// WebSocket端口
    /// </summary>
    /// <remarks>
    /// WebSocket port.
    /// </remarks>
    [Option(nameof(WsPort), DefaultValue = 8889, Description = "WebSocket 端口，默认值为 8889，当 IsEnableWebSocket 为 true 时才会启用")]
    public ushort WsPort { get; set; } = 8889;

    /// <summary>
    /// WebSocket加密端口
    /// </summary>
    /// <remarks>
    /// WebSocket secure port.
    /// </remarks>
    [Option(nameof(WssPort), Description = "WebSocket 加密端口")]
    public ushort WssPort { get; set; }

    /// <summary>
    /// Wss使用的证书路径
    /// </summary>
    /// <remarks>
    /// Certificate file path for WSS.
    /// </remarks>
    [Option(nameof(WssCertFilePath), Description = "Wss 使用的证书路径")]
    public string WssCertFilePath { get; set; }

    /// <summary>
    /// 数据库地址
    /// </summary>
    /// <remarks>
    /// Database URL.
    /// </remarks>
    [Option(nameof(DataBaseUrl), Description = "数据库 地址")]
    public string DataBaseUrl { get; set; }

    /// <summary>
    /// 数据库名称
    /// </summary>
    /// <remarks>
    /// Database name.
    /// </remarks>
    [Option(nameof(DataBaseName), Description = "数据库名称")]
    public string DataBaseName { get; set; }

    /// <summary>
    /// 数据库密码
    /// </summary>
    /// <remarks>
    /// Database password.
    /// </remarks>
    [Option(nameof(DataBasePassword), Description = "数据库密码")]
    public string DataBasePassword { get; set; }

    /// <summary>
    /// 服务器时区
    /// </summary>
    /// <remarks>
    /// Server time zone identifier.
    /// </remarks>
    [Option(nameof(TimeZone), DefaultValue = "Asia/Shanghai", Description = "服务器时区设置，默认为 Asia/Shanghai，支持 IANA 时区数据库标准标识符")]
    public string TimeZone { get; set; } = "Asia/Shanghai";

    /// <summary>
    /// 是否使用时区时间记录
    /// <para>启用后数据库时间戳将包含时区偏移量</para>
    /// <para>默认值为 false</para>
    /// </summary>
    /// <remarks>
    /// Whether to use time zone for time recording.
    /// When enabled, database timestamps will include time zone offset.
    /// Default value is false.
    /// </remarks>
    [Option(nameof(IsUseTimeZone), DefaultValue = false, Description = "是否启用自定义时区设置，默认为 false，禁用时使用系统默认时区")]
    public bool IsUseTimeZone { get; set; } = false;

    /// <summary>
    /// 语言
    /// </summary>
    /// <remarks>
    /// Language.
    /// </remarks>
    [Option(nameof(Language), Description = "语言")]
    [GrafanaLokiLabelTag]
    public string Language { get; set; }

    /// <summary>
    /// 数据中心
    /// </summary>
    /// <remarks>
    /// Data center.
    /// </remarks>
    [Option(nameof(DataCenter), Description = "数据中心")]
    public string DataCenter { get; set; }

    /// <summary>
    /// 最大客户端数量
    /// </summary>
    /// <remarks>
    /// Maximum client count. Default is 3000.
    /// </remarks>
    public int MaxClientCount { get; set; } = 3000;

    /// <summary>
    /// 游戏逻辑服务器的处理最小模块ID
    /// </summary>
    /// <remarks>
    /// Minimum module ID for game logic server.
    /// </remarks>
    [Option(nameof(MinModuleId), Description = "游戏逻辑服务器的处理最小模块ID")]
    public short MinModuleId { get; set; }

    /// <summary>
    /// 游戏逻辑服务器的处理最大模块ID
    /// </summary>
    /// <remarks>
    /// Maximum module ID for game logic server.
    /// </remarks>
    [Option(nameof(MaxModuleId), Description = "游戏逻辑服务器的处理最大模块ID")]
    public short MaxModuleId { get; set; }

    /// <summary>
    /// 描述信息
    /// </summary>
    /// <remarks>
    /// Description.
    /// </remarks>
    [Option(nameof(Description), DefaultValue = "", Description = "描述信息-用于描述该服务器的用途,没有实际用途,只是方便运维管理")]
    [GrafanaLokiLabelTag]
    public string Description { get; set; }

    /// <summary>
    /// 备注信息
    /// </summary>
    /// <remarks>
    /// Note.
    /// </remarks>
    [Option(nameof(Note), DefaultValue = "", Description = "备注信息-用于描述该服务器的备注信息,没有实际用途,只是方便运维管理")]
    [GrafanaLokiLabelTag]
    public string Note { get; set; }

    /// <summary>
    /// 标签信息
    /// </summary>
    /// <remarks>
    /// Label.
    /// </remarks>
    [Option(nameof(Label), DefaultValue = "", Description = "标签信息-用于描述该服务器的标签信息,没有实际用途,只是方便运维管理")]
    [GrafanaLokiLabelTag]
    public string Label { get; set; }

    /// <summary>
    /// 客户端API地址
    /// </summary>
    /// <remarks>
    /// Client API host.
    /// </remarks>
    [Option(nameof(ClientApiHost), DefaultValue = "", Description = "客户端API地址")]
    public string ClientApiHost { get; set; }

    /// <summary>
    /// HubAPI地址
    /// </summary>
    /// <remarks>
    /// Hub API host.
    /// </remarks>
    [Option(nameof(HubApiHost), DefaultValue = "", Description = "HubAPI地址")]
    public string HubApiHost { get; set; }

    #endregion
}
