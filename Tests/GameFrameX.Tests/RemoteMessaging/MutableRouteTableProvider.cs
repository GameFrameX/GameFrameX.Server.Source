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
/// 可变路由表提供者（测试替身）：按服务名登记端点，<see cref="Current"/> 每次访问重建快照。
/// </summary>
/// <remarks>
/// Mutable route-table provider (test double): endpoints are registered per service name and
/// <see cref="Current"/> rebuilds the snapshot on every access, so later registrations are
/// visible to already-built resolvers/clients.
/// </remarks>
public sealed class MutableRouteTableProvider : IRoleRouteTableProvider
{
    private readonly ConcurrentDictionary<string, InstanceDescriptor> _instances = new(StringComparer.Ordinal);
    private int _sequence;
    /// <summary>
    /// 登记一个服务端点（实例 Id 缺省按登记序生成；同 (服务, 实例) 重复登记时后登记者胜出）。
    /// </summary>
    public MutableRouteTableProvider SetServiceEndpoint(
        string serviceName,
        string endpoint,
        string instanceId = null,
        IReadOnlyList<string> additionalEndpoints = null)
    {
        // 实例 Id 全表唯一（RoleRouteTable 按实例 Id 建视图），缺省按 (服务名, 登记序) 生成避免跨服务碰撞
        var resolvedInstanceId = instanceId ?? $"{serviceName}-{Interlocked.Increment(ref _sequence)}";
        _instances[serviceName + "/" + resolvedInstanceId] = RouteTableTestHelper.Descriptor(
            serviceName, resolvedInstanceId, endpoint, InstanceStatus.Active, additionalEndpoints);
        return this;
    }

    /// <summary>
    /// 移除一个服务的全部实例登记。
    /// </summary>
    public MutableRouteTableProvider RemoveService(string serviceName)
    {
        var keys = _instances.Keys.Where(key => key.StartsWith(serviceName + "/", StringComparison.Ordinal)).ToArray();
        foreach (var key in keys)
        {
            _instances.TryRemove(key, out _);
        }

        return this;
    }

    public RoleRouteTable Current
    {
        get { return RoleRouteTable.FromInstances(_instances.Values.ToArray()); }
    }
}
