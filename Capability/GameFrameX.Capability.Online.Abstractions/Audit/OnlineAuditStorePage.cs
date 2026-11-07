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
//   Any disputes or liabilities arising from secondary development based on this project
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


using System.Collections.Generic;
using GameFrameX.Capability.Online.Abstractions.Contracts;

namespace GameFrameX.Capability.Online.Abstractions.Audit;

/// <summary>
/// 统一审计存储层查询结果页（<see cref="IOnlineAuditStore.QueryAsync"/> 的唯一出参；
/// 行按跨域检索全序 (OccurredTime 倒序, EventId 序数升序) 排列，至多 <c>PageSize</c> 行）。
/// <para>
/// 维护约束：<see cref="HasMore"/> 表示「过滤后仍有下一行」（实现以第 PageSize+1 条命中探测，
/// 不预取剩余行）；行集为存储实现的防御性拷贝（持久化行语义，调用方引用不得别名存储内部状态）；
/// 无数据与跨作用域查询同构（空行集 + <see cref="HasMore"/>=<see langword="false"/>）。
/// </para>
/// </summary>
public sealed class OnlineAuditStorePage
{
    /// <summary>
    /// 获取当前页审计记录行（只读；空页为空列表而非 <see langword="null"/>）。
    /// </summary>
    public IReadOnlyList<OnlineAuditRecord> Records { get; }

    /// <summary>
    /// 获取是否还有下一行（存在第 PageSize+1 条命中时为 <see langword="true"/>；
    /// 为真时行集必为满页，页末行即服务层下一页游标的编码锚点）。
    /// </summary>
    public bool HasMore { get; }

    /// <summary>
    /// 构造存储层查询结果页。
    /// </summary>
    /// <param name="records">当前页记录行（按全序排列）。</param>
    /// <param name="hasMore">是否还有下一行。</param>
    public OnlineAuditStorePage(IReadOnlyList<OnlineAuditRecord> records, bool hasMore)
    {
        Records = records ?? new List<OnlineAuditRecord>();
        HasMore = hasMore;
    }
}
