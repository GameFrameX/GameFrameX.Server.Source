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

using GameFrameX.Apps.ServerRole.Auth.Component;
using GameFrameX.Apps.ServerRole.Auth.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Auth;

/// <summary>
/// 权限授予服务器（Auth Role）业务组件代理：权限的授予、撤销、查询与列举。
/// </summary>
/// <remarks>
/// Auth Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="AuthRules"/>（纯函数，可单测）；
/// 本类只做状态编排：按玩家维度的权限集合懒创建、授予记录写入与撤销移除。
/// Grant/Revoke 需登录（操作者=请求者）；Has/List 以目标玩家ID为键，无需登录。
/// </remarks>
public class AuthComponentAgent : StateComponentAgent<AuthComponent, AuthState>
{
    /// <summary>
    /// 授予权限：权限码须在白名单；重复授予返回 <see cref="AuthErrorCode.AlreadyGranted"/>；成功记录授予者与时间。
    /// </summary>
    /// <param name="grantorPlayerId">操作者玩家ID（请求者）。</param>
    /// <param name="request">授予请求。</param>
    /// <param name="response">授予响应。</param>
    public Task OnGrantAsync(long grantorPlayerId, ReqAuthGrant request, RespAuthGrant response)
    {
        if (!AuthRules.IsPermissionDefined(request.Permission))
        {
            response.ErrorCode = (int)AuthErrorCode.PermissionUnknown;
            return Task.CompletedTask;
        }

        var permissions = GetOrCreatePermissions(request.TargetPlayerId);
        if (permissions.ContainsKey(request.Permission))
        {
            response.ErrorCode = (int)AuthErrorCode.AlreadyGranted;
            return Task.CompletedTask;
        }

        permissions[request.Permission] = new GrantRecordState
        {
            GrantorPlayerId = grantorPlayerId,
            CreatedUnixTime = TimerHelper.UnixTimeSeconds(),
        };

        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤销权限：权限码须在白名单；未授予返回 <see cref="AuthErrorCode.NotGranted"/>；成功移除记录。
    /// </summary>
    /// <param name="operatorPlayerId">操作者玩家ID（请求者，仅透传语义）。</param>
    /// <param name="request">撤销请求。</param>
    /// <param name="response">撤销响应。</param>
    public Task OnRevokeAsync(long operatorPlayerId, ReqAuthRevoke request, RespAuthRevoke response)
    {
        if (!AuthRules.IsPermissionDefined(request.Permission))
        {
            response.ErrorCode = (int)AuthErrorCode.PermissionUnknown;
            return Task.CompletedTask;
        }

        if (!State.Grants.TryGetValue(request.TargetPlayerId, out var permissions)
            || !permissions.Remove(request.Permission))
        {
            response.ErrorCode = (int)AuthErrorCode.NotGranted;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询玩家是否持有指定权限（未定义权限码返回 <see cref="AuthErrorCode.PermissionUnknown"/>）。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnHasAsync(ReqAuthHas request, RespAuthHas response)
    {
        if (!AuthRules.IsPermissionDefined(request.Permission))
        {
            response.ErrorCode = (int)AuthErrorCode.PermissionUnknown;
            return Task.CompletedTask;
        }

        response.Granted = State.Grants.TryGetValue(request.TargetPlayerId, out var permissions)
                           && permissions.ContainsKey(request.Permission);
        return Task.CompletedTask;
    }

    /// <summary>
    /// 列举玩家持有的全部权限（无记录返回空列表）。
    /// </summary>
    /// <param name="request">列举请求。</param>
    /// <param name="response">列举响应。</param>
    public Task OnListPermissionsAsync(ReqAuthListPermissions request, RespAuthListPermissions response)
    {
        if (State.Grants.TryGetValue(request.TargetPlayerId, out var permissions))
        {
            response.Permissions.AddRange(permissions.Keys);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询玩家权限集合，不存在时懒创建。
    /// </summary>
    /// <param name="targetPlayerId">目标玩家ID。</param>
    /// <returns>权限集合。</returns>
    private Dictionary<AuthPermission, GrantRecordState> GetOrCreatePermissions(long targetPlayerId)
    {
        if (!State.Grants.TryGetValue(targetPlayerId, out var permissions))
        {
            permissions = new Dictionary<AuthPermission, GrantRecordState>();
            State.Grants[targetPlayerId] = permissions;
        }

        return permissions;
    }
}
