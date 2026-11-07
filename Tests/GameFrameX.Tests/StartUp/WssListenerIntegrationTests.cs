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

using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using GameFrameX.SuperSocket.Server.Host;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// 使用生产监听装配路径与真实客户端验证 WS / WSS 握手。
/// </summary>
/// <remarks>Verifies WS / WSS handshakes using production listener configuration and real clients.</remarks>
[Collection(nameof(StartUpStaticStateCollection))]
public class WssListenerIntegrationTests
{
    /// <summary>PFX、普通 PEM、加密 PEM 均可与明文端口同时完成真实握手。</summary>
    /// <remarks>PFX, unencrypted PEM and encrypted PEM support real handshakes alongside the plaintext port.</remarks>
    /// <param name="format">证书格式：0 为 PFX，1 为 PEM，2 为加密 PEM / Certificate format: 0 PFX, 1 PEM, 2 encrypted PEM</param>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task WebSocketServer_WithWssPort_ShouldAcceptSecureAndPlainHandshakes(int format)
    {
        using (var files = new TestServerCertificateFiles())
        {
            var plainPort = GetAvailablePort();
            var securePort = GetAvailablePort();
            while (securePort == plainPort)
            {
                securePort = GetAvailablePort();
            }

            var setting = new AppSetting
            {
                IsEnableWebSocket = true,
                WsPort = plainPort,
                WssPort = securePort,
                WssCertFilePath = format == 0 ? files.CertificatePath : files.PemPath,
                WssCertificateKeyPath = format == 2 ? files.EncryptedKeyPath : files.KeyPath,
                WssCertificatePassword = format == 1 ? string.Empty : TestServerCertificateFiles.Password,
                // HTTPS 配置故意无效，证明 WSS 的 TLS 参数不依赖 HTTPS。
                HttpsCertPath = "missing-https.pfx",
                HttpsCertPassword = "wrong-https-password",
            };
            var startup = new WebSocketCertificateTestStartUp(setting);
            var builder = MultipleServerHostBuilder.Create();
            startup.ConfigureWebSocketServer(builder);
            using (var host = builder.Build())
            {
                try
                {
                    await host.StartAsync();
                    using (var expectedCertificate = GameFrameX.StartUp.AppStartUpBase.LoadWssCertificate(setting))
                    {
                        await AssertHandshakeAsync("wss", securePort, expectedCertificate.Thumbprint);
                        await AssertHandshakeAsync("ws", plainPort);
                    }
                }
                finally
                {
                    await host.StopAsync();
                    await startup.ShutdownAsync();
                }
            }
        }
    }

    /// <summary>不配置 WSS 端口时保持明文监听，并忽略未启用的证书配置。</summary>
    /// <remarks>Without a WSS port, plaintext listening remains and unused certificate settings are ignored.</remarks>
    [Fact]
    public async Task WebSocketServer_WithoutWssPort_ShouldKeepPlainHandshake()
    {
        var setting = new AppSetting
        {
            IsEnableWebSocket = true, WsPort = GetAvailablePort(), WssPort = 0,
            WssCertFilePath = "missing-wss.pem", WssCertificateKeyPath = "missing-private-key.pem",
        };
        var startup = new WebSocketCertificateTestStartUp(setting);
        var builder = MultipleServerHostBuilder.Create();
        startup.ConfigureWebSocketServer(builder);
        using (var host = builder.Build())
        {
            try
            {
                await host.StartAsync();
                await AssertHandshakeAsync("ws", setting.WsPort);
            }
            finally
            {
                await host.StopAsync();
                await startup.ShutdownAsync();
            }
        }
    }

    /// <summary>启用 WSS 时错误证书配置必须在构建和监听前失败。</summary>
    /// <remarks>Invalid WSS certificate configuration must fail before host building or listening.</remarks>
    [Fact]
    public void ConfigureWebSocketServer_WithInvalidWssCertificate_ShouldFailBeforeBuild()
    {
        var startup = new WebSocketCertificateTestStartUp(new AppSetting { IsEnableWebSocket = true, WsPort = 0, WssPort = 8443 });
        var exception = Assert.Throws<InvalidOperationException>(() => startup.ConfigureWebSocketServer(MultipleServerHostBuilder.Create()));
        Assert.Contains(nameof(AppSetting.WssCertFilePath), exception.Message);
    }

    /// <summary>关闭 WebSocket 时不得读取 WSS 证书配置。</summary>
    /// <remarks>Disabled WebSocket must not load WSS certificate configuration.</remarks>
    [Fact]
    public void ConfigureWebSocketServer_WhenDisabled_ShouldIgnoreWssCertificate()
    {
        var startup = new WebSocketCertificateTestStartUp(new AppSetting { IsEnableWebSocket = false, WssPort = 8443 });
        startup.ConfigureWebSocketServer(MultipleServerHostBuilder.Create());
    }

    /// <summary>使用真实客户端完成握手，并固定信任当前测试自签证书。</summary>
    /// <remarks>Completes a real handshake, pinning trust to the current self-signed test certificate.</remarks>
    private static async Task AssertHandshakeAsync(string scheme, ushort port, string thumbprint = null)
    {
        using (var client = new ClientWebSocket())
        {
            if (thumbprint != null)
            {
                // 仅信任本例生成的自签证书，避免全局关闭证书验证。
                client.Options.RemoteCertificateValidationCallback = (_, certificate, _, _) => certificate?.GetCertHashString() == thumbprint;
            }

            using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(10)))
            {
                await client.ConnectAsync(new Uri($"{scheme}://localhost:{port}/"), cancellation.Token);
                Assert.Equal(WebSocketState.Open, client.State);
                await client.CloseAsync(WebSocketCloseStatus.NormalClosure, "测试完成", cancellation.Token);
            }
        }
    }

    /// <summary>申请并释放本机临时端口。</summary>
    /// <remarks>Allocates and releases a local ephemeral port.</remarks>
    private static ushort GetAvailablePort()
    {
        using (var listener = new TcpListener(IPAddress.Loopback, 0))
        {
            listener.Start();
            return checked((ushort)((IPEndPoint)listener.LocalEndpoint).Port);
        }
    }
}
