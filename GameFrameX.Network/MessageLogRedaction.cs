// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameFrameX.Network;

/// <summary>
/// 网络消息日志脱敏——收发调试日志的敏感字段遮蔽机制。
/// </summary>
/// <remarks>
/// Network message log redaction: the sensitive-field masking mechanism for
/// send/receive debug logs.
/// 调试日志会把消息体全量 JSON 序列化（含 <c>Password</c> / <c>OldPassword</c> / <c>NewPassword</c> /
/// 各类 token 等凭据字段），日志面（控制台 / 文件 / Loki 聚合）即凭据扩散面。本类按
/// <see cref="IsSensitive"/> 的大小写不敏感「包含」匹配判定敏感属性名，命中者的字符串值在日志输出中
/// 替换为 <c>***</c>（经 <see cref="RedactJson"/> 的 JsonNode DOM 遍历实现，仅作用于日志字符串，
/// 线缆与业务对象零影响）。默认词表 {password, token, secret}，宿主可经
/// <see cref="AddSensitiveToken"/> / <see cref="RemoveSensitiveToken"/> / <see cref="ResetToDefault"/>
/// 扩展或收敛；误伤（如 TokenCount 被遮蔽）接受为日志侧保守取舍。
/// </remarks>
public static class MessageLogRedaction
{
    /// <summary>
    /// 脱敏替换占位符。
    /// </summary>
    public const string Mask = "***";

    /// <summary>
    /// 默认敏感词表（只读快照；运行表见 <see cref="_sensitiveTokens"/>）。
    /// </summary>
    private static readonly string[] DefaultTokens = { "password", "token", "secret" };

    /// <summary>
    /// 运行期敏感词表（大小写不敏感键）。
    /// </summary>
    private static readonly ConcurrentDictionary<string, byte> _sensitiveTokens = new ConcurrentDictionary<string, byte>(DefaultTokens.Select(t => new KeyValuePair<string, byte>(t, 0)), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 判断属性名是否命中敏感词表（大小写不敏感包含匹配，如 NewPassword / AccessToken 均命中）。
    /// </summary>
    /// <param name="propertyName">JSON 属性名 / The JSON property name</param>
    /// <returns>命中返回 <c>true</c> / true when the property name is sensitive</returns>
    public static bool IsSensitive(string propertyName)
    {
        if (string.IsNullOrEmpty(propertyName))
        {
            return false;
        }

        foreach (var token in _sensitiveTokens.Keys)
        {
            if (propertyName.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// 添加敏感词（幂等）。
    /// </summary>
    /// <param name="token">敏感词（大小写不敏感）/ The sensitive token (case-insensitive)</param>
    public static void AddSensitiveToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return;
        }

        _sensitiveTokens[token] = 0;
    }

    /// <summary>
    /// 移除敏感词（不存在时 no-op）。
    /// </summary>
    /// <param name="token">敏感词 / The sensitive token</param>
    public static void RemoveSensitiveToken(string token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        _sensitiveTokens.TryRemove(token, out _);
    }

    /// <summary>
    /// 重置为默认词表 {password, token, secret}。
    /// </summary>
    public static void ResetToDefault()
    {
        _sensitiveTokens.Clear();
        foreach (var token in DefaultTokens)
        {
            _sensitiveTokens[token] = 0;
        }
    }

    /// <summary>
    /// 对 JSON 文本做脱敏：递归遍历 JsonNode DOM，将敏感名属性的字符串值替换为 <c>***</c>。
    /// </summary>
    /// <remarks>
    /// Redacts a JSON text: recursively walks the JsonNode DOM and replaces string values of
    /// sensitive-named properties with <c>***</c>. 仅日志路径调用（调试收发日志），双次序列化开销
    /// 不进入业务热路径；解析失败（非 JSON 文本）原样返回，不影响日志可用性。
    /// </remarks>
    /// <param name="json">序列化后的消息 JSON / The serialized message JSON</param>
    /// <returns>脱敏后的 JSON 文本 / The redacted JSON text</returns>
    public static string RedactJson(string json)
    {
        if (string.IsNullOrEmpty(json))
        {
            return json;
        }

        JsonNode node;
        try
        {
            node = JsonNode.Parse(json);
        }
        catch (JsonException)
        {
            // 非 JSON 文本（不应发生）：原样返回，脱敏不破坏日志可用性
            return json;
        }

        if (node == null)
        {
            return json;
        }

        RedactNode(node);
        return node.ToJsonString();
    }

    /// <summary>
    /// 递归脱敏分派：对象与数组各交给专用遍历，字符串值命中即遮蔽（行为不变）。
    /// </summary>
    /// <param name="node">当前节点 / The current node</param>
    private static void RedactNode(JsonNode node)
    {
        if (node is JsonObject jsonObject)
        {
            RedactObject(jsonObject);
            return;
        }

        if (node is JsonArray jsonArray)
        {
            RedactArray(jsonArray);
        }
    }

    /// <summary>
    /// 对象脱敏：字符串值属性命中敏感词表即遮蔽，非字符串非空属性值下钻递归。
    /// </summary>
    /// <param name="jsonObject">当前对象节点 / The current object node</param>
    private static void RedactObject(JsonObject jsonObject)
    {
        foreach (var property in jsonObject)
        {
            if (property.Value is JsonValue value && value.TryGetValue<string>(out _))
            {
                if (IsSensitive(property.Key))
                {
                    jsonObject[property.Key] = Mask;
                }
            }
            else if (property.Value != null)
            {
                RedactNode(property.Value);
            }
        }
    }

    /// <summary>
    /// 数组脱敏：逐非空元素下钻递归。
    /// </summary>
    /// <param name="jsonArray">当前数组节点 / The current array node</param>
    private static void RedactArray(JsonArray jsonArray)
    {
        foreach (var element in jsonArray)
        {
            if (element != null)
            {
                RedactNode(element);
            }
        }
    }
}
