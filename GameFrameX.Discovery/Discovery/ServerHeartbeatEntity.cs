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
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.Discovery;

/// <summary>
/// server_heartbeat 存储模型的共享属性基类（Mongo 文档与 PostgreSql 行实体的公共属性形态）。
/// </summary>
/// <remarks>
/// The shared property base of the <c>server_heartbeat</c> storage model: the common
/// shape implemented by the Mongo document entity and the PostgreSql row entity.
/// Properties are declared once here; each provider keeps its own wire mapping
/// (Mongo: camelCase elements with <c>_id</c>; PostgreSql: snake_case columns), so
/// the stored schema of either provider stays byte-identical to its pre-refactor
/// form. Enum-shaped values (<see cref="Status"/>, <see cref="AddressKind"/>) stay
/// raw strings; the watcher's defensive parsing (unknown names skipped) applies to
/// both providers unchanged.
/// </remarks>
public abstract class ServerHeartbeatEntity
{
    /// <summary>
    /// 获取或设置实例唯一标识（主键 / 文档键）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the unique instance id (the primary key / document key).
    /// </remarks>
    public string InstanceId { get; set; }

    /// <summary>
    /// 获取或设置承载的 Role 名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the hosted role name.
    /// </remarks>
    public string Role { get; set; }

    /// <summary>
    /// 获取或设置对外可达端点（未解析的 scheme://host:port）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the reachable advertise endpoint (unparsed scheme://host:port).
    /// </remarks>
    public string AdvertiseEndpoint { get; set; }

    /// <summary>
    /// 获取或设置实例状态名（字符串形态，读取侧防御解析）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the status name (kept as a raw string, parsed defensively on read).
    /// </remarks>
    public string Status { get; set; }

    /// <summary>
    /// 获取或设置负载值（0–100）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the load value (0-100).
    /// </remarks>
    public int Load { get; set; }

    /// <summary>
    /// 获取或设置地址形态名（字符串形态，读取侧防御解析）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the address-kind name (kept as a raw string, parsed defensively on read).
    /// </remarks>
    public string AddressKind { get; set; }

    /// <summary>
    /// 获取或设置代数（重启纪元）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the incarnation (the restart epoch).
    /// </remarks>
    public long Incarnation { get; set; }

    /// <summary>
    /// 获取或设置最后心跳时间（UTC）。
    /// </summary>
    /// <remarks>
    /// Gets or sets the last heartbeat time (UTC; the client-side cleanup
    /// window's cutoff field).
    /// </remarks>
    public DateTime LastHeartbeat { get; set; }
}
