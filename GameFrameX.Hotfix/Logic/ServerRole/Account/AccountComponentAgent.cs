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

using GameFrameX.Apps.ServerRole.Account.Component;
using GameFrameX.Apps.ServerRole.Account.Entity;
using GameFrameX.Foundation.Utility;

namespace GameFrameX.Hotfix.Logic.ServerRole.Account;

/// <summary>
/// 账号服务器（Account Role）业务组件代理：账号档案的注册、查询与改密。
/// </summary>
/// <remarks>
/// Account Role 独立业务逻辑系统（C197 垂直切片）的热更侧落点。
/// 业务规则判定一律委托 <see cref="AccountRules"/>（纯函数，可单测）；
/// 本类只做状态编排：账号名唯一性、档案建档、密码哈希更新。
/// 全部业务以账号名为键，无登录语义。
/// </remarks>
public class AccountComponentAgent : StateComponentAgent<AccountComponent, AccountState>
{
    /// <summary>
    /// 注册账号：账号名唯一、名/密码合法性校验通过后建档（随机盐 + SHA256 哈希，LastLoginUnixTime=0）。
    /// </summary>
    /// <param name="request">注册请求。</param>
    /// <param name="response">注册响应。</param>
    public Task OnRegisterAsync(ReqAccountRegister request, RespAccountRegister response)
    {
        if (!AccountRules.IsNameValid(request.AccountName))
        {
            response.ErrorCode = (int)AccountErrorCode.NameInvalid;
            return Task.CompletedTask;
        }

        if (!AccountRules.IsPasswordValid(request.Password))
        {
            response.ErrorCode = (int)AccountErrorCode.PasswordTooShort;
            return Task.CompletedTask;
        }

        if (State.Accounts.ContainsKey(request.AccountName))
        {
            response.ErrorCode = (int)AccountErrorCode.AccountExists;
            return Task.CompletedTask;
        }

        var salt = AccountRules.CreateSalt();
        var profile = new AccountProfileState
        {
            AccountId = State.NextAccountId++,
            PasswordHash = AccountRules.HashPassword(salt, request.Password),
            Salt = salt,
            CreatedUnixTime = TimerHelper.UnixTimeSeconds(),
            LastLoginUnixTime = 0,
        };
        State.Accounts[request.AccountName] = profile;

        response.AccountId = profile.AccountId;
        response.CreatedUnixTime = profile.CreatedUnixTime;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 查询账号档案（不含密码）：不存在返回 <see cref="AccountErrorCode.AccountNotFound"/>。
    /// </summary>
    /// <param name="request">查询请求。</param>
    /// <param name="response">查询响应。</param>
    public Task OnQueryAsync(ReqAccountQuery request, RespAccountQuery response)
    {
        if (!State.Accounts.TryGetValue(request.AccountName, out var profile))
        {
            response.ErrorCode = (int)AccountErrorCode.AccountNotFound;
            return Task.CompletedTask;
        }

        // 查询即视为一次账号档案访问（登录态活跃），刷新最近活跃时间——字段语义为「最近一次档案访问/活跃」。
        profile.LastLoginUnixTime = TimerHelper.UnixTimeSeconds();
        response.AccountId = profile.AccountId;
        response.CreatedUnixTime = profile.CreatedUnixTime;
        response.LastLoginUnixTime = profile.LastLoginUnixTime;
        return Task.CompletedTask;
    }

    /// <summary>
    /// 修改密码：账号须存在、旧密码哈希须匹配、新密码须合法；成功后更新哈希（盐不变）。
    /// </summary>
    /// <param name="request">改密请求。</param>
    /// <param name="response">改密响应。</param>
    public Task OnChangePasswordAsync(ReqAccountChangePassword request, RespAccountChangePassword response)
    {
        if (!State.Accounts.TryGetValue(request.AccountName, out var profile))
        {
            response.ErrorCode = (int)AccountErrorCode.AccountNotFound;
            return Task.CompletedTask;
        }

        if (AccountRules.HashPassword(profile.Salt, request.OldPassword) != profile.PasswordHash)
        {
            response.ErrorCode = (int)AccountErrorCode.OldPasswordMismatch;
            return Task.CompletedTask;
        }

        if (!AccountRules.IsPasswordValid(request.NewPassword))
        {
            response.ErrorCode = (int)AccountErrorCode.PasswordTooShort;
            return Task.CompletedTask;
        }

        profile.PasswordHash = AccountRules.HashPassword(profile.Salt, request.NewPassword);
        return Task.CompletedTask;
    }
}
