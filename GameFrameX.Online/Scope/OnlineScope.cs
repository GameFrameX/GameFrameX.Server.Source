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

namespace GameFrameX.Online.Scope;

/// <summary>
/// Online 作用域（vault:C2：TenantId/AppId/ServerId 以鉴权或服务端路由为准，不能由客户端字段直接覆盖）。
/// <para>
/// 维护约束：作用域三元组是数据隔离的根边界——跨租户/跨 App/跨服请求必须被拒绝并留审计（VC-1.6/1.7）；
/// <c>PlayerId</c> 为可选主体位，面向玩家级操作（幂等键绑定、事件归属）时必填。客户端提交的同名字段
/// 一律经 <c>OnlineScopeResolver.EnforceAuthorized</c> 以鉴权上下文覆盖，服务端不信任请求体作用域（VC-1.8）。
/// </para>
/// </summary>
public sealed class OnlineScope
{
    /// <summary>
    /// 获取租户标识（鉴权上下文强制注入，请求任何入口都不可覆盖）。
    /// </summary>
    public long TenantId
    {
        get;
    }

    /// <summary>
    /// 获取 App 标识（必须归属当前租户，经租户归属校验）。
    /// </summary>
    public long AppId
    {
        get;
    }

    /// <summary>
    /// 获取区服标识（必须归属当前 AppId，经区服归属校验）。
    /// </summary>
    public long ServerId
    {
        get;
    }

    /// <summary>
    /// 获取玩家标识（可选；玩家级操作的幂等键绑定与事件归属主体）。
    /// </summary>
    public long PlayerId
    {
        get;
    }

    /// <summary>
    /// 构造作用域。
    /// </summary>
    /// <param name="tenantId">租户标识。</param>
    /// <param name="appId">App 标识。</param>
    /// <param name="serverId">区服标识。</param>
    /// <param name="playerId">玩家标识（无主体时传 0）。</param>
    public OnlineScope(long tenantId, long appId, long serverId, long playerId = 0)
    {
        TenantId = tenantId;
        AppId = appId;
        ServerId = serverId;
        PlayerId = playerId;
    }

    /// <summary>
    /// 计算作用域的稳定字符串形式（<c>tenant:app:server[:player]</c>；玩家级操作含玩家位），
    /// 作为幂等记录的作用域绑定键与日志留痕标识。
    /// </summary>
    /// <param name="includePlayer">是否包含玩家位（玩家级幂等绑定时必须为 <c>true</c>，防止跨玩家复用幂等键）。</param>
    /// <returns>作用域键字符串。</returns>
    public string ToScopeKey(bool includePlayer)
    {
        var key = "online:" + TenantId + ":" + AppId + ":" + ServerId;
        if (includePlayer)
        {
            key += ":" + PlayerId;
        }

        return key;
    }
}
