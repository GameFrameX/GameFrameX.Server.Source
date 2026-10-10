// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using GameFrameX.Client.Bot;
using Xunit;

namespace GameFrameX.Tests.Client;

/// <summary>
/// <see cref="BotRunOptions"/> 命令行解析与 <see cref="BotTransport"/> 校验的单元测试。
/// </summary>
/// <remarks>
/// 覆盖 C190 T3 选项解析任务：
/// - 默认 transport = tcp（与历史行为一致，零改动）。
/// - --transport tcp / kcp 显式值大小写不敏感。
/// - 非法 transport 值显式抛 ArgumentException（不静默回退）。
/// 反证推演：若某条断言失败，说明传输选择被错误回退或静默忽略——产品客户端 e2e 将无法按预期启动。
/// </remarks>
public class BotRunOptionsTests
{
    [Fact]
    public void Parse_DefaultTransport_ShouldBeTcp()
    {
        var options = BotRunOptions.Parse(Array.Empty<string>());

        Assert.Equal(BotTransport.Tcp, options.Transport);
    }

    [Fact]
    public void Parse_ExplicitTcp_ShouldKeepTcp()
    {
        var options = BotRunOptions.Parse(new[] { "--transport=tcp" });

        Assert.Equal(BotTransport.Tcp, options.Transport);
    }

    [Fact]
    public void Parse_ExplicitKcp_ShouldUseKcp()
    {
        var options = BotRunOptions.Parse(new[] { "--transport=kcp" });

        Assert.Equal(BotTransport.Kcp, options.Transport);
    }

    [Fact]
    public void Parse_UppercaseKcp_ShouldBeAccepted()
    {
        var options = BotRunOptions.Parse(new[] { "--transport=KCP" });

        Assert.Equal(BotTransport.Kcp, options.Transport);
    }

    [Fact]
    public void Parse_InvalidTransport_ShouldThrowArgumentException()
    {
        var ex = Assert.Throws<ArgumentException>(() => BotRunOptions.Parse(new[] { "--transport=quic" }));

        Assert.Contains("quic", ex.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("--transport", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_EmptyValue_ShouldFallBackToDefault()
    {
        // 命令行解析器跳过空值的键，与现有所有其他键（--bot-count= 等）的行为一致：
        // 空值视为格式异常而非非法值，使用默认值；非法值才是 ArgumentException。
        var options = BotRunOptions.Parse(new[] { "--transport=" });

        Assert.Equal(BotTransport.Tcp, options.Transport);
    }

    [Fact]
    public void BotTransport_IsValid_ShouldMatchSupportedValues()
    {
        Assert.True(BotTransport.IsValid("tcp"));
        Assert.True(BotTransport.IsValid("kcp"));
        Assert.True(BotTransport.IsValid("KCP"));
        Assert.False(BotTransport.IsValid("udp"));
        Assert.False(BotTransport.IsValid(""));
        Assert.False(BotTransport.IsValid(null));
    }

    [Fact]
    public void Parse_KcpHostAndPort_ShouldOverrideDefaults()
    {
        var options = BotRunOptions.Parse(new[]
        {
            "--kcp-host=10.0.0.5",
            "--kcp-port=29200",
        });

        Assert.Equal("10.0.0.5", options.KcpHost);
        Assert.Equal(29200, options.KcpPort);
    }

    [Fact]
    public void Parse_DefaultKcpPort_ShouldMatchGameKcpListener()
    {
        var options = BotRunOptions.Parse(Array.Empty<string>());

        // 默认 Kcp 端口对齐 Game 服务 KCP 监听端口（Configs/app_config.json 的 29120），
        // 保证 --transport=kcp 缺省 --kcp-port 时能连上默认监听器；且与 Tcp 端口错开。
        Assert.Equal(29120, options.KcpPort);
        Assert.NotEqual(options.TcpPort, options.KcpPort);
    }
}