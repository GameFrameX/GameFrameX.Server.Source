// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
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
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Text.Json;
using System.Text.Json.Serialization;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// BaseCacheState ↔ jsonb 文档序列化层（C166 T3）。
/// </summary>
/// <remarks>
/// Serialization layer between <c>BaseCacheState</c> and jsonb documents (C166 T3).
/// <para>
/// 与 BSON 序列化的对齐约定：
/// ① 显式写 null（对齐 BSON null 字段语义，<c>doc->>'K'</c> 对 JSON null 与缺失键均返回 SQL NULL，查询等价）；
/// ② 枚举按数值序列化（对齐 MongoDB C# 驱动枚举 int32 默认）；
/// ③ DateTime 全 UTC ISO-8601（毫秒→微秒精度提升不回退，P1-7）；
/// ④ 属性名原样（PascalCase，与 Mongo 元素名约定一致）。
/// 变更检测（StateHash）基于实体自决的 <c>ToBytes()</c>，与本层无关，初载路径经
/// <c>LoadFromDbPostHandler(isNew)</c> 与 Mongo 完全一致。
/// </para>
/// </remarks>
internal static class PostgreSqlJsonDocumentSerializer
{
    /// <summary>
    /// 序列化选项（进程级单例，首次使用触发初始化）。
    /// </summary>
    /// <remarks>
    /// 枚举按数值序列化（对齐 MongoDB C# 驱动枚举 int32 默认；查询翻译器对枚举属性按底层整型
    /// 生成 <c>::bigint</c> cast，字符串形态会导致 cast 失败）。
    /// Enums are serialized as numbers (matching the MongoDB C# driver's int32 default; the query
    /// translator emits a <c>::bigint</c> cast for enum properties, so a string form would fail the cast).
    /// </remarks>
    private static readonly JsonSerializerOptions DocumentOptions = new()
    {
        ReferenceHandler = ReferenceHandler.IgnoreCycles,
        PropertyNamingPolicy = null,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    /// <summary>
    /// 将状态对象序列化为 jsonb 文本。
    /// </summary>
    /// <remarks>
    /// Serializes the state object to jsonb text following the BSON alignment conventions documented
    /// on this class.
    /// </remarks>
    /// <typeparam name="TState">状态类型 / State type</typeparam>
    /// <param name="state">状态对象 / State object</param>
    /// <returns>jsonb 文本 / jsonb text</returns>
    public static string Serialize<TState>(TState state)
    {
        return JsonSerializer.Serialize(state, DocumentOptions);
    }

    /// <summary>
    /// 将 jsonb 文本反序列化为状态对象。
    /// </summary>
    /// <remarks>
    /// Deserializes jsonb text back into a state object (a parameterless constructor is required).
    /// </remarks>
    /// <typeparam name="TState">状态类型 / State type</typeparam>
    /// <param name="json">jsonb 文本 / jsonb text</param>
    /// <returns>状态对象 / State object</returns>
    public static TState Deserialize<TState>(string json) where TState : new()
    {
        return JsonSerializer.Deserialize<TState>(json, DocumentOptions);
    }

    /// <summary>
    /// 将部分更新字段字典序列化为 jsonb 合并对象（null 值项由调用方移入移除键集合）。
    /// </summary>
    /// <remarks>
    /// Serializes a partial-update field dictionary into a jsonb merge object (null-valued entries
    /// are moved into the removal key set by the caller).
    /// </remarks>
    /// <param name="updateFields">更新字段 / Update fields</param>
    /// <returns>jsonb 对象文本 / jsonb object text</returns>
    public static string SerializeFields(IReadOnlyDictionary<string, object> updateFields)
    {
        return JsonSerializer.Serialize(updateFields, DocumentOptions);
    }
}