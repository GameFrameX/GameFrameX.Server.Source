// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or to infringe upon the legal rights, interests of others as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Collections.Concurrent;
using GameFrameX.Discovery;


namespace GameFrameX.Tests.RemoteMessaging;
/// <summary>
/// 路由表测试辅助：从 (role, instanceId, status, tcpEndpoint, additionalEndpoints) 构造
/// <see cref="InstanceDescriptor"/> 与 <see cref="RoleRouteTable"/>，并提供固定/可变路由表提供者。
/// </summary>
/// <remarks>
/// Route-table test helper: builds <see cref="InstanceDescriptor"/> and <see cref="RoleRouteTable"/>
/// from (role, instanceId, status, tcpEndpoint, additionalEndpoints) tuples and exposes fixed and
/// mutable <see cref="IRoleRouteTableProvider"/> implementations for builder / resolver wiring.
/// </remarks>
public static class RouteTableTestHelper
{
    /// <summary>
    /// 构造实例描述符（附加端点默认为空，其它字段取测试常用值）。
    /// </summary>
    public static InstanceDescriptor Descriptor(
        string role,
        string instanceId,
        string tcpEndpoint,
        InstanceStatus status = InstanceStatus.Active,
        IReadOnlyList<string> additionalEndpoints = null,
        int load = 0,
        long incarnation = 1)
    {
        return new InstanceDescriptor(
            role,
            instanceId,
            tcpEndpoint,
            status,
            load,
            EndpointAddressKind.IPv4,
            incarnation,
            DateTime.UtcNow,
            additionalEndpoints);
    }

    /// <summary>
    /// 从一组实例描述符构造路由表。
    /// </summary>
    public static RoleRouteTable Table(params InstanceDescriptor[] instances)
    {
        return RoleRouteTable.FromInstances(instances);
    }

    /// <summary>
    /// 构造固定路由表提供者（<see cref="IRoleRouteTableProvider.Current"/> 恒返回给定表）。
    /// </summary>
    public static FixedRouteTableProvider Provider(RoleRouteTable table)
    {
        return new FixedRouteTableProvider(table);
    }

    /// <summary>
    /// 构造空路由表提供者（无任何实例）。
    /// </summary>
    public static FixedRouteTableProvider EmptyProvider()
    {
        return new FixedRouteTableProvider(RoleRouteTable.Empty);
    }
}
