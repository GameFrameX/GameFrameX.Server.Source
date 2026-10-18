// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB 仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Text.Json;
using GameFrameX.Network;
using GameFrameX.Proto.Proto;
using Xunit;

namespace GameFrameX.Tests.Network;

/// <summary>
/// 消息日志脱敏单元测试（C272）/ Message log redaction unit tests (C272)
/// </summary>
/// <remarks>
/// 覆盖敏感词匹配（大小写/包含变体）、嵌套对象与数组的 DOM 遮蔽、非 JSON 文本透传、
/// 词表扩展与重置，以及 FormatMessage 对携带 Password 消息的端到端脱敏。
/// </remarks>
public class MessageLogRedactionTests : IDisposable
{
    public MessageLogRedactionTests()
    {
        MessageLogRedaction.ResetToDefault();
    }

    public void Dispose()
    {
        MessageLogRedaction.ResetToDefault();
    }

    /// <summary>
    /// 默认词表应大小写不敏感包含匹配：Password / NewPassword / AccessToken / ClientSecret 均命中。
    /// </summary>
    [Theory]
    [InlineData("Password", true)]
    [InlineData("NewPassword", true)]
    [InlineData("OldPassword", true)]
    [InlineData("accessToken", true)]
    [InlineData("ClientSecret", true)]
    [InlineData("secret", true)]
    [InlineData("AccountName", false)]
    [InlineData("Id", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsSensitive_DefaultTokens_ShouldMatchContainmentCaseInsensitive(string name, bool expected)
    {
        Assert.Equal(expected, MessageLogRedaction.IsSensitive(name));
    }

    /// <summary>
    /// 嵌套对象与数组内的敏感字段值应被替换为 ***，非敏感字段保持原值。
    /// </summary>
    [Fact]
    public void RedactJson_NestedPayload_ShouldMaskSensitiveValuesOnly()
    {
        const string json = "{\"AccountName\":\"alice\",\"Password\":\"p@ss\",\"Tokens\":[{\"AccessToken\":\"abc\"},{\"Kind\":2}],\"Level\":7}";

        var redacted = MessageLogRedaction.RedactJson(json);

        using var document = JsonDocument.Parse(redacted);
        var root = document.RootElement;
        Assert.Equal("alice", root.GetProperty("AccountName").GetString());
        Assert.Equal("***", root.GetProperty("Password").GetString());
        Assert.Equal("***", root.GetProperty("Tokens")[0].GetProperty("AccessToken").GetString());
        Assert.Equal(2, root.GetProperty("Tokens")[1].GetProperty("Kind").GetInt32());
        Assert.Equal(7, root.GetProperty("Level").GetInt32());
        Assert.DoesNotContain("p@ss", redacted);
        Assert.DoesNotContain("abc", redacted);
    }

    /// <summary>
    /// 非 JSON 文本应原样透传（脱敏不破坏日志可用性）。
    /// </summary>
    [Fact]
    public void RedactJson_NonJsonText_ShouldReturnAsIs()
    {
        Assert.Equal("not a json", MessageLogRedaction.RedactJson("not a json"));
        Assert.Equal(string.Empty, MessageLogRedaction.RedactJson(string.Empty));
    }

    /// <summary>
    /// 词表扩展与移除：AddSensitiveToken 后新词命中，RemoveSensitiveToken 后不再命中，ResetToDefault 恢复默认词表。
    /// </summary>
    [Fact]
    public void SensitiveTokens_AddRemoveReset_ShouldToggleMatching()
    {
        Assert.False(MessageLogRedaction.IsSensitive("PhoneNumber"));

        MessageLogRedaction.AddSensitiveToken("phone");
        Assert.True(MessageLogRedaction.IsSensitive("PhoneNumber"));
        Assert.Equal("{\"PhoneNumber\":\"***\"}", MessageLogRedaction.RedactJson("{\"PhoneNumber\":\"13800000000\"}"));

        MessageLogRedaction.RemoveSensitiveToken("phone");
        Assert.False(MessageLogRedaction.IsSensitive("PhoneNumber"));

        MessageLogRedaction.RemoveSensitiveToken("password");
        Assert.False(MessageLogRedaction.IsSensitive("Password"));
        MessageLogRedaction.ResetToDefault();
        Assert.True(MessageLogRedaction.IsSensitive("Password"));
    }

    /// <summary>
    /// FormatMessage 对携带 Password 的真实 proto 消息应输出脱敏结果（控制台文本路径）。
    /// </summary>
    [Fact]
    public void FormatMessage_MessageWithPassword_ShouldMaskInOutput()
    {
        var message = new ReqAccountRegister
        {
            AccountName = "alice",
            Password = "super-secret-pw",
        };

        var formatted = MessageObjectLoggerHelper.FormatMessage((200 << 16) + 10, 1, message, 0);

        Assert.NotNull(formatted);
        Assert.DoesNotContain("super-secret-pw", formatted);
        Assert.Contains("***", formatted);
        Assert.Contains("alice", formatted);
    }
}
