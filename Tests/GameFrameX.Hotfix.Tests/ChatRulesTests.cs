// ==========================================================================================
//  GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//  GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//  均受中华人民共和国及相关国际法律法规保护。
//  are protected by the laws of the People's Republic of China and relevant international regulations.
// 
//  使用本项目须严格遵守相应法律法规及开源许可证之规定。
//  Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
// 
//  本项目采用 MIT 许可证与 Apache License 2.0 双许可证分发，
//  This project is dual-licensed under the MIT License and Apache License 2.0,
//  完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//  please refer to the LICENSE file in the root directory of the source code for the full license text.
// 
//  禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//  It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//  侵犯他人合法权益等法律法规所禁止的行为！
//  or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//  因基于本项目二次开发所产生的一切法律纠纷与责任，
//  Any legal disputes and liabilities arising from secondary development based on this project
//  本项目组织与贡献者概不承担。
//  shall be borne solely by the developer; the project organization and contributors assume no responsibility.
// 
//  GitHub 仓库：https://github.com/GameFrameX
//  GitHub Repository: https://github.com/GameFrameX
//  Gitee  仓库：https://gitee.com/GameFrameX
//  Gitee Repository:  https://gitee.com/GameFrameX
//  官方文档：https://gameframex.doc.alianblank.com/
//  Official Documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using GameFrameX.Apps.ServerRole.Chat.Entity;
using GameFrameX.Hotfix.Logic.ServerRole.Chat;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// Chat 业务纯规则测试（C197 Phase 2）。
/// </summary>
/// <remarks>
/// 覆盖文本合法性边界、容量判定、拉取条数归一化、环形历史削顶与增量选择语义。
/// </remarks>
public class ChatRulesTests
{
    /// <summary>
    /// 文本合法性：空白与超长拒绝、边界长度接受。
    /// </summary>
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("a", true)]
    [InlineData("你好", true)]
    public void IsTextValid_BoundaryCases(string text, bool expected)
    {
        Assert.Equal(expected, ChatRules.IsTextValid(text));
    }

    /// <summary>
    /// 文本达到上限长度（200）仍合法，超限（201）非法。
    /// </summary>
    [Fact]
    public void IsTextValid_LengthLimit()
    {
        Assert.True(ChatRules.IsTextValid(new string('a', ChatRules.MaxTextLength)));
        Assert.False(ChatRules.IsTextValid(new string('a', ChatRules.MaxTextLength + 1)));
    }

    /// <summary>
    /// 成员容量：达到上限后不可再加入。
    /// </summary>
    [Fact]
    public void CanJoin_RejectsAtCapacity()
    {
        Assert.True(ChatRules.CanJoin(ChatRules.MaxChannelMembers - 1));
        Assert.False(ChatRules.CanJoin(ChatRules.MaxChannelMembers));
    }

    /// <summary>
    /// 拉取条数归一化：&lt;=0 与超上限都按上限处理。
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(ChatRules.MaxPullLimit + 1)]
    public void ClampPullLimit_NormalizesToMax(int limit)
    {
        Assert.Equal(ChatRules.MaxPullLimit, ChatRules.ClampPullLimit(limit));
    }

    /// <summary>
    /// 环形历史：追加超过保留上限后最旧消息被淘汰，且总数不超过上限。
    /// </summary>
    [Fact]
    public void AppendMessage_EvictsOldestBeyondCapacity()
    {
        var channel = new ChatChannelState { ChannelId = 1 };
        for (var seq = 1; seq <= ChatRules.MaxHistoryMessages + 10; seq++)
        {
            ChatRules.AppendMessage(channel, new ChatMessageState { Seq = seq, PlayerId = 7, Text = $"m{seq}" });
        }

        Assert.Equal(ChatRules.MaxHistoryMessages, channel.Messages.Count);
        Assert.Equal(11, channel.Messages[0].Seq);
        Assert.Equal(ChatRules.MaxHistoryMessages + 10, channel.Messages[^1].Seq);
    }

    /// <summary>
    /// 增量选择：仅返回序号大于 sinceSeq 的消息，升序且不超过 limit。
    /// </summary>
    [Fact]
    public void SelectHistory_IncrementalAndOrdered()
    {
        var channel = new ChatChannelState { ChannelId = 1 };
        for (var seq = 1; seq <= 10; seq++)
        {
            ChatRules.AppendMessage(channel, new ChatMessageState { Seq = seq, PlayerId = 7, Text = $"m{seq}" });
        }

        var since = ChatRules.SelectHistory(channel, 3, 100);
        Assert.Equal(new long[] { 4, 5, 6, 7, 8, 9, 10 }, since.Select(message => message.Seq));

        var limited = ChatRules.SelectHistory(channel, 3, 2);
        Assert.Equal(new long[] { 4, 5 }, limited.Select(message => message.Seq));

        Assert.Empty(ChatRules.SelectHistory(channel, 10, 100));
    }
}
