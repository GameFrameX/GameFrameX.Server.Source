// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System.Linq;
using GameFrameX.Apps.ServerRole.Gateway.Component;
using GameFrameX.Apps.ServerRole.Gateway.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Gateway;

/// <summary>
/// 后端路由服务器（Gateway Role）业务组件代理：路由目标注册、心跳、加权解析与注销。
/// </summary>
/// <remarks>
/// Gateway Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="GatewayRules"/>（纯函数，可单测，随机数注入）；
/// 本类只做状态编排：目标建档、心跳刷新、按角色类型过滤后的加权随机解析。
/// 全部业务以 TargetId/RoleType 为键，无登录语义。
/// </remarks>
public class GatewayComponentAgent : StateComponentAgent<GatewayComponent, GatewayState>
{
    /// <summary>
    /// 注册路由目标：权重大于 0；注册即视为一次心跳（存活起点）。
    /// </summary>
    /// <param name="request">注册请求。</param>
    /// <param name="response">注册响应。</param>
    public Task OnRegisterTargetAsync(ReqGatewayRegisterTarget request, RespGatewayRegisterTarget response)
    {
        if (!GatewayRules.IsWeightValid(request.Weight))
        {
            response.ErrorCode = (int)GatewayErrorCode.WeightInvalid;
            return Task.CompletedTask;
        }

        var entry = new TargetEntryState
        {
            TargetId = State.NextTargetId++,
            RoleType = request.RoleType,
            Host = request.Host,
            Port = request.Port,
            Weight = request.Weight,
            LastHeartbeatUnixTime = TimerHelper.UnixTimeSeconds(),
        };
        State.Targets[entry.TargetId] = entry;

        response.TargetId = entry.TargetId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 路由目标心跳：刷新最近心跳时间；不存在返回 <see cref="GatewayErrorCode.TargetNotFound"/>。
    /// </summary>
    /// <param name="request">心跳请求。</param>
    /// <param name="response">心跳响应。</param>
    public Task OnHeartbeatAsync(ReqGatewayHeartbeat request, RespGatewayHeartbeat response)
    {
        if (!State.Targets.TryGetValue(request.TargetId, out var entry))
        {
            response.ErrorCode = (int)GatewayErrorCode.TargetNotFound;
            return Task.CompletedTask;
        }

        entry.LastHeartbeatUnixTime = TimerHelper.UnixTimeSeconds();
        response.Alive = true;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 解析角色类型路由目标：按角色类型过滤候选集后，心跳存活过滤 + 加权随机选择；无可用返回 <see cref="GatewayErrorCode.NoAvailableTarget"/>。
    /// </summary>
    /// <param name="request">解析请求。</param>
    /// <param name="response">解析响应。</param>
    public Task OnResolveTargetAsync(ReqGatewayResolveTarget request, RespGatewayResolveTarget response)
    {
        var now = TimerHelper.UnixTimeSeconds();
        var entry = GatewayRules.PickTarget(State.Targets.Values.Where(target => target.RoleType == request.RoleType), now, RandomHelper.NextDouble());
        if (entry == null)
        {
            response.ErrorCode = (int)GatewayErrorCode.NoAvailableTarget;
            return Task.CompletedTask;
        }

        response.TargetId = entry.TargetId;
        response.Host = entry.Host;
        response.Port = entry.Port;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 注销路由目标：移除登记；不存在返回 <see cref="GatewayErrorCode.TargetNotFound"/>。
    /// </summary>
    /// <param name="request">注销请求。</param>
    /// <param name="response">注销响应。</param>
    public Task OnUnregisterTargetAsync(ReqGatewayUnregisterTarget request, RespGatewayUnregisterTarget response)
    {
        if (!State.Targets.Remove(request.TargetId))
        {
            response.ErrorCode = (int)GatewayErrorCode.TargetNotFound;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
