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
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameFrameX.Utility.Setting;

/// <summary>
/// <see cref="JsonElement"/> 的未定义安全序列化转换器：写侧把缺省值
/// <see cref="JsonValueKind.Undefined"/> 输出为空对象（与「未配置等效空对象」的既有文档口径一致），
/// 读侧保持默认行为。未配置的能力参数节随启动配置日志序列化时不再抛
/// <see cref="InvalidOperationException"/>（System.Text.Json 对 Undefined 元素的写操作恒抛该异常）。
/// </summary>
/// <remarks>
/// An undefined-safe serialization converter for <see cref="JsonElement"/>: the write side emits the
/// default <see cref="JsonValueKind.Undefined"/> value as an empty object (matching the documented
/// "unconfigured is equivalent to an empty object" semantics); the read side keeps the default
/// behavior. Serializing an unconfigured capability-parameter section together with the startup
/// configuration log no longer throws <see cref="InvalidOperationException"/> (System.Text.Json
/// always throws that exception when writing an Undefined element).
/// </remarks>
public sealed class UndefinedSafeJsonElementConverter : JsonConverter<JsonElement>
{
    /// <summary>
    /// 读取保持默认行为（由框架默认实现完成 JsonElement 的读取）。
    /// </summary>
    /// <remarks>
    /// Reading keeps the default behavior: the element is parsed via <see cref="JsonDocument.ParseValue"/>
    /// —不走 <c>JsonSerializer.Deserialize&lt;JsonElement&gt;</c>（那会绕开调用方选项且可能递归进入本转换器）。
    /// </remarks>
    public override JsonElement Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using (var document = JsonDocument.ParseValue(ref reader))
        {
            return document.RootElement.Clone();
        }
    }

    /// <summary>
    /// 写入 JsonElement；缺省 Undefined 值输出空对象，其余值按原样写。
    /// </summary>
    /// <remarks>
    /// Writes the JsonElement; the default Undefined value is written as an empty object and every
    /// other value is written as-is.
    /// </remarks>
    public override void Write(Utf8JsonWriter writer, JsonElement value, JsonSerializerOptions options)
    {
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            writer.WriteStartObject();
            writer.WriteEndObject();
            return;
        }

        value.WriteTo(writer);
    }
}
