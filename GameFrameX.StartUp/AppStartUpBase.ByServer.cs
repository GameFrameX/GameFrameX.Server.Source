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


using System.Runtime.CompilerServices;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Network.Message;
using GameFrameX.Network.RemoteMessaging.Versioning;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Primitives;
using GameFrameX.SuperSocket.ProtoBase;
using GameFrameX.SuperSocket.Server;
using GameFrameX.SuperSocket.Server.Abstractions;
using GameFrameX.SuperSocket.Server.Abstractions.Host;
using GameFrameX.SuperSocket.Server.Abstractions.Middleware;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using GameFrameX.SuperSocket.Server.Host;
using GameFrameX.SuperSocket.Kcp;
using GameFrameX.SuperSocket.Udp;
using GameFrameX.SuperSocket.WebSocket;
using GameFrameX.SuperSocket.WebSocket.Server;
using GameFrameX.Utility;
using GameFrameX.Utility.Runtime;
using GameFrameX.Utility.Setting;
using GameFrameX.StartUp.ServiceDefaults;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using CloseReason = GameFrameX.SuperSocket.WebSocket.CloseReason;

namespace GameFrameX.StartUp;

/// <summary>
/// 程序启动器基类 - 提供服务器的基础功能实现。
/// </summary>
/// <remarks>
/// Application startup base class - provides server basic functionality implementation.
/// This partial class specifically handles TCP, KCP and WebSocket server startup and configuration functionality.
/// </remarks>
public abstract partial class AppStartUpBase
{
    /// <summary>
    /// 启动服务器 - 同时启动 TCP、KCP 和 WebSocket 服务。
    /// </summary>
    /// <remarks>
    /// Start server - simultaneously start TCP, KCP, and WebSocket services.
    /// This method is responsible for initializing message encoders/decoders, starting various network services, and setting the global startup status.
    /// </remarks>
    /// <typeparam name="TMessageDecoderHandler">消息解码处理器类型，必须实现 <see cref="IMessageDecoderHandler"/> 和 <see cref="IPackageDecoder{TPackageInfo}"/> 接口 / Message decoder handler type, must implement IMessageDecoderHandler and IPackageDecoder interfaces</typeparam>
    /// <typeparam name="TMessageEncoderHandler">消息编码处理器类型，必须实现 <see cref="IMessageEncoderHandler"/> 和 <see cref="IPackageEncoder{TPackageInfo}"/> 接口 / Message encoder handler type, must implement IMessageEncoderHandler and IPackageEncoder interfaces</typeparam>
    /// <param name="messageCompressHandler">消息编码时使用的压缩处理器；如果为空则不处理压缩消息 / Compression handler used when encoding messages; no compression processing if null</param>
    /// <param name="messageDecompressHandler">消息解码时使用的解压处理器；如果为空则不处理压缩消息 / Decompression handler used when decoding messages; no decompression processing if null</param>
    /// <param name="baseHandler">HTTP 处理器列表，用于处理不同的 HTTP 请求 / HTTP handler list for processing different HTTP requests</param>
    /// <param name="httpFactory">HTTP 处理器工厂，根据命令标识符创建对应的处理器实例 / HTTP handler factory that creates corresponding handler instances based on command identifiers</param>
    /// <param name="minimumLevelLogLevel">日志记录的最小级别，用于控制日志输出 / Minimum level for logging to control log output</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected async Task StartServerAsync<TMessageDecoderHandler, TMessageEncoderHandler>(
        IMessageCompressHandler messageCompressHandler,
        IMessageDecompressHandler messageDecompressHandler,
        List<BaseHttpHandler> baseHandler, Func<string, BaseHttpHandler> httpFactory, LogLevel minimumLevelLogLevel = LogLevel.Debug)
        where TMessageDecoderHandler : class, IMessageDecoderHandler, new()
        where TMessageEncoderHandler : class, IMessageEncoderHandler, new()
    {
        MessageHelper.SetMessageDecoderHandler(Activator.CreateInstance<TMessageDecoderHandler>(), messageDecompressHandler);
        MessageHelper.SetMessageEncoderHandler(Activator.CreateInstance<TMessageEncoderHandler>(), messageCompressHandler);
        // aop 处理器装配：反射收集 IHttpAopHandler 实现并按 Priority 升序，HTTP 拦截按声明优先级生效
        var aopHandlers = AssemblyHelper.GetRuntimeImplementTypeNamesInstance<IHttpAopHandler>();
        // 排序
        aopHandlers.Sort((handlerX, handlerY) => handlerX.Priority.CompareTo(handlerY.Priority));
        // 启动服务器
        await StartServerCoreAsync(baseHandler, httpFactory, aopHandlers, minimumLevelLogLevel);
        // 设置全局启动状态
        GameAppRuntime.MarkStarted(DateTime.UtcNow);
    }

    /// <summary>
    /// 停止服务器 - 关闭所有网络服务。
    /// </summary>
    /// <remarks>
    /// Stop server - close all network services.
    /// This method is responsible for gracefully closing all network services and connections.
    /// </remarks>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected async Task StopServerAsync()
    {
        GameAppRuntime.MarkStopping();

        if (_gameServer != null)
        {
            await _gameServer.StopAsync();
            _gameServer = null;
        }

        // HTTP 宿主最后停：此刻同 Role 的能力族（Actor/业务服务）与 socket 宿主均已停止，满足「HTTP 停机晚于其它服务」的顺序约束
        await StopHttpServerAsync();
    }

    /// <summary>
    /// 消息处理异常处理方法。
    /// </summary>
    /// <remarks>
    /// Message processing exception handler.
    /// Handles exceptions that occur during message processing.
    /// </remarks>
    /// <param name="appSession">会话对象 / Session object</param>
    /// <param name="exception">异常信息 / Exception information</param>
    /// <returns>返回 <c>true</c> 表示继续处理；返回 <c>false</c> 表示终止处理 / Returns <c>true</c> to continue processing; returns <c>false</c> to terminate processing</returns>
    protected virtual ValueTask<bool> PackageErrorHandlerAsync(IAppSession appSession, PackageHandlingException<IMessage> exception)
    {
        return ValueTask.FromResult(true);
    }

    /// <summary>
    /// 客户端断开连接时的处理方法。
    /// </summary>
    /// <remarks>
    /// Handler when client disconnects.
    /// Handles the situation when a client disconnects, recording relevant information and
    /// closing the session-scoped network channel created by the package funnel so its
    /// resources (the channel-level cancellation source) are released deterministically
    /// instead of waiting for garbage collection.
    /// </remarks>
    /// <param name="appSession">断开连接的会话对象 / Session object that disconnected</param>
    /// <param name="disconnectEventArgs">断开连接的相关参数 / Parameters related to disconnection</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected virtual ValueTask OnDisconnectedAsync(IAppSession appSession, CloseEventArgs disconnectEventArgs)
    {
        // Localization: StartUp.Server.ClientDisconnected - 客户端断开连接 - 会话ID: {0}, 远程终端: {1}, 断开原因: {2}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Server.ClientDisconnected, appSession.SessionId, appSession.RemoteEndPoint, disconnectEventArgs.Reason));
        CloseSessionNetworkChannel(appSession);
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 客户端连接成功时的处理方法。
    /// </summary>
    /// <remarks>
    /// Handler when client connects successfully.
    /// Handles the situation when a new client connects, recording relevant information.
    /// </remarks>
    /// <param name="appSession">新建立的会话对象 / Newly established session object</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected virtual ValueTask OnConnectedAsync(IAppSession appSession)
    {
        // Localization: StartUp.Server.NewClientConnection - 新客户端连接 - 会话ID: {0}, 远程终端: {1}
        LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Server.NewClientConnection, appSession.SessionId, appSession.RemoteEndPoint));
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 收到消息包的处理方法（漏斗缺省：完整消息分发）。
    /// </summary>
    /// <remarks>
    /// Handler for received message packages — the single funnel shared by the TCP, KCP and
    /// WebSocket listeners. Non-package messages only hit the debug log. Package messages go
    /// through the full dispatch: the session-cached network channel, the framework-level
    /// heartbeat short-circuit (<see cref="ReplyHeartBeatAsync"/>), handler resolution via
    /// <see cref="ResolveMessageHandler"/> with a clean "handler not found" error log, and finally
    /// <see cref="InvokeMessageHandlerAsync"/>.
    /// </remarks>
    /// <param name="session">会话对象 / Session object</param>
    /// <param name="message">接收到的消息 / Received message</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected virtual async ValueTask PackageHandlerAsync(IAppSession session, IMessage message)
    {
        if (message is not INetworkMessagePackage messagePackage)
        {
            LogReceiveMessageDebug(message);
            return;
        }

        var networkChannel = GetSessionNetworkChannel(session);

        // 心跳是框架级保活语义（鉴权中间件亦白名单放行），优先短路直接回包，不进入业务 handler 分发
        if (MessageProtoHelper.IsHeartbeat(messagePackage.MessageType))
        {
            // 畸形心跳包（合法心跳 msgId + 非法 protobuf 体）可令日志反序列化（ToFormatMessageString）或回包路径抛异常，
            // 分支整体兜底对齐 InvokeMessageHandler 的错误日志契约，避免异常逸出被传输层关闭会话
            try
            {
                // 心跳日志门控与两处覆写对齐：三开关单点检（IsDebug + IsDebugReceive + IsDebugReceiveHeartBeat）
                if (Setting.IsDebug && Setting.IsDebugReceive && Setting.IsDebugReceiveHeartBeat)
                {
                    // Localization: StartUp.Server.MessageReceived - 接收到消息 - 服务器类型: [{0}], 消息内容: {1}
                    LogHelper.Debug(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageReceived, ServerType, message.ToFormatMessageString()));
                }

                networkChannel.UpdateReceiveMessageTime();
                await ReplyHeartBeatAsync(networkChannel, messagePackage);
            }
            catch (Exception e)
            {
                // Localization: StartUp.Server.MessageHandlerError - 消息处理器错误: {0}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageHandlerError, e.ToString()));
            }

            return;
        }

        // 协议版本握手短路（框架级，位于业务 handler 分发之前）：major 不符回拒绝包并关闭会话；
        // 旧拨号端不发握手帧，此处不触发，行为与升级前等价
        if (ProtocolVersionNegotiationServer.IsHandshakeRequest(messagePackage))
        {
            try
            {
                networkChannel.UpdateReceiveMessageTime();
                await ProtocolVersionNegotiationServer.ReplyAsync(networkChannel, messagePackage);
            }
            catch (Exception e)
            {
                // Localization: StartUp.Server.MessageHandlerError - 消息处理器错误: {0}
                LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageHandlerError, e.ToString()));
            }

            return;
        }

        LogReceiveMessageDebug(message);

        var handler = ResolveMessageHandler(messagePackage.Header.MessageId);
        if (handler == null)
        {
            // Localization: StartUp.Server.MessageHandlerNotFound - 接收消息: 找不到消息处理器, 消息ID: {0}, 消息类型: {1}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageHandlerNotFound, messagePackage.Header.MessageId, messagePackage.MessageType));
            return;
        }

        await InvokeMessageHandlerAsync(handler, messagePackage.DeserializeMessageObject(), networkChannel);
    }

    /// <summary>
    /// 在开启接收调试时打印收包日志。
    /// </summary>
    /// <remarks>
    /// Logs the received message when receive debugging is enabled.
    /// </remarks>
    /// <param name="message">接收到的消息 / The received message</param>
    private void LogReceiveMessageDebug(IMessage message)
    {
        if (Setting.IsDebug && Setting.IsDebugReceive)
        {
            // Localization: StartUp.Server.MessageReceived - 接收到消息 - 服务器类型: [{0}], 消息内容: {1}
            LogHelper.Debug(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageReceived, ServerType, message.ToFormatMessageString()));
        }
    }

    /// <summary>
    /// 解析消息处理器（分发 seam，缺省无解析源）。
    /// </summary>
    /// <remarks>
    /// The message-handler resolution seam of the package funnel (protocol-agnostic: the same
    /// funnel serves TCP, KCP and WebSocket). This project sits below the hotfix registry
    /// (Core) and cannot resolve handlers itself, so the default returns <c>null</c>
    /// (dispatch-ready but source-less); composition layers above (Launcher) override this to
    /// bridge <c>HotfixManager.GetMessageHandler</c>. A <c>null</c> result hits the funnel's
    /// "handler not found" error log instead of a NullReferenceException.
    /// </remarks>
    /// <param name="messageId">消息ID / The message identifier</param>
    /// <returns>处理器实例；未注册或无解析源时为 <c>null</c> / The handler instance, or <c>null</c> when unregistered or unresolved</returns>
    protected virtual IMessageHandler ResolveMessageHandler(int messageId)
    {
        return null;
    }

    /// <summary>
    /// 回复心跳消息（协议 seam，缺省不回复）。
    /// </summary>
    /// <remarks>
    /// The heartbeat reply seam of the package funnel. The concrete keepalive protocol types
    /// (<c>ReqHeartBeat</c>/<c>NotifyHeartBeat</c>) live in the Proto project above Core,
    /// which is invisible from here, so the default is a no-op; composition layers above
    /// (Launcher) override this to emit the actual reply. The receive-time stamping
    /// (<c>UpdateReceiveMessageTime</c>) happens in the funnel before this hook.
    /// </remarks>
    /// <param name="networkChannel">会话网络通道 / The session network channel</param>
    /// <param name="messagePackage">心跳消息包 / The heartbeat message package</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected virtual ValueTask ReplyHeartBeatAsync(INetworkChannel networkChannel, INetworkMessagePackage messagePackage)
    {
        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// 会话级网络通道缓存：每会话恰一个通道，断线时经 <see cref="CloseSessionNetworkChannel"/>
    /// 显式关闭并摘除，未及断线回调的残余条目随会话对象回收而释放。
    /// </summary>
    /// <remarks>
    /// The session-scoped channel cache: exactly one channel per session, explicitly closed
    /// and removed on disconnect via <see cref="CloseSessionNetworkChannel"/>; entries whose
    /// disconnect callback never arrives are released when the session object is collected.
    /// Channel user data (e.g. the actor binding written at login) survives across the
    /// messages of the same session — the per-message channel of the previous shape dropped
    /// it on every package.
    /// </remarks>
    private readonly ConditionalWeakTable<IAppSession, INetworkChannel> _sessionNetworkChannels = new();

    /// <summary>
    /// 取会话级网络通道（无则创建）。
    /// </summary>
    /// <remarks>
    /// Gets the session-scoped network channel, creating it on first use. The creation
    /// factory may run more than once under a race, but only one channel instance wins.
    /// </remarks>
    /// <param name="session">会话对象 / The session</param>
    /// <returns>该会话的网络通道 / The session's network channel</returns>
    private INetworkChannel GetSessionNetworkChannel(IAppSession session)
    {
        // 通道用户数据（如登录时的 ActorId 绑定）跨消息存活；断线时经 CloseSessionNetworkChannel 关闭并摘除
        return _sessionNetworkChannels.GetValue(session, CreateSessionNetworkChannel);
    }

    /// <summary>
    /// 创建指定会话的网络通道。
    /// </summary>
    /// <remarks>
    /// Creates the network channel for the specified session. This is a virtual factory method
    /// that can be overridden by derived classes to customize the channel creation behavior,
    /// using the current startup settings to initialize the default channel.
    /// </remarks>
    /// <param name="session">会话对象 / The session</param>
    /// <returns>该会话的网络通道 / The session's network channel</returns>
    protected virtual INetworkChannel CreateSessionNetworkChannel(IAppSession session)
    {
        return new DefaultNetworkChannel(session, Setting);
    }

    /// <summary>
    /// 关闭并摘除会话级网络通道缓存中的通道（断线路径资源释放）。
    /// </summary>
    /// <remarks>
    /// Closes and removes the session-scoped channel from the cache — the disconnect-path
    /// resource release so the channel-level cancellation source is disposed deterministically
    /// instead of waiting for garbage collection. No-op when the session never had a channel
    /// (e.g. disconnected before any package arrived); <see cref="INetworkChannel.Close"/> is
    /// idempotent, so an already-closed channel is harmless.
    /// </remarks>
    /// <param name="session">断开连接的会话对象 / The session that disconnected</param>
    protected void CloseSessionNetworkChannel(IAppSession session)
    {
        if (_sessionNetworkChannels.TryGetValue(session, out var channel))
        {
            channel.Close();
            _sessionNetworkChannels.Remove(session);
        }
    }

    /// <summary>
    /// 异步消息处理方法。
    /// </summary>
    /// <remarks>
    /// Asynchronous message processing method.
    /// Asynchronously invokes the message handler, including initialization and execution logic.
    /// </remarks>
    /// <param name="handler">消息处理器 / Message handler</param>
    /// <param name="message">网络消息 / Network message</param>
    /// <param name="networkChannel">网络通道 / Network channel</param>
    /// <param name="timeout">超时时间（毫秒）/ Timeout (milliseconds)</param>
    /// <param name="cancellationToken">取消令牌 / Cancellation token</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    protected async Task InvokeMessageHandlerAsync(IMessageHandler handler, INetworkMessage message, INetworkChannel networkChannel, int timeout = 30000, CancellationToken cancellationToken = default)
    {
        try
        {
            var initSuccess = await handler.InitAsync(message, networkChannel);
            if (!initSuccess)
            {
                return;
            }

            await handler.InnerActionAsync(timeout, cancellationToken);
        }
        catch (Exception ex)
        {
            // Localization: StartUp.Server.MessageHandlerError - 消息处理器错误: {0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.StartUp.Server.MessageHandlerError, ex.ToString()));
        }
    }

    #region TCP Server

    /// <summary>
    /// 游戏服务器主机实例。
    /// </summary>
    /// <remarks>
    /// Game server host instance.
    /// Used to manage the lifecycle of TCP and WebSocket servers.
    /// </remarks>
    /// <value>用于管理游戏服务器生命周期的主机实例 / Host instance for managing the game server lifecycle</value>
    private IHost _gameServer;

    /// <summary>
    /// 启动 TCP 服务器。
    /// </summary>
    /// <remarks>
    /// Start TCP server.
    /// This method is responsible for configuring and starting TCP, WebSocket, and HTTP servers, as well as related monitoring and logging functionality.
    /// </remarks>
    /// <param name="baseHandler">HTTP 处理器列表，用于处理不同的 HTTP 请求 / HTTP handler list for processing different HTTP requests</param>
    /// <param name="httpFactory">HTTP 处理器工厂，根据命令标识符创建对应的处理器实例 / HTTP handler factory that creates corresponding handler instances based on command identifiers</param>
    /// <param name="aopHandlerTypes">AOP 处理器列表，用于在 HTTP 请求处理前后执行额外的逻辑 / AOP handler list for executing additional logic before and after HTTP request processing</param>
    /// <param name="minimumLevelLogLevel">日志记录的最小级别，用于控制日志输出 / Minimum level for logging to control log output</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    private async Task StartServerCoreAsync(List<BaseHttpHandler> baseHandler, Func<string, BaseHttpHandler> httpFactory, List<IHttpAopHandler> aopHandlerTypes = null, LogLevel minimumLevelLogLevel = LogLevel.Debug)
    {
        var multipleServerHostBuilder = MultipleServerHostBuilder.Create();
        ConfigureTcpServer(multipleServerHostBuilder);
        ConfigureKcpServer(multipleServerHostBuilder);
        ConfigureInnerKcpServer(multipleServerHostBuilder);
        ConfigureWebSocketServer(multipleServerHostBuilder);

        // await StartHttpServerAsync(hostBuilder,baseHandler, httpFactory, aopHandlerTypes, minimumLevelLogLevel);
        await StartHttpServerAsync(baseHandler, httpFactory, aopHandlerTypes, minimumLevelLogLevel);

        // 配置日志
        ConfigureHostLogging(multipleServerHostBuilder, minimumLevelLogLevel);
        // 配置监控和跟踪
        multipleServerHostBuilder.ConfigureServices(services => { services.AddServiceDefaults(Setting.IsOpenTelemetry, Setting.IsOpenTelemetryMetrics, Setting.IsOpenTelemetryTracing); });

        // 构建并启动服务器
        _gameServer = multipleServerHostBuilder.Build();

        await _gameServer.StartAsync();
    }

    /// <summary>
    /// 配置 TCP 服务器（含可选 UDP）。
    /// </summary>
    /// <remarks>Configure the TCP server (with optional UDP). Extracted from <see cref="StartServerCoreAsync"/> to keep cognitive complexity under the Sonar S3776 threshold.</remarks>
    /// <param name="multipleServerHostBuilder">多服务器主机构建器 / Multiple server host builder</param>
    private void ConfigureTcpServer(MultipleServerHostBuilder multipleServerHostBuilder)
    {
        if (!Setting.IsEnableTcp)
        {
            // Localization: StartUp.TcpServer.ServerDisabled - 启动TCP服务器 类型: {0}, 地址: {1}, 端口: {2}, 原因: TCP服务器被禁用
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.TcpServer.ServerDisabled, ServerType, Setting.InnerHost, Setting.InnerPort));
            return;
        }

        // 检查TCP端口是否可用
        if (Setting.InnerPort > 0 && NetHelper.PortIsAvailable(Setting.InnerPort))
        {
            // Localization: StartUp.TcpServer.StartingServer - 启动TCP服务器 类型: {0}, 地址: {1}, 端口: {2}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.TcpServer.StartingServer, ServerType, Setting.InnerHost, Setting.InnerPort));
            multipleServerHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
            {
                var serverBuilder = builder
                                    .UseClearIdleSession()
                                    .UseSessionHandler(OnConnectedAsync, OnDisconnectedAsync)
                                    .UsePackageHandler(PackageHandlerAsync, PackageErrorHandlerAsync)
                                    .UseInProcSessionContainer();

                // 启用UDP 检查是否可用
                if (Setting.IsEnableUdp)
                {
                    serverBuilder.UseUdp();
                }

                serverBuilder.ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        var listenOptions = new ListenOptions
                        {
                            Ip = "Any",
                            Port = Setting.InnerPort,
                        };
                        options.AddListener(listenOptions);
                    });
                    // foreach (var serviceDescriptor in serviceCollection)
                    // {
                    //     if (serviceDescriptor.ServiceType == typeof(IPackageDecoder<IMessage>))
                    //     {
                    //         serviceDescriptor.ImplementationInstance ;
                    // Localization: StartUp.TcpServer.StartupComplete - 启动TCP服务器完成 类型: {0}, 地址: {1}, 端口: {2}
                    //         LogHelper.Info($"XX");
                    //     }
                    // }
                });
            });
            // Localization: StartUp.TcpServer.StartupComplete - 启动TCP服务器完成 类型: {0}, 地址: {1}, 端口: {2}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.TcpServer.StartupComplete, ServerType, Setting.InnerHost, Setting.InnerPort));
        }
        else
        {
            // Localization: StartUp.TcpServer.StartupFailed - 启动TCP服务器失败 类型: {0}, 地址: {1}, 端口: {2}, 原因: 端口无效或被占用
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.StartUp.TcpServer.StartupFailed, ServerType, Setting.InnerHost, Setting.InnerPort));
            LogPortOccupationDetails("TCP", Setting.InnerPort);
        }
    }

    /// <summary>
    /// 创建 KCP 会话首消息鉴权配置。
    /// </summary>
    /// <remarks>
    /// Creates the KCP session first-message authentication options.
    /// KCP 无握手：服务端收到未知 conv 的任意 UDP 包即建连，必须启用首消息鉴权防线。
    /// 默认返回空白名单的严格配置（fail-closed：未鉴权会话除心跳外全部拦截），
    /// 需要放行登录流的宿主应 override 本方法并填充 <see cref="SessionAuthenticationOptions.AllowedMessageIds"/>
    /// （登录流程消息）与 <see cref="SessionAuthenticationOptions.AuthenticatedByMessageIds"/>（鉴权完成消息，如角色登录）。
    /// </remarks>
    /// <returns>KCP 会话鉴权配置 / The KCP session authentication options</returns>
    protected virtual SessionAuthenticationOptions CreateKcpSessionAuthenticationOptions()
    {
        return new SessionAuthenticationOptions();
    }

    /// <summary>
    /// 配置 KCP 服务器（基于 GameFrameX.SuperSocket.Kcp）。
    /// </summary>
    /// <remarks>
    /// Configure the KCP server. 与 <see cref="ConfigureTcpServer"/> 同构：开关检查 → 端口检查 → AddServer + UseKcp + 共用连接/断开/消息回调 → 生命周期日志。
    /// KCP 与 TCP 共享 OnConnected / OnDisconnected / PackageHandler / PackageErrorHandler 四个回调，会话走与 TCP 相同的 InProcSessionContainer。
    /// 在此之上叠加 KCP 专属的首消息鉴权防线：<see cref="SessionAuthenticationMiddleware"/>（仅挂载到本 KCP server，
    /// 负责未鉴权会话的超时主动关闭）+ UsePackageHandler 前置鉴权包装（未鉴权期仅放行心跳与白名单消息，包装内部仍转发共享 PackageHandler，不新增回调签名）。
    /// </remarks>
    /// <param name="multipleServerHostBuilder">多服务器主机构建器 / Multiple server host builder</param>
    private void ConfigureKcpServer(MultipleServerHostBuilder multipleServerHostBuilder)
    {
        if (!Setting.IsEnableKcp)
        {
            // Localization: StartUp.Kcp.ServerDisabled - {0} KCP 服务未启用（{1}:{2}），跳过监听器装配（需 IsEnableKcp 显式开启）。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Kcp.ServerDisabled, ServerType, Setting.InnerHost, Setting.KcpPort));
            return;
        }

        // KCP 端口必须在 (0, ushort.MaxValue] 范围内（与 WsPort 守卫同构）
        if (Setting.KcpPort is > 0 and <= ushort.MaxValue && NetHelper.PortIsAvailable(Setting.KcpPort))
        {
            // Localization: StartUp.Kcp.StartingServer - 正在启动 {0} KCP 服务，监听 {1}:{2}。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Kcp.StartingServer, ServerType, Setting.InnerHost, Setting.KcpPort));
            var authenticationOptions = CreateKcpSessionAuthenticationOptions();
            var authenticationMiddleware = new SessionAuthenticationMiddleware(authenticationOptions);
            multipleServerHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
            {
                builder
                    .UseKcp(o =>
                    {
                        // 实时游戏推荐配置：NoDelay+小 Interval+中等 MTU+1 分钟空闲超时
                        o.NoDelay = true;
                        o.Interval = 10;
                        o.Mtu = 1400;
                        o.IdleTimeout = 60000;
                    })
                    .UseClearIdleSession()
                    .UseSessionHandler(OnConnectedAsync, OnDisconnectedAsync)
                    .UsePackageHandler((session, message) => HandleKcpPackageWithAuthenticationAsync(authenticationMiddleware, session, message), PackageErrorHandlerAsync)
                    .UseInProcSessionContainer()
                    // UseMiddleware 返回非泛型 ISuperSocketHostBuilder，须置于泛型链末尾（其后仅剩 IHostBuilder 级调用）
                    .UseMiddleware<SessionAuthenticationMiddleware>(_ => authenticationMiddleware)
                    .ConfigureServices((context, serviceCollection) =>
                    {
                        serviceCollection.Configure<ServerOptions>(options =>
                        {
                            var listenOptions = new ListenOptions
                            {
                                Ip = "Any",
                                Port = Setting.KcpPort,
                            };
                            options.AddListener(listenOptions);
                        });
                    });
            });
            // Localization: StartUp.Kcp.StartupComplete - {0} KCP 服务已成功启动，监听 {1}:{2}。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.Kcp.StartupComplete, ServerType, Setting.InnerHost, Setting.KcpPort));
        }
        else
        {
            // Localization: StartUp.Kcp.StartupFailed - {0} KCP 服务启动失败（{1}:{2}），端口不可用或越界。
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.StartUp.Kcp.StartupFailed, ServerType, Setting.InnerHost, Setting.KcpPort));
            LogPortOccupationDetails("KCP", Setting.KcpPort);
        }
    }

    /// <summary>
    /// KCP 消息处理前置鉴权包装：未通过首消息鉴权的消息不进入业务 PackageHandler。
    /// </summary>
    /// <remarks>
    /// Pre-authentication gate for KCP packages: messages rejected by the first-message authentication middleware
    /// are dropped (and the session closed as protocol violation) before reaching the shared business PackageHandler.
    /// </remarks>
    /// <param name="authenticationMiddleware">KCP 会话鉴权中间件 / The KCP session authentication middleware</param>
    /// <param name="session">会话对象 / Session object</param>
    /// <param name="message">接收到的消息 / Received message</param>
    private async ValueTask HandleKcpPackageWithAuthenticationAsync(SessionAuthenticationMiddleware authenticationMiddleware, IAppSession session, IMessage message)
    {
        if (!await authenticationMiddleware.ShouldAllowPackageAsync(session, message))
        {
            return;
        }

        await PackageHandlerAsync(session, message);
    }

    /// <summary>
    /// 配置服务器间 KCP 监听：与 <see cref="ConfigureKcpServer"/> 同构（共用消息漏斗与管线过滤器），
    /// 差异 = 独立端口与开关（<c>IsEnableInnerKcp</c>/<c>InnerKcpPort</c>）；
    /// 在 <see cref="GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication"/> 共享密钥
    /// 已配置时挂会话鉴权中间件作为凭据闸门：以协议版本握手帧为鉴权完成消息，凭据经 HMAC 验签，
    /// 未完成握手前白名单外消息一律按协议违规关闭（该专用面按构造无玩家流量，fail-closed）；
    /// 密钥为空时不挂中间件（信任级对齐 Inner TCP：内网端点）、挂空闲会话清理（清理窗 300s 严格大于客户端陈旧淘汰阈 240s）。
    /// </summary>
    /// <remarks>
    /// Configures the inter-server KCP listener: structurally identical to
    /// <see cref="ConfigureKcpServer"/> (shared message funnel and pipeline filter), differing in the
    /// dedicated port and switch (<c>IsEnableInnerKcp</c>/<c>InnerKcpPort</c>).
    /// When the <see cref="GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication"/>
    /// shared secret is configured, the session authentication middleware is mounted as a credential
    /// gate: the protocol version handshake frame is the authentication-completion message, its
    /// credential is verified via HMAC, and messages outside the whitelist are closed as protocol
    /// violations until the handshake completes (this dedicated face carries no player traffic by
    /// construction — fail-closed). With an empty secret no middleware is mounted (trust level
    /// aligned with the inner TCP listener: internal endpoints), and idle-session clearing is
    /// mounted (the 300s clearing window is strictly greater than the RemoteMessaging client's 240s
    /// stale-eviction threshold). Enabling KCP here does not affect deployments that only configure
    /// tcp keys (the default dial path stays tcp).
    /// </remarks>
    /// <param name="multipleServerHostBuilder">多服务器主机构建器 / Multiple server host builder</param>
    private void ConfigureInnerKcpServer(MultipleServerHostBuilder multipleServerHostBuilder)
    {
        if (!Setting.IsEnableInnerKcp)
        {
            // Localization: StartUp.InnerKcp.ServerDisabled - {0} 服务器间 KCP 服务未启用（{1}:{2}），跳过监听器装配（需 IsEnableInnerKcp 显式开启）。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.InnerKcp.ServerDisabled, ServerType, Setting.InnerHost, Setting.InnerKcpPort));
            return;
        }

        // 与客户端接入面同构的端口守卫（0 视为未设置跳过；KCP 为 UDP）
        if (Setting.InnerKcpPort is > 0 and <= ushort.MaxValue && NetHelper.PortIsAvailable(Setting.InnerKcpPort))
        {
            // Localization: StartUp.InnerKcp.StartingServer - 正在启动 {0} 服务器间 KCP 服务，监听 {1}:{2}。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.InnerKcp.StartingServer, ServerType, Setting.InnerHost, Setting.InnerKcpPort));
            var interNodeAuthenticationEnabled = GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication.IsEnabled;
            SessionAuthenticationMiddleware authenticationMiddleware = null;
            if (interNodeAuthenticationEnabled)
            {
                // 凭据闸门：握手帧为鉴权完成消息，凭据 HMAC 验签；未完成握手前白名单外消息按协议违规关闭
                var authenticationOptions = new SessionAuthenticationOptions();
                authenticationOptions.AllowedMessageIds.Add(GameFrameX.Network.RemoteMessaging.Versioning.ReqProtocolVersionNegotiation.ReservedMessageId);
                authenticationOptions.AuthenticatedByMessageIds.Add(GameFrameX.Network.RemoteMessaging.Versioning.ReqProtocolVersionNegotiation.ReservedMessageId);
                authenticationOptions.CredentialValidator = VerifyInterNodeHandshakeCredentialAsync;
                authenticationMiddleware = new SessionAuthenticationMiddleware(authenticationOptions);
            }

            multipleServerHostBuilder.AddServer<IMessage, MessageObjectPipelineFilter>(builder =>
            {
                builder
                    .UseKcp(o =>
                    {
                        // 与 RemoteMessaging 客户端会话对称：消息模式（内核缺省，单帧 ≤8 段整帧交付）；
                        // 显式窗口 128（内核缺省 32 在多段帧下接收窗口枯竭）；单帧上限守卫见 KcpStream
                        o.NoDelay = true;
                        o.Interval = 10;
                        o.Mtu = 1400;
                        o.SendWindow = 128;
                        o.ReceiveWindow = 128;
                    })
                    // 空闲会话清理兜底：清理窗 300s 必须大于客户端陈旧淘汰阈 240s（KcpConnectionProvider 时序不变量）
                    .UseClearIdleSession()
                    .UseSessionHandler(OnConnectedAsync, OnDisconnectedAsync)
                    .UsePackageHandler(interNodeAuthenticationEnabled
                        ? (session, message) => HandleKcpPackageWithAuthenticationAsync(authenticationMiddleware, session, message)
                        : (session, message) => PackageHandlerAsync(session, message), PackageErrorHandlerAsync)
                    .UseInProcSessionContainer();
                if (interNodeAuthenticationEnabled)
                {
                    // UseMiddleware 返回非泛型 ISuperSocketHostBuilder，须置于泛型链末尾（与客户端 KCP 面同构）
                    builder.UseMiddleware<SessionAuthenticationMiddleware>(_ => authenticationMiddleware);
                }

                builder.ConfigureServices((context, serviceCollection) =>
                {
                    serviceCollection.Configure<ServerOptions>(options =>
                    {
                        options.IdleSessionTimeOut = 300;
                        options.ClearIdleSessionInterval = 60;
                        var listenOptions = new ListenOptions
                        {
                            Ip = "Any",
                            Port = Setting.InnerKcpPort,
                        };
                        options.AddListener(listenOptions);
                    });
                });
            });
            // 实例级闸门置位：本 Role 的 KCP 监听器装配完成（Host.StartAsync 前完成配置，绑定失败会终止启动），
            // 后续 PublishActiveAdvertiseEndpoints 以此为广播前提。
            // The instance-level gate flips on: this role's KCP listener assembly is complete (the
            // configuration finishes before Host.StartAsync; a bind failure aborts startup), and the
            // later advertise-endpoint publish is gated on this reading.
            IsInnerKcpListenerConfigured = true;
            // Localization: StartUp.InnerKcp.StartupComplete - {0} 服务器间 KCP 服务已成功启动，监听 {1}:{2}。
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.InnerKcp.StartupComplete, ServerType, Setting.InnerHost, Setting.InnerKcpPort));
        }
        else
        {
            // Localization: StartUp.InnerKcp.StartupFailed - {0} 服务器间 KCP 服务启动失败（{1}:{2}），端口不可用或越界。
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.StartUp.InnerKcp.StartupFailed, ServerType, Setting.InnerHost, Setting.InnerKcpPort));
            LogPortOccupationDetails("InnerKcp", Setting.InnerKcpPort);
        }
    }

    /// <summary>
    /// 服务器间凭据闸门校验器：解包协议版本握手帧并校验节点间 HMAC 凭据。
    /// </summary>
    /// <remarks>
    /// The inter-server credential gate validator: deserializes the protocol version handshake
    /// frame and verifies the inter-node HMAC credential.
    /// 非握手消息（不应到达——闸门白名单仅放行握手帧与心跳）或验签失败返回 <c>false</c>，
    /// 由 <see cref="SessionAuthenticationMiddleware"/> 按协议违规关闭会话。
    /// </remarks>
    /// <param name="package">收到的消息包 / The received message package</param>
    /// <returns>凭据校验通过返回 <c>true</c> / true when the credential verifies</returns>
    private static ValueTask<bool> VerifyInterNodeHandshakeCredentialAsync(INetworkMessagePackage package)
    {
        if (package.DeserializeMessageObject() is not GameFrameX.Network.RemoteMessaging.Versioning.ReqProtocolVersionNegotiation request)
        {
            return ValueTask.FromResult(false);
        }

        return ValueTask.FromResult(GameFrameX.Network.RemoteMessaging.Versioning.InterNodeAuthentication.Verify(request.Nonce, request.Major, request.Minor, request.AuthCredential));
    }

    /// <summary>
    /// 配置 WebSocket 服务器。
    /// </summary>
    /// <remarks>Configure the WebSocket server. Extracted from <see cref="StartServerCoreAsync"/> to keep cognitive complexity under the Sonar S3776 threshold.</remarks>
    /// <param name="multipleServerHostBuilder">多服务器主机构建器 / Multiple server host builder</param>
    private void ConfigureWebSocketServer(MultipleServerHostBuilder multipleServerHostBuilder)
    {
        if (!Setting.IsEnableWebSocket)
        {
            // Localization: StartUp.WebSocketServer.ServiceNotEnabled - 启动WebSocket服务器失败 类型: {0}, 端口: {1}, 原因: WebSocket服务未启用
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.WebSocketServer.ServiceNotEnabled, ServerType, Setting.WsPort));
            return;
        }

        // 检查WebSocket端口是否可用
        if (Setting.WsPort is > 0 and < ushort.MaxValue && NetHelper.PortIsAvailable(Setting.WsPort))
        {
            // Localization: StartUp.WebSocketServer.StartingServer - 启动WebSocket服务器 类型: {0}, 端口: {1}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.WebSocketServer.StartingServer, ServerType, Setting.WsPort));

            // 配置并启动WebSocket服务器
            multipleServerHostBuilder.AddWebSocketServer(builder =>
            {
                builder
                    .UseWebSocketMessageHandler(WebSocketMessageHandlerAsync)
                    .UseSessionHandler(OnConnectedAsync, OnDisconnectedAsync)
                    .ConfigureServices((context, serviceCollection) =>
                    {
                        serviceCollection.Configure<ServerOptions>(options =>
                        {
                            var listenOptions = new ListenOptions
                            {
                                Ip = "Any",
                                Port = Setting.WsPort,
                            };
                            options.AddListener(listenOptions);
                        });
                    });
            });
            // Localization: StartUp.WebSocketServer.StartupComplete - 启动WebSocket服务器完成 类型: {0}, 端口: {1}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.StartUp.WebSocketServer.StartupComplete, ServerType, Setting.WsPort));
        }
        else
        {
            // Localization: StartUp.WebSocketServer.StartupFailed - 启动WebSocket服务器失败 类型: {0}, 端口: {1}, 原因: 端口无效或被占用
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.StartUp.WebSocketServer.StartupFailed, ServerType, Setting.WsPort));
            LogPortOccupationDetails("WebSocket", Setting.WsPort);
        }
    }

    /// <summary>
    /// 端口不可用时记录占用该端口的进程详情。
    /// </summary>
    /// <remarks>Log details of processes occupying a port when it is unavailable. Consolidates the occupation-detail logging previously inlined for both TCP and WebSocket startup.</remarks>
    /// <param name="serverName">服务器名称，用于日志展示 / Server name for log display</param>
    /// <param name="port">待检查的端口 / Port to check</param>
    private void LogPortOccupationDetails(string serverName, int port)
    {
        if (port <= 0)
        {
            return;
        }

        var occupiedProcesses = NetHelper.GetPortOccupyingProcesses(port);
        if (occupiedProcesses.Count > 0)
        {
            // Localization: StartUp.Server.PortOccupiedDetails - {0}端口[{1}]占用详情: {2}
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.StartUp.Server.PortOccupiedDetails, serverName, port, string.Join(" | ", occupiedProcesses)));
        }
    }

    /// <summary>
    /// 配置服务器主机的日志记录。
    /// </summary>
    /// <remarks>Configure host logging for the multiple server host builder. Extracted from <see cref="StartServerCoreAsync"/> to keep cognitive complexity under the Sonar S3776 threshold.</remarks>
    /// <param name="multipleServerHostBuilder">多服务器主机构建器 / Multiple server host builder</param>
    /// <param name="minimumLevelLogLevel">日志记录的最小级别，用于控制日志输出 / Minimum level for logging to control log output</param>
    private void ConfigureHostLogging(MultipleServerHostBuilder multipleServerHostBuilder, LogLevel minimumLevelLogLevel)
    {
        // 配置日志
        multipleServerHostBuilder.ConfigureLogging(logging =>
        {
            logging.ClearProviders();
            logging.AddSerilog(Log.Logger, true);
            logging.SetMinimumLevel(minimumLevelLogLevel);
            logging.ConfigureOpenTelemetryLogger(Setting.IsOpenTelemetry);
        });
    }

    #endregion

    #region WebSocket

    /// <summary>
    /// WebSocket 消息处理方法。
    /// </summary>
    /// <remarks>
    /// WebSocket message processing method.
    /// Handles WebSocket messages; only processes binary message types.
    /// </remarks>
    /// <param name="session">WebSocket 会话对象 / WebSocket session object</param>
    /// <param name="messagePackage">接收到的消息包 / Received message package</param>
    /// <returns>表示异步操作的任务 / A task representing the asynchronous operation</returns>
    private async ValueTask WebSocketMessageHandlerAsync(WebSocketSession session, WebSocketPackage messagePackage)
    {
        // 只处理二进制消息
        if (messagePackage.OpCode != OpCode.Binary)
        {
            await session.CloseAsync(CloseReason.ProtocolError);
            return;
        }

        var readOnlySequence = messagePackage.Data;
        var message = MessageHelper.DecoderHandler.Handler(ref readOnlySequence);
        await PackageHandlerAsync(session, message);
    }

    #endregion
}