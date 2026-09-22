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
/// 实例上下线变化类别（C143d D15 事件）。
/// </summary>
/// <remarks>
/// The kind of instance lifecycle change broadcast by the watcher (C143d D15 events).
/// Values start at 1 on purpose: an uninitialized field must never read as a valid kind.
/// </remarks>
public enum RoleInstanceChangeKind
{
    /// <summary>
    /// 上线：新实例出现或重启实例（新 incarnation）上线。
    /// </summary>
    /// <remarks>
    /// Online: a new instance appeared, or a restarted instance (new incarnation) came online.
    /// </remarks>
    Online = 1,

    /// <summary>
    /// 排水中：实例进入 Draining。
    /// </summary>
    /// <remarks>
    /// Draining: the instance entered the Draining state.
    /// </remarks>
    Draining = 2,

    /// <summary>
    /// 离线：心跳超过三周期阈值，从路由表摘除。
    /// </summary>
    /// <remarks>
    /// Offline: the heartbeat exceeded the three-period staleness threshold and was evicted from the table.
    /// </remarks>
    Offline = 3,

    /// <summary>
    /// 驱逐：心跳文档被 TTL 清除，实例身份彻底消失。
    /// </summary>
    /// <remarks>
    /// Evicted: the heartbeat document was TTL-removed; the instance identity is gone for good.
    /// </remarks>
    Evicted = 4,

    /// <summary>
    /// 恢复：同一实例（incarnation 不变）心跳恢复新鲜。
    /// </summary>
    /// <remarks>
    /// Recovered: the same instance (unchanged incarnation) went stale and then became fresh again.
    /// </remarks>
    Recovered = 5,
}
