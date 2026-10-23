// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
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

using GameFrameX.Network.RemoteMessaging.Versioning;
using GameFrameX.ProtoBuf.Net;
using Xunit;

namespace GameFrameX.Tests.RemoteMessaging;

/// <summary>
/// 节点间握手鉴权单元测试 / Inter-node handshake authentication unit tests
/// </summary>
/// <remarks>
/// 覆盖：密钥未启用时全链路旧行为等价（Verify 恒真 / 无凭据握手照常接受）、启用后凭据往返、
/// 缺失/错钥拒绝（ErrorCode=2）、major 不符优先报版本错（ErrorCode=1）、线缆字段往返。
/// 进程级静态密钥在每用例 try/finally 内配置与复位（窗口极小，避免波及并行 e2e 握手用例）。
/// </remarks>
public sealed class InterNodeAuthenticationTests : IDisposable
{
    public InterNodeAuthenticationTests()
    {
        InterNodeAuthentication.Reset();
    }

    public void Dispose()
    {
        InterNodeAuthentication.Reset();
    }

    /// <summary>
    /// 密钥未启用（缺省）：Verify 恒真，无凭据握手照常接受——存量部署零破坏红线。
    /// </summary>
    [Fact]
    public void Disabled_ByDefault_VerifyAlwaysTrueAndHandshakeAcceptedWithoutCredential()
    {
        Assert.False(InterNodeAuthentication.IsEnabled);

        Assert.True(InterNodeAuthentication.Verify(null, 1, 0, null));
        Assert.True(InterNodeAuthentication.Verify("nonce", 1, 0, "whatever"));
        Assert.Equal(string.Empty, InterNodeAuthentication.ComputeCredential("nonce", 1, 0));

        var reply = ProtocolVersionNegotiationServer.BuildReply(new ReqProtocolVersionNegotiation { Major = 1, Minor = 0 }, RemoteMessagingProtocolVersion.Current);
        Assert.True(reply.Accepted);
        Assert.Equal(0, reply.ErrorCode);
    }

    /// <summary>
    /// 启用后：正确密钥计算的凭据通过验签，握手被接受（ErrorCode=0）。
    /// </summary>
    [Fact]
    public void Enabled_ValidCredential_HandshakeAccepted()
    {
        try
        {
            InterNodeAuthentication.Configure("cluster-secret");
            var nonce = InterNodeAuthentication.CreateNonce();
            var credential = InterNodeAuthentication.ComputeCredential(nonce, 1, 0);

            Assert.True(InterNodeAuthentication.Verify(nonce, 1, 0, credential));

            var reply = ProtocolVersionNegotiationServer.BuildReply(
                new ReqProtocolVersionNegotiation { Major = 1, Minor = 0, Nonce = nonce, AuthCredential = credential },
                RemoteMessagingProtocolVersion.Current);
            Assert.True(reply.Accepted);
            Assert.Equal(0, reply.ErrorCode);
        }
        finally
        {
            InterNodeAuthentication.Reset();
        }
    }

    /// <summary>
    /// 启用后：凭据缺失或验签失败（错钥）→ 拒绝并报 ErrorCode=2（fail-closed）。
    /// </summary>
    [Fact]
    public void Enabled_MissingOrWrongCredential_RejectedWithAuthFailureCode()
    {
        // 准备阶段：凭据先行计算（ComputeWithSecret 会改写进程级静态密钥并复位，须在正式 Configure 之前完成）
        var nonce = InterNodeAuthentication.CreateNonce();
        var honestCredential = ComputeWithSecret("cluster-secret", nonce, 1, 0);
        var wrongKeyCredential = ComputeWithSecret("other-secret", nonce, 1, 0);
        var otherNonce = InterNodeAuthentication.CreateNonce();

        try
        {
            InterNodeAuthentication.Configure("cluster-secret");

            // 凭据整体缺失（旧版本节点形态）
            var missingReply = ProtocolVersionNegotiationServer.BuildReply(
                new ReqProtocolVersionNegotiation { Major = 1, Minor = 0 },
                RemoteMessagingProtocolVersion.Current);
            Assert.False(missingReply.Accepted);
            Assert.Equal(2, missingReply.ErrorCode);

            // 错钥凭据
            var wrongReply = ProtocolVersionNegotiationServer.BuildReply(
                new ReqProtocolVersionNegotiation { Major = 1, Minor = 0, Nonce = nonce, AuthCredential = wrongKeyCredential },
                RemoteMessagingProtocolVersion.Current);
            Assert.False(wrongReply.Accepted);
            Assert.Equal(2, wrongReply.ErrorCode);

            // 凭据与 nonce 不匹配（凭据挪用到其他 nonce）
            var replayReply = ProtocolVersionNegotiationServer.BuildReply(
                new ReqProtocolVersionNegotiation { Major = 1, Minor = 0, Nonce = otherNonce, AuthCredential = honestCredential },
                RemoteMessagingProtocolVersion.Current);
            Assert.False(replayReply.Accepted);
            Assert.Equal(2, replayReply.ErrorCode);

            Assert.False(InterNodeAuthentication.Verify(null, 1, 0, credential: "x"));
            Assert.False(InterNodeAuthentication.Verify("nonce", 1, 0, null));
        }
        finally
        {
            InterNodeAuthentication.Reset();
        }
    }

    /// <summary>
    /// 版本与凭据双重不符：major 不符优先报 ErrorCode=1（版本结论为准，语义与既有拒绝路径一致）。
    /// </summary>
    [Fact]
    public void Enabled_MajorMismatchTakesPrecedenceOverAuthFailure()
    {
        try
        {
            InterNodeAuthentication.Configure("cluster-secret");
            var reply = ProtocolVersionNegotiationServer.BuildReply(
                new ReqProtocolVersionNegotiation { Major = 9, Minor = 9 },
                RemoteMessagingProtocolVersion.Current);
            Assert.False(reply.Accepted);
            Assert.Equal(1, reply.ErrorCode);
        }
        finally
        {
            InterNodeAuthentication.Reset();
        }
    }

    /// <summary>
    /// 线缆契约：Nonce 与 AuthCredential 序列化往返保真（旧端不识别新字段按缺省 null 反序列化）。
    /// </summary>
    [Fact]
    public void HandshakeRequest_CredentialFields_SurviveWireRoundTrip()
    {
        var request = new ReqProtocolVersionNegotiation
        {
            Major = 1,
            Minor = 0,
            Nonce = "abcdef0123456789",
            AuthCredential = "deadbeef",
        };

        var decoded = ProtoBufSerializerHelper.Deserialize<ReqProtocolVersionNegotiation>(ProtoBufSerializerHelper.Serialize(request));

        Assert.Equal("abcdef0123456789", decoded.Nonce);
        Assert.Equal("deadbeef", decoded.AuthCredential);

        // 旧形态（无新字段）反序列化为 null——两向线缆兼容
        var legacy = ProtoBufSerializerHelper.Deserialize<ReqProtocolVersionNegotiation>(
            ProtoBufSerializerHelper.Serialize(new ReqProtocolVersionNegotiation { Major = 1, Minor = 0 }));
        Assert.Null(legacy.Nonce);
        Assert.Null(legacy.AuthCredential);
    }

    /// <summary>
    /// 以指定密钥计算凭据（跨密钥用例辅助；不复用被测类的静态态）。
    /// </summary>
    private static string ComputeWithSecret(string secret, string nonce, int major, int minor)
    {
        try
        {
            InterNodeAuthentication.Configure(secret);
            return InterNodeAuthentication.ComputeCredential(nonce, major, minor);
        }
        finally
        {
            InterNodeAuthentication.Reset();
        }
    }
}
