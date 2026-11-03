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

namespace GameFrameX.Tests.StartUp;

/// <summary>
/// 证书加载与握手测试使用的临时自签证书文件。
/// </summary>
/// <remarks>Temporary self-signed certificate files for loading and handshake tests.</remarks>
internal sealed class TestServerCertificateFiles : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"gfx987-{Guid.NewGuid():N}");

    /// <summary>测试密码。</summary>
    /// <remarks>Test password.</remarks>
    internal const string Password = "test-password";

    /// <summary>PFX 文件路径。</summary>
    /// <remarks>PFX file path.</remarks>
    internal string CertificatePath => Path.Combine(_directory, "certificate.pfx");

    /// <summary>PEM 证书路径。</summary>
    /// <remarks>PEM certificate path.</remarks>
    internal string PemPath => Path.Combine(_directory, "fullchain.pem");

    /// <summary>大写 CRT 扩展名证书路径。</summary>
    /// <remarks>Certificate path with an uppercase CRT extension.</remarks>
    internal string CrtPath => Path.Combine(_directory, "certificate.CRT");

    /// <summary>未加密私钥路径。</summary>
    /// <remarks>Unencrypted private key path.</remarks>
    internal string KeyPath => Path.Combine(_directory, "privkey.pem");

    /// <summary>加密私钥路径。</summary>
    /// <remarks>Encrypted private key path.</remarks>
    internal string EncryptedKeyPath => Path.Combine(_directory, "encrypted-key.pem");

    /// <summary>不含私钥的 PFX 路径。</summary>
    /// <remarks>PFX path without a private key.</remarks>
    internal string PublicCertificatePath => Path.Combine(_directory, "public.pfx");

    /// <summary>构造具有 localhost 身份的测试证书。</summary>
    /// <remarks>Creates a test certificate for localhost.</remarks>
    internal TestServerCertificateFiles()
    {
        Directory.CreateDirectory(_directory);
        using (var privateKey = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", privateKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var names = new SubjectAlternativeNameBuilder();
            names.AddDnsName("localhost");
            names.AddIpAddress(System.Net.IPAddress.Loopback);
            request.CertificateExtensions.Add(names.Build());
            using (var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1)))
            {
                File.WriteAllBytes(CertificatePath, certificate.Export(X509ContentType.Pfx, Password));
                File.WriteAllText(PemPath, certificate.ExportCertificatePem());
                File.WriteAllText(CrtPath, certificate.ExportCertificatePem());
                File.WriteAllText(KeyPath, privateKey.ExportPkcs8PrivateKeyPem());
                File.WriteAllText(EncryptedKeyPath, privateKey.ExportEncryptedPkcs8PrivateKeyPem(Password,
                    new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 1000)));
                using (var publicCertificate = X509CertificateLoader.LoadCertificate(certificate.RawData))
                {
                    File.WriteAllBytes(PublicCertificatePath, publicCertificate.Export(X509ContentType.Pfx, Password));
                }
            }
        }
    }

    /// <summary>删除所有临时证书与私钥文件。</summary>
    /// <remarks>Deletes all temporary certificate and private key files.</remarks>
    public void Dispose()
    {
        Directory.Delete(_directory, true);
    }
}
