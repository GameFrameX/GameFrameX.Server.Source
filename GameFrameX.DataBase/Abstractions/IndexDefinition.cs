// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 协议分发，
//   This project is licensed under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯其他合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:   https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System.Linq.Expressions;

namespace GameFrameX.DataBase.Abstractions;

/// <summary>
/// 集合级索引定义参数对象（字符串键），收敛索引 API 的键名、方向散参并预留唯一 / TTL 位（C157）。
/// </summary>
/// <remarks>
/// Parameter object for collection-level index definitions (string key), collapsing the key / direction parameters of the index API and reserving unique / TTL slots (C157).
/// </remarks>
public sealed class IndexDefinition
{
    /// <summary>
    /// 获取或设置索引名称；为空时由数据库生成默认名称。
    /// </summary>
    /// <remarks>
    /// Gets or sets the index name; when empty the database generates a default name.
    /// </remarks>
    public string Name { get; set; }

    /// <summary>
    /// 获取或设置索引键的字段名。
    /// </summary>
    /// <remarks>
    /// Gets or sets the field name of the index key.
    /// </remarks>
    public string Key { get; set; }

    /// <summary>
    /// 获取或设置是否升序，默认为 true。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the index is ascending; defaults to true.
    /// </remarks>
    public bool Ascending { get; set; } = true;

    /// <summary>
    /// 获取或设置是否为唯一索引，默认为 false。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the index is unique; defaults to false.
    /// </remarks>
    public bool Unique { get; set; }

    /// <summary>
    /// 获取或设置 TTL 过期时间；null 表示不启用 TTL。
    /// </summary>
    /// <remarks>
    /// Gets or sets the TTL expire-after window; null disables TTL.
    /// </remarks>
    public TimeSpan? ExpireAfter { get; set; }
}

/// <summary>
/// 泛型索引定义参数对象（表达式键），收敛索引 API 的键、方向散参并预留唯一 / TTL 位（C157）。
/// </summary>
/// <remarks>
/// Parameter object for generic index definitions (expression key), collapsing the key / direction parameters of the index API and reserving unique / TTL slots (C157).
/// </remarks>
/// <typeparam name="TState">文档类型，必须实现 ICacheState 接口 / Document type, must implement ICacheState interface</typeparam>
public sealed class IndexDefinition<TState> where TState : class, ICacheState
{
    /// <summary>
    /// 获取或设置索引名称；为空时由数据库生成默认名称。
    /// </summary>
    /// <remarks>
    /// Gets or sets the index name; when empty the database generates a default name.
    /// </remarks>
    public string Name { get; set; }

    /// <summary>
    /// 获取或设置索引键表达式。
    /// </summary>
    /// <remarks>
    /// Gets or sets the index key expression.
    /// </remarks>
    public Expression<Func<TState, object>> Key { get; set; }

    /// <summary>
    /// 获取或设置是否升序，默认为 true。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the index is ascending; defaults to true.
    /// </remarks>
    public bool Ascending { get; set; } = true;

    /// <summary>
    /// 获取或设置是否为唯一索引，默认为 false。
    /// </summary>
    /// <remarks>
    /// Gets or sets whether the index is unique; defaults to false.
    /// </remarks>
    public bool Unique { get; set; }

    /// <summary>
    /// 获取或设置 TTL 过期时间；null 表示不启用 TTL。
    /// </summary>
    /// <remarks>
    /// Gets or sets the TTL expire-after window; null disables TTL.
    /// </remarks>
    public TimeSpan? ExpireAfter { get; set; }
}
