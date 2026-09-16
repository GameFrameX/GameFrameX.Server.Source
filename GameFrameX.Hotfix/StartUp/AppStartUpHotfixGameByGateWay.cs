// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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


/*using System.Net;
using System.Timers;
using GameFrameX.Launcher;
using GameFrameX.NetWork;
using GameFrameX.NetWork.Messages;
using GameFrameX.Proto.BuiltIn;
using GameFrameX.SuperSocket.ClientEngine;
using GameFrameX.SuperSocket.Server.Abstractions.Session;
using Timer = System.Timers.Timer;

namespace GameFrameX.Hotfix.StartUp;

internal partial class AppStartUpHotfixGame
{
    AsyncTcpSession _gatewayClient;

    private Timer _gateWayReconnectionTimer;
    private Timer _gateWayHeartBeatTimer;
    private ReqHeartBeat _reqGatewayActorHeartBeat;

    private void SendToGatewayMessage(IMessage message)
    {
        if (!_gatewayClient.IsConnected)
        {
            return;
        }

        var span = messageEncoderHandler.Handler(message);
        if (Setting.IsDebug && Setting.IsDebugSend)
        {
            LogHelper.Debug(message.ToSendMessageString(Setting.ServerType, ServerType.Gateway));
        }

        var result = _gatewayClient.TrySend(span);
        if (result)
        {
            _gateWayHeartBeatTimer.Reset();
        }
    }

    protected override void ConnectServerHandler()
    {
        ConnectToGateWay();
    }

    protected override void DisconnectServerHandler()
    {
        DisconnectToGateWay();
    }

    private void StartGatewayClient()
    {
        _gateWayReconnectionTimer = new Timer
        {
            Interval = 5000
        };
        _gateWayReconnectionTimer.Elapsed += GateWayReconnectionTimerOnElapsed;
        _gateWayReconnectionTimer.Start();
        _gateWayHeartBeatTimer = new Timer
        {
            Interval = 5000
        };
        _gateWayHeartBeatTimer.Elapsed += GateWayHeartBeatTimerOnElapsed;
        _gateWayHeartBeatTimer.Start();
        _reqGatewayActorHeartBeat = new ReqHeartBeat();
        _gatewayClient = new AsyncTcpSession();
        _gatewayClient.Closed += GateWayClientOnClosed;
        _gatewayClient.DataReceived += GateWayClientOnDataReceived;
        _gatewayClient.Connected += GateWayClientOnConnected;
        _gatewayClient.Error += GateWayClientOnError;
    }

    private void GateWayHeartBeatTimerOnElapsed(object sender, ElapsedEventArgs e)
    {
        _reqGatewayActorHeartBeat.Timestamp = TimeHelper.UnixTimeSeconds();
        _reqGatewayActorHeartBeat.UpdateUniqueId();
        SendToGatewayMessage(_reqGatewayActorHeartBeat);
    }

    private void GateWayReconnectionTimerOnElapsed(object sender, ElapsedEventArgs e)
    {
        ConnectToGateWay();
    }

    private void GateWayClientOnError(object sender, SuperSocket.ClientEngine.ErrorEventArgs errorEventArgs)
    {
        LogHelper.Info("和网关服务器链接链接发生错误!" + errorEventArgs);
        GateWayClientOnClosed(sender, errorEventArgs);
    }

    private void GateWayClientOnConnected(object sender, EventArgs e)
    {
        // 和网关服务器链接成功，关闭重连
        _gateWayReconnectionTimer.Stop();
        _gateWayHeartBeatTimer.Start();
        var appSession = sender as IGameAppSession;
        var netChannel = new DefaultNetWorkChannel(appSession, messageEncoderHandler);
        GameClientSessionManager.SetSession(appSession.SessionID, netChannel); //移除
        LogHelper.Info("和网关服务器链接链接成功!");
        ReqRegisterGameServer reqRegisterGameServer = new ReqRegisterGameServer
        {
            ServerType = Setting.ServerType,
            ServerID = Setting.ServerId,
            MinModuleMessageID = Setting.MinModuleId,
            MaxModuleMessageID = Setting.MaxModuleId,
            ServerName = Setting.ServerName
        };
        SendToGatewayMessage(reqRegisterGameServer);
    }

    private async void GateWayClientOnDataReceived(object sender, DataEventArgs dataEventArgs)
    {
        IGameAppSession appSession = (IGameAppSession)sender;
        var messageData = dataEventArgs.Data.ReadBytes(dataEventArgs.Offset, dataEventArgs.Length);
        var message = messageDecoderHandler.Handler(messageData);
        if (message is MessageObject messageObject)
        {
            if (Setting.IsDebug && Setting.IsDebugReceive)
            {
                LogHelper.Info($"收到网关服务器消息：{messageObject.ToReceiveMessageString(ServerType.Gateway, ServerType)}");
            }

            var handler = HotfixManager.GetTcpHandler(message.MessageId);
            if (handler == null)
            {
                LogHelper.Error($"找不到[{message.MessageId}][{messageObject.GetType()}]对应的handler");
                return;
            }

            handler.Message = messageObject;
            handler.NetWorkChannel = GameClientSessionManager.GetSession(appSession.SessionID);
            await handler.Init();
            await handler.InnerAction();
        }
    }

    private void GateWayClientOnClosed(object sender, EventArgs eventArgs)
    {
        LogHelper.Info("和网关服务器链接链接断开!开启重连");
        // 和网关服务器链接断开，开启重连
        _gateWayReconnectionTimer.Start();
        _gateWayHeartBeatTimer.Stop();
    }

    private void ConnectToGateWay()
    {
        if (ConnectTargetServer == null)
        {
            return;
        }

        var endPoint = new IPEndPoint(IPAddress.Parse((string)ConnectTargetServer.TargetIP), ConnectTargetServer.TargetPort);
        _gatewayClient.Connect(endPoint);
    }

    private void DisconnectToGateWay()
    {
        _gatewayClient?.Close();
    }
}*/