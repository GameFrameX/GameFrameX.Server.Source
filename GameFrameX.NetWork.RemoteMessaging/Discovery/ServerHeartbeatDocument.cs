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


using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// server_heartbeat 集合文档模型（C143d D11/D15，D18 全名约定）。
/// </summary>
/// <remarks>
/// The document model of the <c>server_heartbeat</c> collection in the control
/// database (C143d D11/D15; the collection name follows the D18 no-abbreviation rule).
/// One document per live instance, keyed by instance id; the TTL index on
/// <see cref="LastHeartbeat"/> (15 s) is the last-resort cleanup for instances that
/// died without writing Stopped, while the watcher's three-period staleness check
/// remains the primary liveness signal.
/// </remarks>
internal sealed class ServerHeartbeatDocument
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

    /// <summary>
    /// 获取或设置实例唯一标识（主键）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the instance id (the primary key).
    /// </remarks>
    [BsonId]
    public string InstanceId { get; set; }

    /// <summary>
    /// 获取或设置承载的 Role 名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the hosted role name.
    /// </remarks>
    [BsonElement("role")]
    public string Role { get; set; }

    /// <summary>
    /// 获取或设置对外可达端点（未解析的 scheme://host:port）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the advertise endpoint (unparsed scheme://host:port, D15).
    /// </remarks>
    [BsonElement("advertiseEndpoint")]
    public string AdvertiseEndpoint { get; set; }

    /// <summary>
    /// 获取或设置实例状态名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the status name (the enum name, stored as a string for readability).
    /// </remarks>
    [BsonElement("status")]
    public string Status { get; set; }

    /// <summary>
    /// 获取或设置负载值（0–100）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the load value (0-100).
    /// </remarks>
    [BsonElement("load")]
    public int Load { get; set; }

    /// <summary>
    /// 获取或设置地址形态名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the address kind name.
    /// </remarks>
    [BsonElement("addressKind")]
    public string AddressKind { get; set; }

    /// <summary>
    /// 获取或设置代数（重启纪元）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the incarnation.
    /// </remarks>
    [BsonElement("incarnation")]
    public long Incarnation { get; set; }

    /// <summary>
    /// 获取或设置最后心跳时间（UTC，TTL 索引字段）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the last heartbeat time (UTC; the TTL index field).
    /// </remarks>
    [BsonElement("lastHeartbeat")]
    [BsonRepresentation(BsonType.DateTime)]
    public DateTime LastHeartbeat { get; set; }
}
