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

using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GameFrameX.StartUp;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// HTTPS 证书加载 fail-fast 校验测试（C271：HttpsPort>0 时 Kestrel HTTPS 真监听的前置校验）。
/// </summary>
/// <remarks>
/// Tests for the HTTPS certificate fail-fast validation (C271: prerequisite check of the real
/// Kestrel HTTPS listening when HttpsPort &gt; 0).
/// </remarks>
public class HttpsCertificateLoadTests : IDisposable
{
    private readonly string _pfxPath;

    public HttpsCertificateLoadTests()
    {
        _pfxPath = Path.Combine(Path.GetTempPath(), $"gfx911-{Guid.NewGuid():N}.pfx");
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=gameframex-https-test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using (var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
            {
                File.WriteAllBytes(_pfxPath, certificate.Export(X509ContentType.Pfx, "test-password"));
            }
        }
    }

    public void Dispose()
    {
        if (File.Exists(_pfxPath))
        {
            File.Delete(_pfxPath);
        }
    }

    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenCertPathIsMissing()
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = string.Empty };

        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));

        Assert.Contains("HttpsCertPath", exception.Message);
    }

    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenCertFileDoesNotExist()
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = "/nonexistent/path/cert.pfx" };

        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));

        Assert.Contains("/nonexistent/path/cert.pfx", exception.Message);
    }

    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenPasswordIsWrong()
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = _pfxPath, HttpsCertPassword = "wrong-password" };

        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));

        Assert.Contains(_pfxPath, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    [Fact]
    public void LoadHttpsCertificate_ShouldReturnCertificate_WhenPfxIsValid()
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = _pfxPath, HttpsCertPassword = "test-password" };

        using (var certificate = AppStartUpBase.LoadHttpsCertificate(setting))
        {
            Assert.NotNull(certificate);
            Assert.Equal("CN=gameframex-https-test", certificate.Subject);
            Assert.True(certificate.HasPrivateKey);
        }
    }
}
