//  ==========================================================================================
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

namespace GameFrameX.Online.Social;

using GameFrameX.Online.Contracts;

/// <summary>
/// 聊天历史分页结果（vault:C7 S6.6 / vault:C2 游标契约：VC-6.10）。
/// <para>
/// 维护约束：<see cref="PageCursor"/> 是服务端按「稳定排序键 + 偏移」编码的**不透明令牌**，
/// 客户端只回传不解释；翻页顺序固定为 <c>(SentAtTime, Sequence)</c> 升序，
/// 配合存储层严格递增的序号，保证翻页期间新消息不导致重复或漏项。
/// </para>
/// </summary>
public sealed class OnlineChatHistoryPage
{
    /// <summary>
    /// 获取消息列表（按 <c>(SentAtTime, Sequence)</c> 升序；已撤回消息仍在列表中，内容为空）。
    /// </summary>
    public IReadOnlyList<OnlineChatMessage> Messages
    {
        get;
        set;
    }

    /// <summary>
    /// 获取分页游标（末页 <see cref="OnlinePageCursor.HasMore"/> 为假）。
    /// </summary>
    public OnlinePageCursor PageCursor
    {
        get;
        set;
    }
}
