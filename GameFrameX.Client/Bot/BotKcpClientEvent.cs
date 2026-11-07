// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整 license 文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
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

using System.Buffers;
using System.Net;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Utility;
using GameFrameX.Network;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.Message;
using GameFrameX.Network.Messages;
using GameFrameX.Proto.Proto;
using GameFrameX.ProtoBuf.Net;
using GameFrameX.SuperSocket.Client;
using GameFrameX.SuperSocket.Connection;
using GameFrameX.SuperSocket.Kcp;

namespace GameFrameX.Client.Bot;

/// <summary>
/// 机器人 KCP 客户端事件结构体,包含各种回调事件。
/// </summary>
/// <remarks>
/// 与 <see cref="BotTcpClientEvent"/> 同构，便于 <see cref="BotClient"/> 在两种传输间共享事件接线，
/// 不为 KCP 引入独立事件签名。命名沿用 BotTcpClientEvent 以保持与现有代码一致。
/// </remarks>
public struct BotKcpClientEvent
{
    /// <summary>
    /// 连接成功时的回调
    /// </summary>
    public Action OnConnectedCallback { get; set; }

    /// <summary>
    /// 连接关闭时的回调
    /// </summary>
    public Action OnClosedCallback { get; set; }

    /// <summary>
    /// 发生错误时的回调
    /// </summary>
    public Action<Exception> OnErrorCallback { get; set; }

    /// <summary>
    /// 接收到消息时的回调
    /// </summary>
    public Action<MessageObject> OnReceiveMsgCallback { get; set; }
}
