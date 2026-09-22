// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规及开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
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
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using GameFrameX.NetWork.RemoteMessaging.Discovery;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// EndpointParser 地址格式用例集（C143d D15 / AC-4a：域名/容器名/Kubernetes Service 名/IPv4/IPv6 方括号）。
/// </summary>
/// <remarks>
/// The address-format suite for EndpointParser (C143d D15 / AC-4a). These are the
/// CI cases required by the change: every supported shape must parse to the exact
/// scheme/host/port/address-kind triple, and every structural violation must fail
/// loudly with EndpointFormatException.
/// </remarks>
public sealed class EndpointParserTests
{
    [Theory]
    [InlineData("tcp://game-1.gameframex:7777", "tcp", "game-1.gameframex", 7777, EndpointAddressKind.DnsName)]
    [InlineData("TCP://Game-1.GameFrameX:7777", "tcp", "Game-1.GameFrameX", 7777, EndpointAddressKind.DnsName)]
    [InlineData("kcp://match-service:9000", "kcp", "match-service", 9000, EndpointAddressKind.DnsName)]
    [InlineData("ws://social.internal.svc.cluster.local:8080", "ws", "social.internal.svc.cluster.local", 8080, EndpointAddressKind.DnsName)]
    [InlineData("wss://gateway.example.com:443", "wss", "gateway.example.com", 443, EndpointAddressKind.DnsName)]
    [InlineData("tcp://10.0.0.17:7777", "tcp", "10.0.0.17", 7777, EndpointAddressKind.IPv4)]
    [InlineData("tcp://127.0.0.1:1", "tcp", "127.0.0.1", 1, EndpointAddressKind.IPv4)]
    [InlineData("tcp://[::1]:7777", "tcp", "::1", 7777, EndpointAddressKind.IPv6)]
    [InlineData("kcp://[2001:db8::1]:9000", "kcp", "2001:db8::1", 9000, EndpointAddressKind.IPv6)]
    [InlineData("tcp://host:65535", "tcp", "host", 65535, EndpointAddressKind.DnsName)]
    public void Parse_WithSupportedShapes_ShouldProduceExactTriple(string endpoint, string expectedScheme, string expectedHost, int expectedPort, EndpointAddressKind expectedAddressKind)
    {
        var parsed = EndpointParser.Parse(endpoint);

        Assert.Equal(expectedScheme, parsed.Scheme);
        Assert.Equal(expectedHost, parsed.Host);
        Assert.Equal(expectedPort, parsed.Port);
        Assert.Equal(expectedAddressKind, parsed.AddressKind);
    }

    [Fact]
    public void Parse_WithWhitespaceAround_ShouldTrim()
    {
        var parsed = EndpointParser.Parse("  tcp://host:7777  ");

        Assert.Equal("tcp", parsed.Scheme);
        Assert.Equal("host", parsed.Host);
        Assert.Equal(7777, parsed.Port);
    }

    [Fact]
    public void Parse_ToString_ShouldRoundTrip()
    {
        var original = EndpointParser.Parse("tcp://[2001:db8::1]:9000");

        var roundTripped = EndpointParser.Parse(original.ToString());

        Assert.Equal(original.Scheme, roundTripped.Scheme);
        Assert.Equal(original.Host, roundTripped.Host);
        Assert.Equal(original.Port, roundTripped.Port);
        Assert.Equal(original.AddressKind, roundTripped.AddressKind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Parse_WithNullOrEmpty_ShouldThrow(string endpoint)
    {
        if (endpoint == null)
        {
            Assert.Throws<ArgumentNullException>(() => EndpointParser.Parse(endpoint));
        }
        else
        {
            Assert.Throws<EndpointFormatException>(() => EndpointParser.Parse(endpoint));
        }
    }

    [Theory]
    [InlineData("game-1.gameframex:7777")]           // scheme 缺失
    [InlineData("://host:7777")]                      // scheme 为空
    [InlineData("http://host:7777")]                  // scheme 不受支持
    [InlineData("tcp://host")]                        // 端口缺失
    [InlineData("tcp://host:")]                       // 端口为空
    [InlineData("tcp://host:abc")]                    // 端口非数字
    [InlineData("tcp://host:0")]                      // 端口越界（下界）
    [InlineData("tcp://host:65536")]                  // 端口越界（上界）
    [InlineData("tcp://host:-1")]                     // 端口为负
    [InlineData("tcp://")]                            // host 缺失
    [InlineData("tcp://:7777")]                       // host 为空
    [InlineData("tcp://[::1")]                        // IPv6 方括号未闭合
    [InlineData("tcp://[]:7777")]                     // IPv6 字面量为空
    [InlineData("tcp://[not-an-address]:7777")]       // 方括号内非 IPv6
    [InlineData("tcp://[::1]7777")]                   // IPv6 后缺端口冒号
    [InlineData("tcp://[::1]:")]                      // IPv6 端口缺失
    [InlineData("tcp://fe80::1:7777")]                // 未加方括号的 IPv6
    [InlineData("tcp://host/path:7777")]           // host 含路径分隔符
    [InlineData("tcp://user@host:7777")]           // host 含 userinfo 分隔符
    [InlineData("tcp://host?q=1:7777")]            // host 含查询分隔符
    [InlineData("tcp://host#frag:7777")]           // host 含片段分隔符
    [InlineData("tcp://ho st:7777")]               // host 含空白
    [InlineData("tcp://a:b:7777")]                 // host 含冒号
    public void Parse_WithStructuralViolations_ShouldThrowEndpointFormatException(string endpoint)
    {
        Assert.Throws<EndpointFormatException>(() => EndpointParser.Parse(endpoint));
    }
}
