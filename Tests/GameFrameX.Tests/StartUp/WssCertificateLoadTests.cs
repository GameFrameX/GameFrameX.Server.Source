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
/// WSS 证书启动期加载与失败矩阵。
/// </summary>
/// <remarks>Startup loading and failure matrix for WSS certificates.</remarks>
public class WssCertificateLoadTests : IDisposable
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
    public void LoadWssCertificate_ShouldThrow_WhenCertificatePathIsMissing(string path)
    {
        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = path };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains("WssCertFilePath", exception.Message);
    }

    /// <summary>不存在的证书路径必须阻断启动。</summary>
    /// <remarks>Missing certificate files must fail startup.</remarks>
    [Fact]
    public void LoadWssCertificate_ShouldThrow_WhenCertificateFileDoesNotExist()
    {
        var path = Path.Combine(Path.GetTempPath(), $"gfx987-{Guid.NewGuid():N}.missing");
        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = path };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(path, exception.Message);
    }

    /// <summary>PFX 密码错误必须阻断启动并保留底层异常。</summary>
    /// <remarks>Incorrect PFX passwords must fail startup and retain the underlying exception.</remarks>
    [Fact]
    public void LoadWssCertificate_ShouldThrow_WhenPasswordIsWrong()
    {
        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = Files.CertificatePath, WssCertificatePassword = "wrong-password" };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(Files.CertificatePath, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    /// <summary>有效 PFX 应保留证书身份与私钥。</summary>
    /// <remarks>Valid PFX must retain the certificate identity and private key.</remarks>
    [Fact]
    public void LoadWssCertificate_ShouldReturnCertificate_WhenPfxIsValid()
    {
        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = Files.CertificatePath, WssCertificatePassword = TestServerCertificateFiles.Password };
        using (var certificate = AppStartUpBase.LoadWssCertificate(setting))
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
    public void LoadWssCertificate_ShouldReturnCertificate_WhenPemIsValid(bool encrypted, bool useCrtExtension)
    {
        var setting = new AppSetting
        {
            WssPort = 8443,
            WssCertFilePath = useCrtExtension ? Files.CrtPath : Files.PemPath,
            WssCertificateKeyPath = encrypted ? Files.EncryptedKeyPath : Files.KeyPath,
            WssCertificatePassword = encrypted ? TestServerCertificateFiles.Password : string.Empty,
        };
        using (var certificate = AppStartUpBase.LoadWssCertificate(setting))
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
    public void LoadWssCertificate_ShouldThrow_WhenPemKeyPathIsMissing(string keyPath)
    {
        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = Files.PemPath, WssCertificateKeyPath = keyPath };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains("WssCertificateKeyPath", exception.Message);
    }

    /// <summary>私钥文件不存在时必须阻断启动。</summary>
    /// <remarks>Missing private key files must fail startup.</remarks>
    [Fact]
    public void LoadWssCertificate_ShouldThrow_WhenPemKeyFileDoesNotExist()
    {
        var keyPath = Files.KeyPath + ".missing";
        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = Files.PemPath, WssCertificateKeyPath = keyPath };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(keyPath, exception.Message);
    }

    /// <summary>加密私钥密码错误或缺失时必须阻断启动。</summary>
    /// <remarks>Incorrect or missing encrypted private key passwords must fail startup.</remarks>
    [Theory]
    [InlineData("wrong-password")]
    [InlineData("")]
    [InlineData(null)]
    public void LoadWssCertificate_ShouldThrow_WhenPemKeyPasswordIsWrong(string password)
    {
        var setting = new AppSetting
        {
            WssPort = 8443, WssCertFilePath = Files.PemPath,
            WssCertificateKeyPath = Files.EncryptedKeyPath, WssCertificatePassword = password,
        };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(Files.PemPath, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    /// <summary>损坏证书或不含私钥的 PFX 必须阻断启动。</summary>
    /// <remarks>Corrupted certificates and PFX without private keys must fail startup.</remarks>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LoadWssCertificate_ShouldThrow_WhenCertificateIsUnusable(bool publicOnly)
    {
        var path = publicOnly ? Files.PublicCertificatePath : Files.CertificatePath;
        if (!publicOnly)
        {
            File.WriteAllText(path, "invalid-certificate");
        }

        var setting = new AppSetting { WssPort = 8443, WssCertFilePath = path, WssCertificatePassword = TestServerCertificateFiles.Password };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(path, exception.Message);
        Assert.NotNull(exception.InnerException);
    }

    /// <summary>HTTPS 配置完整也不得替代缺失的 WSS 配置。</summary>
    /// <remarks>Valid HTTPS settings must never replace missing WSS settings.</remarks>
    [Fact]
    public void LoadWssCertificate_ShouldNotFallBackToHttpsConfiguration()
    {
        var setting = new AppSetting
        {
            WssPort = 8443, HttpsCertPath = Files.CertificatePath,
            HttpsCertPassword = TestServerCertificateFiles.Password,
            HttpsCertificateKeyPath = Files.KeyPath,
        };
        var exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(nameof(AppSetting.WssCertFilePath), exception.Message);

        setting.WssCertFilePath = Files.PemPath;
        exception = Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
        Assert.Contains(nameof(AppSetting.WssCertificateKeyPath), exception.Message);

        setting.WssCertificateKeyPath = Files.EncryptedKeyPath;
        Assert.Throws<InvalidOperationException>(() => AppStartUpBase.LoadWssCertificate(setting));
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
