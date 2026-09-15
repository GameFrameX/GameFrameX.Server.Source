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
using GameFrameX.Online.Contracts;

namespace GameFrameX.Online.Audit;

/// <summary>
/// 统一审计跨域检索分页结果（行按 (OccurredTime 毫秒倒序, EventId 序数升序) 全序排列；
/// 翻页经不透明游标，翻页期间新接入的记录不重不漏）。
/// <para>
/// 维护约束：无任何数据与跨作用域查询**同构**（空行集 + <c>HasMore=false</c> + 空游标，反预言——
/// 不向调用方泄露「其它租户/App 存在审计」的存在性差异）。
/// </para>
/// </summary>
public sealed class OnlineAuditPage
{
    /// <summary>
    /// 获取当前页审计记录行（只读；空页为空列表而非 <see langword="null"/>）。
    /// </summary>
    public IReadOnlyList<OnlineAuditRecord> Records
    {
        get;
    }

    /// <summary>
    /// 获取分页游标（末页 HasMore=false 且游标为空字符串）。
    /// </summary>
    public OnlinePageCursor Cursor
    {
        get;
    }

    /// <summary>
    /// 构造审计分页结果。
    /// </summary>
    /// <param name="records">当前页记录行。</param>
    /// <param name="cursor">分页游标。</param>
    public OnlineAuditPage(IReadOnlyList<OnlineAuditRecord> records, OnlinePageCursor cursor)
    {
        Records = records ?? new List<OnlineAuditRecord>();
        Cursor = cursor ?? new OnlinePageCursor(string.Empty, false);
    }
}
