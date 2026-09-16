// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// admin 线缆解析助手（Admin 请求体的既有事实：标识类字段为字符串、枚举为数字、可空过滤字段缺失为 null）。
/// <para>
/// 维护约束：本类只做「线缆形态 → Online 域形态」的机械转换，不做业务判定；
/// 参数非法一律抛 <see cref="OnlineServiceException"/>（4xxx 段），由调度器统一映射内层协议码。
/// </para>
/// </summary>
public static class OnlineAdminApiContract
{
    /// <summary>
    /// 按属性名大小写不敏感地取子元素。
    /// </summary>
    /// <param name="root">根元素。</param>
    /// <param name="name">属性名。</param>
    /// <param name="element">输出：子元素。</param>
    /// <returns>命中返回 true。</returns>
    public static bool TryGetElement(JsonElement root, string name, out JsonElement element)
    {
        element = default;
        if (root.ValueKind != JsonValueKind.Object)
        {
            return false;
        }

        foreach (var property in root.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                element = property.Value;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 读取玩家标识（线缆字符串形态；必填正数）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>玩家标识。</returns>
    public static long ReadPlayerId(OnlineAdminApiRequest request)
    {
        if (request.TryReadInt64("PlayerId", out var playerId) && playerId > 0)
        {
            return playerId;
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "PlayerId must be a positive value (wire form: string).");
    }

    /// <summary>
    /// 读取玩家作用域（守卫通过的作用域 + 玩家主体位）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">已通过守卫的作用域。</param>
    /// <returns>含玩家位的作用域。</returns>
    public static OnlineScope ReadPlayerScope(OnlineAdminApiRequest request, OnlineScope scope)
    {
        return new OnlineScope(scope.TenantId, scope.AppId, scope.ServerId, ReadPlayerId(request));
    }

    /// <summary>
    /// 读取必填字符串。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">字段名。</param>
    /// <returns>非空字符串。</returns>
    public static string RequireString(OnlineAdminApiRequest request, string name)
    {
        var value = request.ReadString(name);
        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, name + " must be a non-empty string.");
    }

    /// <summary>
    /// 读取可选字符串（缺失返回 null）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">字段名。</param>
    /// <returns>字符串值。</returns>
    public static string ReadOptionalString(OnlineAdminApiRequest request, string name)
    {
        var value = request.ReadString(name);
        return string.IsNullOrEmpty(value) ? null : value;
    }

    /// <summary>
    /// 读取分页大小（默认 20，钳制 1～100）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>分页大小。</returns>
    public static int ReadPageSize(OnlineAdminApiRequest request)
    {
        var pageSize = request.ReadNullableInt32("PageSize");
        if (!pageSize.HasValue || pageSize.Value <= 0)
        {
            return 20;
        }

        return Math.Min(100, pageSize.Value);
    }

    /// <summary>
    /// 读取分页游标（缺失返回 null）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>游标。</returns>
    public static string ReadCursor(OnlineAdminApiRequest request)
    {
        return ReadOptionalString(request, "Cursor");
    }

    /// <summary>
    /// 读取可选长整型（线缆字符串 / 数字双形态）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">字段名。</param>
    /// <returns>值；缺失为 null。</returns>
    public static long? ReadOptionalInt64(OnlineAdminApiRequest request, string name)
    {
        var value = request.ReadNullableInt64(name);
        return value;
    }

    /// <summary>
    /// 解开 <see cref="OnlineResult{TData}"/>（失败抛 <see cref="OnlineServiceException"/>，协议码透传）。
    /// </summary>
    /// <typeparam name="TData">数据类型。</typeparam>
    /// <param name="result">服务结果。</param>
    /// <returns>数据载荷。</returns>
    public static TData Unwrap<TData>(OnlineResult<TData> result)
    {
        if (result == null)
        {
            throw new OnlineServiceException(OnlineErrorCode.InternalError, "Service returned no result.");
        }

        if (result.IsSuccess)
        {
            return result.Data;
        }

        throw new OnlineServiceException(result.Code, result.Message);
    }

    /// <summary>
    /// 当前 Unix 秒。
    /// </summary>
    /// <returns>Unix 秒。</returns>
    public static long NowSeconds()
    {
        return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }
}
