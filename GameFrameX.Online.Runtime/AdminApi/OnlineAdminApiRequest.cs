// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// Online admin API 请求包装（action 名 + 原始请求体 + 横切字段提取）。
/// <para>
/// 维护约束：请求体属性名以 PascalCase 为准（Admin 线缆既成事实），提取一律大小写不敏感兜底；
/// 作用域三元组经 <see cref="TryReadClaimedScope"/> 提取「客户端声称」，由调用方交 <see cref="OnlineScopeGuard"/>
/// 与运行时授权作用域比对后判定拒绝（3002/3003/3004），本类型不做拒绝判定。
/// </para>
/// </summary>
public sealed class OnlineAdminApiRequest
{
    /// <summary>
    /// 空 body 的根元素（Clone 后独立于源文档，无需释放）。
    /// </summary>
    private static readonly JsonElement EmptyRoot = JsonDocument.Parse("{}").RootElement.Clone();

    /// <summary>
    /// 初始化 <see cref="OnlineAdminApiRequest"/>。
    /// </summary>
    /// <param name="action">action 名（路由末段）。</param>
    /// <param name="bodyJson">原始请求体 JSON 字符串（空 body 按空对象处理）。</param>
    public OnlineAdminApiRequest(string action, string bodyJson)
    {
        Action = action;
        if (string.IsNullOrWhiteSpace(bodyJson))
        {
            Root = EmptyRoot;
            BodyText = "{}";
        }
        else
        {
            Document = JsonDocument.Parse(bodyJson);
            Root = Document.RootElement;
            BodyText = bodyJson;
        }
    }

    /// <summary>
    /// 获取 action 名。
    /// </summary>
    public string Action
    {
        get;
    }

    /// <summary>
    /// 获取原始请求体 JSON 字符串（幂等请求摘要的规范化文本来源）。
    /// </summary>
    public string BodyText
    {
        get;
    }

    /// <summary>
    /// 获取请求体根元素。
    /// </summary>
    public JsonElement Root
    {
        get;
    }

    /// <summary>
    /// 获取请求体文档（空 body 时为 null——根元素来自内联空对象文档）。
    /// </summary>
    public JsonDocument Document
    {
        get;
    }

    /// <summary>
    /// 提取请求标识（未携带时由调用方生成；Admin 线缆不强制携带 RequestId）。
    /// </summary>
    /// <returns>请求标识；未携带返回 null。</returns>
    public string ReadRequestId()
    {
        return ReadString("RequestId");
    }

    /// <summary>
    /// 提取幂等键（仅副作用命令携带）。
    /// </summary>
    /// <returns>幂等键；未携带返回 null。</returns>
    public string ReadIdempotencyKey()
    {
        return ReadString("IdempotencyKey");
    }

    /// <summary>
    /// 提取客户端声称的作用域三元组（POST 请求体顶层 <c>TenantId</c>/<c>AppId</c>/<c>ServerId</c>，大小写不敏感）。
    /// </summary>
    /// <param name="tenantId">输出：声称租户标识。</param>
    /// <param name="appId">输出：声称应用标识。</param>
    /// <param name="serverId">输出：声称区服标识。</param>
    /// <returns>三元组完整提取返回 true；任一字段缺失或格式非法返回 false（由调用方按作用域缺失拒绝）。</returns>
    public bool TryReadClaimedScope(out long tenantId, out long appId, out long serverId)
    {
        tenantId = 0;
        appId = 0;
        serverId = 0;
        if (Root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        var hasTenant = TryReadInt64("TenantId", out tenantId);
        var hasApp = TryReadInt64("AppId", out appId);
        var hasServer = TryReadInt64("ServerId", out serverId);
        return hasTenant && hasApp && hasServer;
    }

    /// <summary>
    /// 按属性名读取字符串（大小写不敏感；非字符串 JSON 值取其原文）。
    /// </summary>
    /// <param name="name">属性名。</param>
    /// <returns>字符串值；缺失返回 null。</returns>
    public string ReadString(string name)
    {
        if (Root.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        foreach (var property in Root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
        }

        return null;
    }

    /// <summary>
    /// 按属性名读取可空长整型。
    /// </summary>
    /// <param name="name">属性名。</param>
    /// <returns>长整型值；缺失或非法返回 null。</returns>
    public long? ReadNullableInt64(string name)
    {
        if (TryReadInt64(name, out var value))
        {
            return value;
        }

        return null;
    }

    /// <summary>
    /// 按属性名读取可空整型。
    /// </summary>
    /// <param name="name">属性名。</param>
    /// <returns>整型值；缺失或非法返回 null。</returns>
    public int? ReadNullableInt32(string name)
    {
        if (TryReadInt64(name, out var value) && value >= int.MinValue && value <= int.MaxValue)
        {
            return (int)value;
        }

        return null;
    }

    /// <summary>
    /// 按属性名读取长整型（大小写不敏感）。
    /// </summary>
    /// <param name="name">属性名。</param>
    /// <param name="value">输出：读取值。</param>
    /// <returns>读取成功返回 true。</returns>
    public bool TryReadInt64(string name, out long value)
    {
        value = 0;
        if (Root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in Root.EnumerateObject())
        {
            if (!string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt64(out value))
            {
                return true;
            }

            if (property.Value.ValueKind == JsonValueKind.String && long.TryParse(property.Value.GetString(), out value))
            {
                return true;
            }

            return false;
        }

        return false;
    }
}
