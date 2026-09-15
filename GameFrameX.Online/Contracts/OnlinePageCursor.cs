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

namespace GameFrameX.Online.Contracts;

/// <summary>
/// Online 分页游标契约（vault:C2 S1.2：分页统一使用明确的 <c>Cursor</c>、<c>HasMore</c> 和稳定排序，
/// 不使用无法解释的页码偏移作为跨服务游标）。
/// <para>
/// 维护约束：游标由服务端按「稳定排序键 + 偏移」编码生成，客户端视为不透明令牌只回传不解释；
/// 列表接口必须声明稳定排序（键 + 方向），保证翻页期间插入新数据不重复、不漏项（VC-1.12 稳定性由排序约定保证）。
/// </para>
/// </summary>
public sealed class OnlinePageCursor
{
    /// <summary>
    /// 获取或设置分页游标（服务端编码的不透明令牌；首页请求传空）。
    /// </summary>
    public string Cursor
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置是否还有更多数据（按稳定排序取满当前页且存在后续记录时为 <c>true</c>）。
    /// </summary>
    public bool HasMore
    {
        get;
        set;
    }

    /// <summary>
    /// 构造游标结果。
    /// </summary>
    /// <param name="cursor">游标令牌（末页为空字符串）。</param>
    /// <param name="hasMore">是否还有更多数据。</param>
    public OnlinePageCursor(string cursor, bool hasMore)
    {
        Cursor = cursor;
        HasMore = hasMore;
    }
}
