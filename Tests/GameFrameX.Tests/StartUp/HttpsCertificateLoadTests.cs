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

using System.Security.Cryptography.X509Certificates;
using GameFrameX.StartUp;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// HTTPS 证书启动期加载与失败矩阵。
/// </summary>
/// <remarks>Startup loading and failure matrix for HTTPS certificates.</remarks>
public class HttpsCertificateLoadTests : IDisposable
{
    private TestServerCertificateFiles _files;

    // 路径校验用例不依赖证书生成，仅在需要真实证书时创建临时文件。
    private TestServerCertificateFiles Files => _files ??= new TestServerCertificateFiles();

    /// <summary>清理临时证书文件。</summary>
    /// <remarks>Cleans up temporary certificate files.</remarks>
    public void Dispose()
    {
        _files?.Dispose();
    }

    /// <summary>空白证书路径必须阻断启动。</summary>
    /// <remarks>Blank certificate paths must fail startup.</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void LoadHttpsCertificate_ShouldThrow_WhenCertificatePathIsMissing(string path)
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = path };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains("HttpsCertPath", exception.Message);
    }

    /// <summary>不存在的证书路径必须阻断启动。</summary>
    /// <remarks>Missing certificate files must fail startup.</remarks>
    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenCertificateFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx987-{Guid.NewGuid():N}.missing");
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = path };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains(path, exception.Message);
    }

    /// <summary>PFX 密码错误必须阻断启动并保留底层异常。</summary>
    /// <remarks>Incorrect PFX passwords must fail startup and retain the underlying exception.</remarks>
    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenPasswordIsWrong()
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = Files.CertificatePath, HttpsCertPassword = "wrong-password" };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains(Files.CertificatePath, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    /// <summary>有效 PFX 应保留证书身份与私钥。</summary>
    /// <remarks>Valid PFX must retain the certificate identity and private key.</remarks>
    [Fact]
    public void LoadHttpsCertificate_ShouldReturnCertificate_WhenPfxIsValid()
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = Files.CertificatePath, HttpsCertPassword = TestServerCertificateFiles.Password };
        using (var certificate = AppStartUpBase.LoadHttpsCertificate(setting))
        {
            AssertCertificate(certificate);
        }
    }

    /// <summary>PEM 支持两种扩展名以及加密与未加密私钥。</summary>
    /// <remarks>PEM supports both extensions with encrypted and unencrypted private keys.</remarks>
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void LoadHttpsCertificate_ShouldReturnCertificate_WhenPemIsValid(bool encrypted, bool useCrtExtension)
    {
        var setting = new AppSetting
        {
            HttpsPort = 8443,
            HttpsCertPath = useCrtExtension ? Files.CrtPath : Files.PemPath,
            HttpsCertificateKeyPath = encrypted ? Files.EncryptedKeyPath : Files.KeyPath,
            HttpsCertPassword = encrypted ? TestServerCertificateFiles.Password : string.Empty,
        };
        using (var certificate = AppStartUpBase.LoadHttpsCertificate(setting))
        {
            AssertCertificate(certificate);
        }
    }

    /// <summary>PEM 必须显式配置独立私钥路径。</summary>
    /// <remarks>PEM requires an explicitly configured separate private key path.</remarks>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void LoadHttpsCertificate_ShouldThrow_WhenPemKeyPathIsMissing(string keyPath)
    {
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = Files.PemPath, HttpsCertificateKeyPath = keyPath };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains("HttpsCertificateKeyPath", exception.Message);
    }

    /// <summary>私钥文件不存在时必须阻断启动。</summary>
    /// <remarks>Missing private key files must fail startup.</remarks>
    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenPemKeyFileDoesNotExist()
    {
        var keyPath = Files.KeyPath + ".missing";
        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = Files.PemPath, HttpsCertificateKeyPath = keyPath };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains(keyPath, exception.Message);
    }

    /// <summary>加密私钥密码错误或缺失时必须阻断启动。</summary>
    /// <remarks>Incorrect or missing encrypted private key passwords must fail startup.</remarks>
    [Theory]
    [InlineData("wrong-password")]
    [InlineData("")]
    [InlineData(null)]
    public void LoadHttpsCertificate_ShouldThrow_WhenPemKeyPasswordIsWrong(string password)
    {
        var setting = new AppSetting
        {
            HttpsPort = 8443, HttpsCertPath = Files.PemPath,
            HttpsCertificateKeyPath = Files.EncryptedKeyPath, HttpsCertPassword = password,
        };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains(Files.PemPath, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    /// <summary>损坏证书或不含私钥的 PFX 必须阻断启动。</summary>
    /// <remarks>Corrupted certificates and PFX without private keys must fail startup.</remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadHttpsCertificate_ShouldThrow_WhenCertificateIsUnusable(bool publicOnly)
    {
        var path = publicOnly ? Files.PublicCertificatePath : Files.CertificatePath;
        if (!publicOnly)
        {
            File.WriteAllText(path, "invalid-certificate");
        }

        var setting = new AppSetting { HttpsPort = 8443, HttpsCertPath = path, HttpsCertPassword = TestServerCertificateFiles.Password };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        Assert.Contains(path, exception.Message);
        if (publicOnly)
        {
            // 无私钥属独立配置结论：直接抛 PrivateKeyMissing，不被 LoadFailed 包装（无底层异常）。
            Assert.Null(exception.InnerException);
        }
        else
        {
            // 加载失败保留底层异常便于排障。
            Assert.NotNull(exception.InnerException);
        }
    }

    /// <summary>传统 OpenSSL 加密私钥必须被明确拒绝并给出转换指引。</summary>
    /// <remarks>Legacy OpenSSL encrypted keys must be rejected with a conversion hint.</remarks>
    [Fact]
    public void LoadHttpsCertificate_ShouldThrow_WhenPemKeyUsesTraditionalOpenSslEncryption()
    {
        var setting = new AppSetting
        {
            HttpsPort = 8443,
            HttpsCertPath = Files.PemPath,
            HttpsCertificateKeyPath = Files.TraditionalEncryptedKeyPath,
            HttpsCertPassword = TestServerCertificateFiles.Password,
        };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadHttpsCertificate(setting));
        // 传统格式专属错误在加载前抛出：不被 LoadFailed 包装，且给出 openssl pkcs8 转换指引（命令文本不随语言变化）。
        Assert.Contains(Files.TraditionalEncryptedKeyPath, exception.Message);
        Assert.Contains("openssl pkcs8 -topk8", exception.Message);
        Assert.Null(exception.InnerException);
    }

    /// <summary>验证身份与可用私钥。</summary>
    /// <remarks>Checks certificate identity and private key availability.</remarks>
    private static void AssertCertificate(X509Certificate2 certificate)
    {
        Assert.Equal("CN=localhost", certificate.Subject);
        Assert.True(certificate.HasPrivateKey);
        using (var privateKey = certificate.GetRSAPrivateKey())
        {
            Assert.NotNull(privateKey);
        }
    }
}
