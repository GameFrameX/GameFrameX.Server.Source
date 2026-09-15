//  ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

namespace GameFrameX.Online.Identity;

using GameFrameX.Online.Scope;

/// <summary>
/// 统一玩家上下文（vault:C3：PlayerId + AppId + ServerId + SessionId，后续模块不再各自解析身份）。
/// <para>
/// 维护约束：登录成功后由服务端生成，客户端不得自行拼接身份关系；
/// 会话进入终态后本上下文即失效，调用方不得缓存复用；
/// 作用域键经 <see cref="OnlineScope.ToScopeKey"/> 派生（幂等与审计的玩家位绑定）。
/// </para>
/// </summary>
public sealed class OnlinePlayerContext
{
    /// <summary>
    /// 获取租户标识。
    /// </summary>
    public long TenantId
    {
        get;
    }

    /// <summary>
    /// 获取应用标识。
    /// </summary>
    public long AppId
    {
        get;
    }

    /// <summary>
    /// 获取区服标识。
    /// </summary>
    public long ServerId
    {
        get;
    }

    /// <summary>
    /// 获取玩家标识（GameAccount 下属、App/Server 归属明确）。
    /// </summary>
    public long PlayerId
    {
        get;
    }

    /// <summary>
    /// 获取游戏账号标识。
    /// </summary>
    public long GameAccountId
    {
        get;
    }

    /// <summary>
    /// 获取会话标识。
    /// </summary>
    public string SessionId
    {
        get;
    }

    /// <summary>
    /// 初始化 <see cref="OnlinePlayerContext"/>。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">应用标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="playerId">玩家标识。</param>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="sessionId">会话标识。</param>
    public OnlinePlayerContext(long tenantId, long appId, long serverId, long playerId, long gameAccountId, string sessionId)
    {
        TenantId = tenantId;
        AppId = appId;
        ServerId = serverId;
        PlayerId = playerId;
        GameAccountId = gameAccountId;
        SessionId = sessionId ?? throw new ArgumentNullException(nameof(sessionId));
    }

    /// <summary>
    /// 从作用域派生玩家上下文（作用域必须已含玩家位）。
    /// </summary>
    /// <param name="scope">含玩家位的作用域。</param>
    /// <param name="gameAccountId">游戏账号标识。</param>
    /// <param name="sessionId">会话标识。</param>
    /// <returns>玩家上下文实例。</returns>
    /// <exception cref="ArgumentNullException">scope 为 null。</exception>
    /// <exception cref="ArgumentException">作用域缺少玩家位（PlayerId &lt;= 0）。</exception>
    public static OnlinePlayerContext FromScope(OnlineScope scope, long gameAccountId, string sessionId)
    {
        if (scope == null)
        {
            throw new ArgumentNullException(nameof(scope));
        }

        if (scope.PlayerId <= 0)
        {
            throw new ArgumentException("作用域缺少玩家主体位（PlayerId <= 0），不能派生玩家上下文。", nameof(scope));
        }

        return new OnlinePlayerContext(scope.TenantId, scope.AppId, scope.ServerId, scope.PlayerId, gameAccountId, sessionId);
    }
}
