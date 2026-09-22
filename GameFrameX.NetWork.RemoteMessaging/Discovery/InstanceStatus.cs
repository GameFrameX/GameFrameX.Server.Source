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


namespace GameFrameX.NetWork.RemoteMessaging.Discovery;

/// <summary>
/// 服务实例状态（C143d D15：heartbeat 文档 status 字段）。
/// </summary>
/// <remarks>
/// The lifecycle status of a role instance (C143d D15: the heartbeat document status field).
/// Scale-down follows the graceful Draining-to-Stopped semantics of AC-4: a Draining
/// instance stays routable for in-flight deliveries (D3 case 2) but is excluded from
/// new any-instance selections (D3 case 3); Stopped is written on graceful exit so the
/// watcher does not have to wait for the TTL to expire.
/// Values start at 1 on purpose: an uninitialized field must never read as a valid status.
/// </remarks>
public enum InstanceStatus
{
    /// <summary>
    /// 启动中：进程已注册但尚未宣告可服务。
    /// </summary>
    /// <remarks>
    /// Booting: registered but not yet announcing service readiness.
    /// </remarks>
    Booting = 1,

    /// <summary>
    /// 活跃：正常服务中，接收新流量。
    /// </summary>
    /// <remarks>
    /// Active: serving normally, accepts new traffic.
    /// </remarks>
    Active = 2,

    /// <summary>
    /// 排水中：优雅缩容中，不接新流量、保留在途投递。
    /// </summary>
    /// <remarks>
    /// Draining: graceful scale-down in progress; no new traffic, in-flight deliveries kept.
    /// </remarks>
    Draining = 3,

    /// <summary>
    /// 已停止：优雅退出时写入，不参与路由。
    /// </summary>
    /// <remarks>
    /// Stopped: written on graceful exit; excluded from routing.
    /// </remarks>
    Stopped = 4,

    /// <summary>
    /// 已摘除：心跳文档被 TTL 清除或实例被移出拓扑。
    /// </summary>
    /// <remarks>
    /// Removed: the heartbeat document was TTL-evicted or the instance left the topology.
    /// </remarks>
    Removed = 5,
}
