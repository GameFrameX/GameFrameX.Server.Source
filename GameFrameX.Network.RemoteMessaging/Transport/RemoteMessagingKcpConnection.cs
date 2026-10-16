// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;

namespace GameFrameX.Network.RemoteMessaging.Transport;

/// <summary>
/// 服务器间 KCP 客户端会话连接：<see cref="KcpPipeConnection"/> 的本程序集投影，
/// 将基类 protected 的 <see cref="KcpStream"/> 读面（Input 管道读端）暴露给 Stream 适配层。
/// </summary>
/// <remarks>
/// Inter-server KCP client-session connection: a thin projection of <see cref="KcpPipeConnection"/>
/// in this assembly, exposing the base's protected input pipe reader (the read face consumed by the
/// Stream adaptation layer) to <see cref="KcpStream"/>. The connection itself is created, fed
/// (UDP receive loop), and driven (KCP update loop) by <see cref="KcpClientSession"/>.
/// </remarks>
internal sealed class RemoteMessagingKcpConnection : KcpPipeConnection
{
    /// <summary>
    /// 初始化 KCP 客户端会话连接。
    /// </summary>
    /// <remarks>
    /// Initializes the KCP client-session connection.
    /// </remarks>
    /// <param name="socket">本会话独占的 UDP Socket / The UDP socket owned by this session</param>
    /// <param name="remoteEndPoint">远端服务器间 KCP 监听端点 / The remote inter-server KCP listener endpoint</param>
    /// <param name="sessionIdentifier">会话标识（客户端侧本地簿记）/ The session identifier (client-side local bookkeeping)</param>
    /// <param name="conv">KCP 会话标识（非 0，客户端侧分配）/ The KCP conversation id (non-zero, allocated client-side)</param>
    /// <param name="options">连接选项 / The connection options</param>
    /// <param name="kcpOptions">KCP 参数 / The KCP options</param>
    /// <param name="ownsSocket">关闭时是否释放 Socket / Whether the socket is disposed on close</param>
    internal RemoteMessagingKcpConnection(Socket socket, IPEndPoint remoteEndPoint, string sessionIdentifier, uint conv, ConnectionOptions options, KcpConnectionOptions kcpOptions, bool ownsSocket)
        : base(socket, remoteEndPoint, sessionIdentifier, conv, options, kcpOptions, ownsSocket)
    {
    }

    /// <summary>
    /// 获取 KCP 应用层读面：Input 管道读端（KCP 重组后的有序字节流）。
    /// </summary>
    /// <remarks>
    /// Gets the KCP application-level read face: the input pipe reader (the ordered byte stream
    /// reassembled by KCP). Consumed by <see cref="KcpStream"/> to serve <see cref="Stream"/> reads.
    /// </remarks>
    internal PipeReader Reader
    {
        get { return InputReader; }
    }
}
