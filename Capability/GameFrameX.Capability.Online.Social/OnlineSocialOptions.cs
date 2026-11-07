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

namespace GameFrameX.Capability.Online.Social;

using GameFrameX.Capability.Hosting.Abstractions;

/// <summary>
/// 社交能力包级 Options 根类型（C294 机制化：manifest 声明，合并序 = 包内默认 → 配置段
/// <c>Online.Social</c> → 代码 Configure → <see cref="Validate"/>，此后无写入入口）。
/// <para>
/// 维护约束：子域 Options 聚合为属性（<see cref="Chat"/> / <see cref="Friend"/> / <see cref="Report"/>），默认值唯一来源 =
/// 各字段初始值；服务构造收 Options 必填参数，不再自带参数默认值或 <c>?? new</c> 兜底——
/// 配置错误由 <see cref="Validate"/> 在装配期一次报全。
/// </para>
/// </summary>
public sealed class OnlineSocialOptions : ICapabilityOptions
{
    /// <summary>
    /// 获取或设置聊天域可配置项（长度 / 频率 / 每页条数 / 撤回窗口）。
    /// </summary>
    public OnlineChatOptions Chat { get; set; } = new OnlineChatOptions();

    /// <summary>
    /// 获取或设置好友域可配置项（请求存活时长 / 搜索与好友上限 / 旧数据迁移开关）。
    /// </summary>
    public OnlineFriendOptions Friend { get; set; } = new OnlineFriendOptions();

    /// <summary>
    /// 获取或设置举报域可配置项（提交频控双窗口阈值）。
    /// </summary>
    public OnlineReportOptions Report { get; set; } = new OnlineReportOptions();

    /// <summary>
    /// 校验合并后的最终值（聊天 / 好友 / 举报各阈值须为正数；聊天历史保留时长允许 0 = 不设上限）。
    /// </summary>
    /// <param name="validation">校验问题收集器。</param>
    public void Validate(CapabilityOptionsValidation validation)
    {
        if (Chat != null)
        {
            validation.RequirePositive("Chat.MaxContentLength", Chat.MaxContentLength);
            validation.RequirePositive("Chat.MaxMessagesPerPage", Chat.MaxMessagesPerPage);
            validation.RequirePositive("Chat.RecallWindowSeconds", Chat.RecallWindowSeconds);
            validation.RequirePositive("Chat.RateLimitWindowSeconds", Chat.RateLimitWindowSeconds);
            validation.RequirePositive("Chat.RateLimitMaxMessages", Chat.RateLimitMaxMessages);
            if (Chat.HistoryRetentionSeconds < 0)
            {
                validation.AddError("Chat.HistoryRetentionSeconds", "must not be negative (actual: " + Chat.HistoryRetentionSeconds + ")");
            }
        }
        else
        {
            validation.AddError("Chat", "must not be null");
        }

        if (Friend != null)
        {
            validation.RequirePositive("Friend.RequestTimeToLiveSeconds", Friend.RequestTimeToLiveSeconds);
            validation.RequirePositive("Friend.SearchLimit", Friend.SearchLimit);
            validation.RequirePositive("Friend.FriendLimit", Friend.FriendLimit);
        }
        else
        {
            validation.AddError("Friend", "must not be null");
        }

        if (Report != null)
        {
            validation.RequirePositive("Report.MaximumSubmissionsPerMinute", Report.MaximumSubmissionsPerMinute);
            validation.RequirePositive("Report.MaximumSubmissionsPerDay", Report.MaximumSubmissionsPerDay);
        }
        else
        {
            validation.AddError("Report", "must not be null");
        }
    }
}
