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
using GameFrameX.Foundation.Localization.Core;
using CertificateKeys = GameFrameX.Localization.Keys.StartUp.Certificates;

namespace GameFrameX.StartUp;

/// <summary>
/// 启动期共用的服务器证书加载器。
/// </summary>
/// <remarks>Shared server certificate loader used during startup.</remarks>
internal static class ServerCertificateLoader
{
    /// <summary>
    /// 加载 PFX 或独立证书与私钥组成的 PEM 证书，任一错误立即阻断启动。
    /// </summary>
    /// <remarks>Loads PFX or PEM certificate and key files, failing startup immediately on any error.</remarks>
    /// <param name="protocol">协议名称 / Protocol name</param>
    /// <param name="certificatePath">证书路径 / Certificate path</param>
    /// <param name="keyPath">PEM 私钥路径 / PEM private key path</param>
    /// <param name="password">PFX 或加密私钥密码 / PFX or encrypted private key password</param>
    /// <param name="certificateSettingName">证书配置项名称 / Certificate setting name</param>
    /// <param name="keySettingName">私钥配置项名称 / Private key setting name</param>
    /// <returns>包含私钥的证书 / Certificate with its private key</returns>
    /// <exception cref="InvalidOperationException">配置无效或加载失败 / Invalid configuration or loading failure</exception>
    internal static X509Certificate2 Load(string protocol, string certificatePath, string keyPath, string password,
        string certificateSettingName, string keySettingName)
    {
        if (string.IsNullOrWhiteSpace(certificatePath))
        {
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.PathMissing, protocol, certificateSettingName));
        }

        if (!File.Exists(certificatePath))
        {
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.FileNotFound, protocol, certificatePath));
        }

        // PEM 扩展名明确要求独立私钥，避免漏配时被当作 PFX 或从证书文件隐式读取私钥。
        var extension = Path.GetExtension(certificatePath);
        var isPem = extension.Equals(".pem", StringComparison.OrdinalIgnoreCase) || extension.Equals(".crt", StringComparison.OrdinalIgnoreCase);
        if (isPem && string.IsNullOrWhiteSpace(keyPath))
        {
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.KeyPathMissing, protocol, keySettingName));
        }

        if (isPem && !File.Exists(keyPath))
        {
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.KeyFileNotFound, protocol, keyPath));
        }

        // 私钥文本只读取一次：传统加密格式识别在 try 外完成，避免其专属错误被 LoadFailed 双重包装掩盖。
        var pemKeyText = isPem ? File.ReadAllText(keyPath) : null;
        if (isPem && !IsPkcs8EncryptedKey(pemKeyText) && IsTraditionalOpenSslEncryptedKey(pemKeyText))
        {
            // .NET 仅支持 RFC 7468 加密 PKCS#8（BEGIN ENCRYPTED PRIVATE KEY）；
            // 传统 OpenSSL 加密私钥（Proc-Type/DEK-Info 头）交给 CreateFromPemFile 只会得到误导性加载失败，
            // 这里明确拒绝并给出转换指引。
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.TraditionalEncryptedKeyUnsupported, protocol, keyPath));
        }

        X509Certificate2 loadedCertificate;
        try
        {
            loadedCertificate = isPem
                ? LoadPemCertificate(certificatePath, keyPath, password, pemKeyText)
                : X509CertificateLoader.LoadPkcs12FromFile(certificatePath, password, X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits.Defaults);
        }
        catch (Exception exception)
        {
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.LoadFailed, protocol, certificatePath), exception);
        }

        // 私钥校验放在 try/catch 之外：PrivateKeyMissing 是独立的配置结论，
        // 被 LoadFailed 包装会让该消息不可达且误导排障方向。
        if (!loadedCertificate.HasPrivateKey)
        {
            loadedCertificate.Dispose();
            throw new InvalidOperationException(LocalizationService.GetString(CertificateKeys.PrivateKeyMissing, protocol, certificatePath));
        }

        return loadedCertificate;
    }

    /// <summary>
    /// 判断私钥文本是否为 RFC 7468 加密 PKCS#8 格式。
    /// </summary>
    /// <remarks>Determines whether the key text is an RFC 7468 encrypted PKCS#8 PEM.</remarks>
    /// <param name="keyText">私钥文件文本 / Private key file text</param>
    /// <returns>是加密 PKCS#8 返回 true / Returns true for encrypted PKCS#8</returns>
    private static bool IsPkcs8EncryptedKey(string keyText)
    {
        return keyText.Contains("-----BEGIN ENCRYPTED PRIVATE KEY-----", StringComparison.Ordinal);
    }

    /// <summary>
    /// 判断私钥文本是否为传统 OpenSSL 加密格式（Proc-Type/DEK-Info 头）。
    /// </summary>
    /// <remarks>Determines whether the key text uses the legacy OpenSSL encrypted format (Proc-Type/DEK-Info headers).</remarks>
    /// <param name="keyText">私钥文件文本 / Private key file text</param>
    /// <returns>是传统加密格式返回 true / Returns true for the legacy encrypted format</returns>
    private static bool IsTraditionalOpenSslEncryptedKey(string keyText)
    {
        return keyText.Contains("Proc-Type:", StringComparison.Ordinal) || keyText.Contains("DEK-Info:", StringComparison.Ordinal);
    }

    /// <summary>
    /// 加载 PEM 并转换为带持久私钥关联的证书，兼容 Windows SChannel。
    /// </summary>
    /// <remarks>Loads PEM and converts it to a certificate with a persisted key association for Windows SChannel.</remarks>
    /// <param name="certificatePath">PEM 证书路径 / PEM certificate path</param>
    /// <param name="keyPath">PEM 私钥路径 / PEM private key path</param>
    /// <param name="password">加密私钥密码 / Encrypted private key password</param>
    /// <param name="keyText">已读取的私钥文件文本 / The already-read private key file text</param>
    /// <returns>重载后的证书 / Reloaded certificate</returns>
    private static X509Certificate2 LoadPemCertificate(string certificatePath, string keyPath, string password, string keyText)
    {
        // 根据私钥头识别是否加密，允许未加密私钥使用空密码或已有密码配置。
        using (var certificate = IsPkcs8EncryptedKey(keyText)
            ? X509Certificate2.CreateFromEncryptedPemFile(certificatePath, password.AsSpan(), keyPath)
            : X509Certificate2.CreateFromPemFile(certificatePath, keyPath))
        {
            var certificateBytes = certificate.Export(X509ContentType.Pfx);
            try
            {
                // 不使用 EphemeralKeySet：SChannel 无法使用 PEM 默认产生的临时私钥。
                return X509CertificateLoader.LoadPkcs12(certificateBytes, null, X509KeyStorageFlags.DefaultKeySet, Pkcs12LoaderLimits.Defaults);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(certificateBytes);
            }
        }
    }
}
