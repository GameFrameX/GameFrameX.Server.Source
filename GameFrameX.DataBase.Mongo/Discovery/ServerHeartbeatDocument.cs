// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
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
//   本组织与贡献者概不承担。
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


using GameFrameX.Discovery;

namespace GameFrameX.DataBase.Mongo.Discovery;

/// <summary>
/// server_heartbeat 集合文档实体（全名约定）；属性形态继承自 <see cref="ServerHeartbeatEntity"/>。
/// </summary>
/// <remarks>
/// The document entity of the <c>server_heartbeat</c> collection in the control
/// database (the collection name follows the no-abbreviation rule).
/// Properties are declared once on the shared <see cref="ServerHeartbeatEntity"/> base;
/// the BSON wire mapping (camelCase elements, <c>_id</c> key, DateTime representation)
/// lives in <see cref="MongoDiscoverySerialization"/> and is byte-identical to the
/// former attribute form. One document per live instance, keyed by instance id; the
/// client-side cleanup loop removes documents whose LastHeartbeat exceeds the 15 s
/// window — the last-resort cleanup for instances that died without writing Stopped —
/// while the watcher's three-period staleness check remains the primary liveness signal.
/// </remarks>
internal sealed class ServerHeartbeatDocument : ServerHeartbeatEntity
{
    /// <summary>
    /// 初始化心跳文档。
    /// </summary>
    /// <remarks>
    /// Initializes the document.
    /// </remarks>
    /// <param name="instanceId">实例唯一标识（文档主键）/ The unique instance id (the document key)</param>
    /// <param name="role">承载的 Role 名 / The hosted role name</param>
    /// <param name="advertiseEndpoint">对外可达端点（未解析）/ The reachable endpoint (unparsed)</param>
    /// <param name="status">实例状态名 / The instance status name</param>
    /// <param name="load">负载值（0–100）/ The load value (0-100)</param>
    /// <param name="addressKind">地址形态名 / The address kind name</param>
    /// <param name="incarnation">代数（重启纪元）/ The incarnation</param>
    /// <param name="lastHeartbeatUtc">本次心跳时间（UTC）/ This heartbeat time (UTC)</param>
    public ServerHeartbeatDocument(string instanceId, string role, string advertiseEndpoint, string status, int load, string addressKind, long incarnation, DateTime lastHeartbeatUtc)
    {
        InstanceId = instanceId;
        Role = role;
        AdvertiseEndpoint = advertiseEndpoint;
        Status = status;
        Load = load;
        AddressKind = addressKind;
        Incarnation = incarnation;
        LastHeartbeat = lastHeartbeatUtc;
    }
}
