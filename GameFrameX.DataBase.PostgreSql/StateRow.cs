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
//   or violate the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:     https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Reflection;
using GameFrameX.DataBase;
using GameFrameX.Foundation.Localization.Core;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GameFrameX.DataBase.PostgreSql;

/// <summary>
/// jsonb 文档表包装实体：每 <c>CacheState</c> 类型一张 <c>(id bigint PK, doc jsonb)</c> 文档表的 EF 映射载体。
/// </summary>
/// <typeparam name="TState">缓存状态类型 / The cache state type</typeparam>
/// <remarks>
/// Wrapper entity for the per-state-type jsonb document table: <c>Id</c> maps the <c>id</c> primary key
/// column and <c>Doc</c> maps the whole state object as an EF owned JSON document on the <c>doc</c> jsonb column
/// (table / column names identical to the legacy storage model — zero data migration).
/// </remarks>
public sealed class StateRow<TState> where TState : BaseCacheState, new()
{
    /// <summary>
    /// 文档主键（映射 <c>id bigint PRIMARY KEY</c>，与状态对象 <c>Id</c> 同值）。
    /// </summary>
    /// <remarks>
    /// The document primary key (mapped to <c>id bigint PRIMARY KEY</c>, same value as the state's <c>Id</c>).
    /// </remarks>
    public long Id { get; set; }

    /// <summary>
    /// 整文档状态对象（owned JSON 映射到 <c>doc</c> jsonb 列）。
    /// </summary>
    /// <remarks>
    /// The whole-document state object (owned JSON mapped onto the <c>doc</c> jsonb column).
    /// </remarks>
    public TState Doc { get; set; }
}
