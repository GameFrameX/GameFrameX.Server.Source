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
using GameFrameX.Apps.Common.Session;
using GameFrameX.Apps.Common.Event;
using GameFrameX.Apps.Common.EventData;
using GameFrameX.Core.Events;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Abstractions;
using GameFrameX.Proto.Proto;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Server.Abstractions.Session;

namespace GameFrameX.Hotfix.StartUp;

/// <summary>
/// 业务服务器.最后启动。
/// </summary>
internal partial class AppStartUpHotfixGame
{
    /// <summary>
    /// 创建 Game 服务器的 KCP 会话首消息鉴权配置：放行登录流程消息，以角色登录为鉴权完成信号。
    /// </summary>
    /// <remarks>
    /// Creates the KCP session first-message authentication options for the game server.
    /// 白名单为现有 TCP 登录流全链路（账号登录 → 角色列表/角色创建 → 角色登录）；
    /// <see cref="ReqPlayerLogin"/> 是鉴权终态——业务侧 OnPlayerLogin 处理该消息并绑定 ActorId，此后会话消息全放行。
    /// </remarks>
    /// <returns>KCP 会话鉴权配置 / The KCP session authentication options</returns>
    protected override SessionAuthenticationOptions CreateKcpSessionAuthenticationOptions()
    {
        var options = base.CreateKcpSessionAuthenticationOptions();
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqLogin)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerList)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerCreate)));
        options.AllowedMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerLogin)));
        options.AuthenticatedByMessageIds.Add(GetLoginMessageId(typeof(ReqPlayerLogin)));
        return options;
    }

    /// <summary>
    /// 从消息类型的 <see cref="MessageTypeHandlerAttribute"/> 读取消息码（编译期固定值，不依赖运行时协议注册表初始化）。
    /// </summary>
    /// <param name="messageType">登录流程消息类型 / The login-flow message type</param>
    /// <returns>消息码 / The message id</returns>
    private static int GetLoginMessageId(Type messageType)
    {
        return messageType.GetCustomAttribute<MessageTypeHandlerAttribute>()?.MessageId
               ?? throw new InvalidOperationException($"Login message type {messageType.FullName} is missing {nameof(MessageTypeHandlerAttribute)}.");
    }

    public override async Task StartAsync()
    {
        // 启动网络服务
        // 设置压缩和解压缩
        await StartServerAsync<DefaultMessageDecoderHandler, DefaultMessageEncoderHandler>(new DefaultMessageCompressHandler(), new DefaultMessageDecompressHandler(), HotfixManager.GetListHttpHandler(), HotfixManager.GetHttpHandler);
        // 启动Http服务
        // await HttpServer.Start(Setting.HttpPort, Setting.HttpsPort, HotfixManager.GetListHttpHandler(), HotfixManager.GetHttpHandler, null, Setting.HttpUrl);
    }

    public async Task RunServer(bool reload = false)
    {
        // 不管是不是重启服务器，都要加载配置
        await ConfigComponent.Instance.LoadConfig();
        if (reload)
        {
            ActorManager.ClearAgent();
            return;
        }

        await StartAsync();
    }


    protected override ValueTask OnDisconnected(IAppSession appSession, CloseEventArgs disconnectEventArgs)
    {
        LogHelper.Info("Client disconnected. SessionID: {sessionId}, Reason: {reason}", appSession.SessionId, disconnectEventArgs.Reason);
        SessionManager.Remove(appSession.SessionId);
        return ValueTask.CompletedTask;
    }

    protected override async ValueTask OnConnected(IAppSession appSession)
    {
        LogHelper.Info("Client connected. SessionID: {sessionId}, RemoteEndPoint: {remoteEndPoint}", appSession.SessionId, appSession.RemoteEndPoint);
        var netChannel = new DefaultNetWorkChannel(appSession, Setting);
        var count = SessionManager.Count();
        if (count > Setting.MaxClientCount)
        {
            // 达到最大在线人数限制
            await netChannel.WriteAsync(new NotifyServerFullyLoaded(), (int)OperationStatusCode.ServerFullyLoaded);
            netChannel.Close();
            return;
        }

        var session = new Session(appSession.SessionId, netChannel);
        SessionManager.Add(session);
    }

    /// <summary>
    /// 处理收到的消息结果
    /// </summary>
    /// <param name="session"></param>
    /// <param name="message"></param>
    protected override async ValueTask PackageHandler(IAppSession session, IMessage message)
    {
        if (message is NetworkMessagePackage messagePackage)
        {
            await HandleNetworkMessagePackageAsync(session, messagePackage);
        }
    }

    /// <summary>
    /// 处理网络消息包：解析会话通道，按消息类型分发（心跳回复 / 业务 handler 调用）。
    /// </summary>
    /// <param name="session">客户端会话。</param>
    /// <param name="messagePackage">网络消息包。</param>
    private async ValueTask HandleNetworkMessagePackageAsync(IAppSession session, NetworkMessagePackage messagePackage)
    {
        var netWorkChannel = SessionManager.GetChannel(session.SessionId);

        if (netWorkChannel.IsNull())
        {
            return;
        }

        var actorId = netWorkChannel.GetData<long>(GlobalConst.ActorIdKey);
        if (messagePackage.Header.OperationType == (byte)MessageOperationType.HeartBeat)
        {
            if (Setting.IsDebug && Setting.IsDebugReceive && Setting.IsDebugReceiveHeartBeat)
            {
                LogHelper.Debug<string>("Data Package Receive HeartBeat: {message}", messagePackage.ToFormatMessageString(actorId));
            }

            // 心跳消息回复
            await ReplyHeartBeatAsync(netWorkChannel, (MessageObject)messagePackage.DeserializeMessageObject());
            return;
        }

        if (Setting.IsDebug && Setting.IsDebugReceive)
        {
            LogHelper.Debug<string>("Data Package Receive: {message}", messagePackage.ToFormatMessageString(actorId));
        }

        var handler = HotfixManager.GetTcpHandler(messagePackage.Header.MessageId);
        if (handler == null)
        {
            LogHelper.Error("Data Package Receive: Can not find handler for message id: {messageId}, message type: {messageType}", messagePackage.Header.MessageId, messagePackage.MessageType);
            return;
        }

        // 执行消息分发处理
        try
        {
            await InvokeMessageHandler(handler, messagePackage.DeserializeMessageObject(), netWorkChannel);
        }
        catch (Exception exception)
        {
            LogHelper.Fatal("Data Package Receive: Error when invoke message handler for message id: {messageId}, message type: {messageType} , exception: {exception}", messagePackage.Header.MessageId, messagePackage.MessageType, exception);
        }
    }

    public override async Task StopAsync(string message = "")
    {
        EventDispatcher.Dispatch(0, (int)EventId.ServiceOffline, new ServiceOfflineEventArgs(Setting.ServerType, Setting.ServerInstanceId, "Stopped", DateTime.UtcNow));
        await base.StopAsync(message);
        // 断开所有连接
        await SessionManager.RemoveAll();
        // 取消所有未执行定时器
        await QuartzTimer.Stop();
        // 保证actor之前的任务都执行完毕
        await ActorManager.AllFinish();
        // 存储所有数据
        await GlobalTimer.Stop();
        // 删除所有actor
        await ActorManager.RemoveAll();
    }
}
