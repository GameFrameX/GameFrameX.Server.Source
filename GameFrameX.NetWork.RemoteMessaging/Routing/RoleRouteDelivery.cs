// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:   https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


namespace GameFrameX.NetWork.RemoteMessaging.Routing;

/// <summary>
/// 跨 Role 路由投递结果（C143c D3）。
/// </summary>
/// <remarks>
/// Delivery result of cross-role routing (C143c D3).
/// Tells the caller which branch of the three-step routing decision actually delivered
/// the message, so topology equivalence tests can assert that the All-in-One topology
/// hits the local in-process path 100% of the time.
/// Values start at 1 on purpose: an uninitialized field must never read as a valid delivery.
/// </remarks>
public enum RoleRouteDelivery
{
    /// <summary>
    /// D3 case 1：目标 Role 属于本进程角色集，经本地投递缝送达。
    /// </summary>
    /// <remarks>
    /// D3 case 1: the target role belongs to this process and was delivered through the local dispatcher.
    /// </remarks>
    LocalActor = 1,

    /// <summary>
    /// D3 case 2/3：目标 Role 不属于本进程角色集，经远程转发缝投出。
    /// </summary>
    /// <remarks>
    /// D3 case 2/3: the target role is hosted by another process and was handed to the remote forwarding seam.
    /// </remarks>
    RemoteForwarded = 2,
}
