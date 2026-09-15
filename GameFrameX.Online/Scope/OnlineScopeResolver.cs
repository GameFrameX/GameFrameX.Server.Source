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

using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Scope;

/// <summary>
/// Online 作用域解析器（vault:C2 S1.7：作用域在 Server / Client API / Hub API / Admin API 四端间传递，
/// 以鉴权上下文为准，客户端伪造字段被忽略）。
/// <para>
/// 维护约束：字段名兼容 Admin 既成事实——GET 查询参数 camelCase（<c>tenantId/appId/serverId</c>）、
/// POST 请求体 PascalCase 合并字段（<c>TenantId/AppId/ServerId</c>），本解析器按键名大小写不敏感统一提取；
/// 提取结果只是「客户端声称」，必须经 <see cref="EnforceAuthorized"/> 与鉴权上下文合并后方可使用。
/// </para>
/// </summary>
public static class OnlineScopeResolver
{
    /// <summary>
    /// 作用域租户字段名（查询参数 camelCase 形态；body PascalCase 形态经大小写不敏感匹配覆盖）。
    /// </summary>
    public const string TenantFieldName = "tenantId";

    /// <summary>
    /// 作用域 App 字段名。
    /// </summary>
    public const string AppFieldName = "appId";

    /// <summary>
    /// 作用域区服字段名。
    /// </summary>
    public const string ServerFieldName = "serverId";

    /// <summary>
    /// 从请求参数字典（查询参数与请求体合并视图）提取客户端声称的作用域。
    /// </summary>
    /// <param name="parameters">请求参数字典（键大小写不敏感处理）。</param>
    /// <param name="authorizedScope">鉴权上下文作用域（服务端权威值，客户端同名字段不可覆盖）。</param>
    /// <param name="error">提取失败时的错误码（三元组缺失或格式非法时为 <see cref="OnlineErrorCode.ScopeMissing"/>）。</param>
    /// <returns>提取成功返回合并鉴权上下文后的作用域；失败返回 <see langword="null"/>。</returns>
    public static OnlineScope TryResolve(IReadOnlyDictionary<string, object> parameters, OnlineScope authorizedScope, out OnlineErrorCode error)
    {
        if (authorizedScope == null)
        {
            error = OnlineErrorCode.ScopeMissing;
            return null;
        }

        // 鉴权上下文（服务端权威）直接生效；客户端字段仅用于服务端路由参考，永远不覆盖鉴权值（VC-1.8）。
        var claimedTenantId = ReadInt64(parameters, TenantFieldName, authorizedScope.TenantId);
        var claimedAppId = ReadInt64(parameters, AppFieldName, authorizedScope.AppId);
        var claimedServerId = ReadInt64(parameters, ServerFieldName, authorizedScope.ServerId);

        if (claimedTenantId != authorizedScope.TenantId || claimedAppId != authorizedScope.AppId || claimedServerId != authorizedScope.ServerId)
        {
            // 客户端声称与鉴权上下文不一致：以鉴权为准忽略声称值；是否构成越权拒绝由 Guard 判定（留审计）。
            claimedTenantId = authorizedScope.TenantId;
            claimedAppId = authorizedScope.AppId;
            claimedServerId = authorizedScope.ServerId;
        }

        if (authorizedScope.TenantId <= 0 || authorizedScope.AppId <= 0 || authorizedScope.ServerId <= 0)
        {
            error = OnlineErrorCode.ScopeMissing;
            return null;
        }

        error = OnlineErrorCode.None;
        return new OnlineScope(authorizedScope.TenantId, authorizedScope.AppId, authorizedScope.ServerId, authorizedScope.PlayerId);
    }

    /// <summary>
    /// 以鉴权上下文强制覆盖客户端声称的作用域（VC-1.8：服务端忽略客户端字段，以鉴权上下文为准）。
    /// <para>维护约束：本方法不对越权做拒绝判定（那是 <c>OnlineScopeGuard</c> 的职责），只保证权威值生效。</para>
    /// </summary>
    /// <param name="authorizedScope">鉴权上下文作用域（权威）。</param>
    /// <param name="clientClaimedScope">客户端声称作用域（被覆盖方，可为 <see langword="null"/>）。</param>
    /// <returns>生效作用域（即鉴权上下文值）。</returns>
    public static OnlineScope EnforceAuthorized(OnlineScope authorizedScope, OnlineScope clientClaimedScope)
    {
        if (authorizedScope == null)
        {
            throw new ArgumentNullException(nameof(authorizedScope));
        }

        return authorizedScope;
    }

    /// <summary>
    /// 从参数字典读取整型值（键大小写不敏感；缺失或格式非法时返回默认值）。
    /// </summary>
    /// <param name="parameters">参数字典。</param>
    /// <param name="fieldName">字段名。</param>
    /// <param name="defaultValue">默认值。</param>
    /// <returns>解析值或默认值。</returns>
    private static long ReadInt64(IReadOnlyDictionary<string, object> parameters, string fieldName, long defaultValue)
    {
        if (parameters == null)
        {
            return defaultValue;
        }

        foreach (var pair in parameters)
        {
            if (!string.Equals(pair.Key, fieldName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (pair.Value == null)
            {
                return defaultValue;
            }

            if (pair.Value is long longValue)
            {
                return longValue;
            }

            if (long.TryParse(pair.Value.ToString(), out var parsed))
            {
                return parsed;
            }

            return defaultValue;
        }

        return defaultValue;
    }
}
