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

/// <summary>
/// 聊天域可配置项（vault:C7 S6.5～S6.7：长度 / 频率 / 每页条数 / 撤回窗口等）。
/// <para>
/// 维护约束：所有阈值集中在本类型，服务层**不得**内嵌魔法数字——运营要按品类调节频控与撤回窗口，
/// 散落的常量改不全就会出现「限制了却提示不一致」的诡异表现。
/// 各字段的默认值即「未配置时的推荐值」，构造后可直接覆盖。
/// </para>
/// </summary>
public sealed class OnlineChatOptions
{
    /// <summary>
    /// 获取或设置单条消息内容长度上限（字符数；默认 500）。
    /// </summary>
    public int MaxContentLength
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置历史分页每页条数上限（默认 50；请求超过则按上限截断）。
    /// </summary>
    public int MaxMessagesPerPage
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置撤回窗口（秒；默认 120）。超窗后发送者也不能撤回——
    /// 无限期撤回等于允许篡改历史，他人已读后撤回会让讨论上下文凭空断裂。
    /// </summary>
    public int RecallWindowSeconds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频控窗口时长（秒；默认 10）。
    /// </summary>
    public int RateLimitWindowSeconds
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置频控窗口内允许的发送条数（默认 8）。压测需同时验证
    /// 「刷屏被拦」与「正常节奏不受影响」（误伤率 ≤0.1%）。
    /// </summary>
    public int RateLimitMaxMessages
    {
        get;
        set;
    }

    /// <summary>
    /// 获取或设置历史保留时长（秒；默认 0 表示不设上限，由运行时装配的清理调度决定）。
    /// </summary>
    public long HistoryRetentionSeconds
    {
        get;
        set;
    }

    /// <summary>
    /// 按推荐值初始化 <see cref="OnlineChatOptions"/>。
    /// </summary>
    public OnlineChatOptions()
    {
        MaxContentLength = 500;
        MaxMessagesPerPage = 50;
        RecallWindowSeconds = 120;
        RateLimitWindowSeconds = 10;
        RateLimitMaxMessages = 8;
        HistoryRetentionSeconds = 0;
    }
}
