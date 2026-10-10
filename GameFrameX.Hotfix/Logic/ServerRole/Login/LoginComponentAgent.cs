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

using GameFrameX.Apps.ServerRole.Login.Component;
using GameFrameX.Apps.ServerRole.Login.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Login;

/// <summary>
/// 登录令牌服务器（Login Role）业务组件代理：令牌签发、校验与撤销。
/// </summary>
/// <remarks>
/// Login Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="LoginRules"/>（纯函数，可单测）；
/// 本类只做状态编排：令牌生成去重、过期惰性判定、撤销移除。
/// 全部业务以令牌/账号ID为键，无登录语义。
/// </remarks>
public class LoginComponentAgent : StateComponentAgent<LoginComponent, LoginState>
{
    /// <summary>
    /// 签发令牌：TTL 合法性校验通过后生成令牌并登记（碰撞时重新生成）。
    /// </summary>
    /// <param name="request">签发请求。</param>
    /// <param name="response">签发响应。</param>
    public Task OnIssueTokenAsync(ReqLoginIssueToken request, RespLoginIssueToken response)
    {
        if (!LoginRules.IsTtlValid(request.TtlSeconds))
        {
            response.ErrorCode = (int)LoginErrorCode.TtlInvalid;
            return Task.CompletedTask;
        }

        string token;
        do
        {
            token = LoginRules.CreateToken();
        }
        while (State.Tokens.ContainsKey(token));

        var now = TimerHelper.UnixTimeSeconds();
        State.Tokens[token] = new TokenEntryState
        {
            Token = token,
            AccountId = request.AccountId,
            ExpireUnixTime = now + request.TtlSeconds,
            CreatedUnixTime = now,
        };

        response.Token = token;
        response.ExpireUnixTime = now + request.TtlSeconds;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 校验令牌：存在且未过期返回 Valid=true 与归属账号ID；否则 Valid=false（不设 ErrorCode，过期条目惰性移除）。
    /// </summary>
    /// <param name="request">校验请求。</param>
    /// <param name="response">校验响应。</param>
    public Task OnVerifyTokenAsync(ReqLoginVerifyToken request, RespLoginVerifyToken response)
    {
        if (!State.Tokens.TryGetValue(request.Token, out var entry))
        {
            response.Valid = false;
            return Task.CompletedTask;
        }

        var now = TimerHelper.UnixTimeSeconds();
        if (entry.ExpireUnixTime <= now)
        {
            State.Tokens.Remove(request.Token);
            response.Valid = false;
            return Task.CompletedTask;
        }

        response.Valid = true;
        response.AccountId = entry.AccountId;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 撤销令牌：移除登记；不存在（含已过期被惰性清理）返回 <see cref="LoginErrorCode.TokenInvalid"/>。
    /// </summary>
    /// <param name="request">撤销请求。</param>
    /// <param name="response">撤销响应。</param>
    public Task OnRevokeTokenAsync(ReqLoginRevokeToken request, RespLoginRevokeToken response)
    {
        if (!State.Tokens.Remove(request.Token))
        {
            response.ErrorCode = (int)LoginErrorCode.TokenInvalid;
            return Task.CompletedTask;
        }

        return Task.CompletedTask;
    }
}
