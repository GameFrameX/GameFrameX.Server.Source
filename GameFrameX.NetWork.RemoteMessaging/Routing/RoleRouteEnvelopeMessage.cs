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


using GameFrameX.NetWork.Abstractions;
using GameFrameX.ProtoBuf.Net;
using ProtoBuf;

namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨进程路由信封传输协议（C143d D3 case 2/3 wire 格式）。
/// </summary>
/// <remarks>
/// The wire representation of a cross-process routing envelope (C143d D3 case 2/3).
/// <see cref="TcpEnvelopeForwarder"/> serializes this message through the standard
/// codec frame, so the bytes on the wire are indistinguishable from any other
/// RemoteMessaging packet. The receiving side (envelope unpacking back into local
/// delivery) is delivered with C143e: <see cref="LocalEnvelopeDispatcher"/> looks up
/// <see cref="InnerMessageId"/> in <c>MessageProtoHelper</c> and re-delivers via
/// <see cref="IPlayerLocalSender"/>.
/// </remarks>
[ProtoContract]
[MessageTypeHandler(ReservedMessageId)]
public sealed class RoleRouteEnvelopeMessage : MessageObject
{
    /// <summary>
    /// 预留消息 Id（负数内部段 -130 的子号 10；仅写入帧头，接收端 change 注册后再启用）。
    /// </summary>
    /// <remarks>
    /// The reserved message id (inner segment -130, sub id 10). Written into frame
    /// headers only; it becomes meaningful once the receiving change registers it.
    /// </remarks>
    public const int ReservedMessageId = unchecked((int)(((-130) << 16) + 10));

    /// <summary>
    /// 获取或设置目标 Role 名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the target role name.
    /// </remarks>
    [ProtoMember(1)]
    public string TargetRole { get; set; }

    /// <summary>
    /// 获取或设置本地投递目标 ActorId（跨进程跳保持透传）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the local delivery target actor id (passed through the remote hop).
    /// </remarks>
    [ProtoMember(2)]
    public long TargetActorId { get; set; }

    /// <summary>
    /// 获取或设置目标实例 Id（D3 case 2 语义；case 3 时为空）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the target instance id (D3 case 2; empty for case 3).
    /// </remarks>
    [ProtoMember(3)]
    public string TargetInstanceId { get; set; }

    /// <summary>
    /// 获取或设置内嵌消息的消息 Id（接收端据此还原消息类型）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the embedded routed message's message id (the receiving side
    /// restores the concrete type from it).
    /// </remarks>
    [ProtoMember(4)]
    public int InnerMessageId { get; set; }

    /// <summary>
    /// 获取或设置内嵌消息的序列化字节。
    /// </summary>
    /// <remarks>
    /// Gets or sets the embedded routed message's serialized bytes. The inner
    /// message travels as id + bytes instead of a polymorphic MessageObject member
    /// on purpose: the vendored protobuf runtime rejects unregistered sub-types on
    /// a base-typed member (ThrowUnexpectedSubtype), and business message types are
    /// only registered per-assembly by the receiving change — the id + bytes form
    /// keeps this contract independent of any registration.
    /// </remarks>
    [ProtoMember(5)]
    public byte[] InnerMessageBytes { get; set; }

    /// <summary>
    /// 清空信封的全部序列化字段为默认值。
    /// </summary>
    /// <remarks>
    /// Resets every serialized envelope field to its default:
    /// <see cref="TargetRole"/> and <see cref="TargetInstanceId"/> to null,
    /// <see cref="TargetActorId"/> and <see cref="InnerMessageId"/> to zero, and
    /// <see cref="InnerMessageBytes"/> to null.
    /// </remarks>
    public override void Clear()
    {
        TargetRole = null;
        TargetActorId = 0;
        TargetInstanceId = null;
        InnerMessageId = 0;
        InnerMessageBytes = null;
    }
}
