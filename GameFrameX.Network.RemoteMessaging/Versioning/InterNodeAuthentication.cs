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

using System.Security.Cryptography;
using System.Text;

namespace GameFrameX.Network.RemoteMessaging.Versioning;

/// <summary>
/// 节点间 RPC 鉴权——进程级共享密钥配置与握手凭据的 HMAC 计算与校验。
/// </summary>
/// <remarks>
/// Inter-node RPC authentication: the process-wide shared-secret configuration plus HMAC
/// computation and verification for handshake credentials.
/// 密钥经 <see cref="Configure"/> 在启动期由宿主注入（单一事实源：<c>AppSetting.InterNodeAuthSecret</c>），
/// 服务端漏斗握手短路、服务器间 KCP 专用监听的凭据闸门与拨号端握手帧三方共用本配置。
/// 密钥为空（缺省）表示不启用鉴权：<see cref="Verify"/> 恒返回 <c>true</c>，全部链路行为与未引入鉴权前等价
/// （opt-in；存量部署零破坏）。启用后凭据 = HMAC-SHA256(secret, nonce|major|minor) 十六进制小写，
/// nonce 由拨号端每连接随机生成，验签使用固定时间比较。
/// 威胁模型为内网横向无凭据接入（纵深防御一层），不承诺抗内网窃听重放；如需更强姿态应叠加 TLS。
/// </remarks>
public static class InterNodeAuthentication
{
    /// <summary>
    /// 进程级共享密钥（启动期注入后只读；string 引用读写原子，读侧无锁安全）。
    /// </summary>
    private static string _sharedSecret = string.Empty;

    /// <summary>
    /// 获取是否已启用节点间鉴权（密钥非空即启用）。
    /// </summary>
    public static bool IsEnabled
    {
        get { return _sharedSecret.Length > 0; }
    }

    /// <summary>
    /// 配置节点间鉴权共享密钥（空串或 null = 关闭鉴权）。集群内所有节点必须配置同一密钥。
    /// </summary>
    /// <param name="sharedSecret">共享密钥 / The shared secret</param>
    public static void Configure(string sharedSecret)
    {
        _sharedSecret = sharedSecret ?? string.Empty;
    }

    /// <summary>
    /// 生成拨号端每连接一次性随机 nonce。
    /// </summary>
    /// <returns>32 位十六进制随机串 / A 32-hex-digit random string</returns>
    public static string CreateNonce()
    {
        return Guid.NewGuid().ToString("N");
    }

    /// <summary>
    /// 计算握手凭据：HMAC-SHA256(secret, nonce|major|minor) 十六进制小写。
    /// 未启用鉴权时返回空串（握手帧不带凭据字段语义）。
    /// </summary>
    /// <param name="nonce">每连接随机 nonce / The per-connection nonce</param>
    /// <param name="major">本端协议 Major 版本 / The local protocol major version</param>
    /// <param name="minor">本端协议 Minor 版本 / The local protocol minor version</param>
    /// <returns>十六进制小写凭据 / The lowercase hex credential</returns>
    public static string ComputeCredential(string nonce, int major, int minor)
    {
        if (!IsEnabled)
        {
            return string.Empty;
        }

        var payload = Encoding.UTF8.GetBytes(nonce + "|" + major.ToString() + "|" + minor.ToString());
        using (var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_sharedSecret)))
        {
            var hash = hmac.ComputeHash(payload);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }
    }

    /// <summary>
    /// 校验握手凭据（固定时间比较）。
    /// </summary>
    /// <remarks>
    /// 未启用鉴权时恒返回 <c>true</c>（是否在「无凭据」状态下放行由调用方语义决定：
    /// 服务端握手短路对「发起握手的连接」验签；服务器间 KCP 闸门在启用鉴权时对全部连接强制）。
    /// 启用鉴权时 nonce 或凭据缺失、或凭据不匹配均返回 <c>false</c>。
    /// </remarks>
    /// <param name="nonce">拨号端随机 nonce / The dialing side's nonce</param>
    /// <param name="major">拨号端协议 Major 版本 / The dialing side's protocol major version</param>
    /// <param name="minor">拨号端协议 Minor 版本 / The dialing side's protocol minor version</param>
    /// <param name="credential">拨号端凭据 / The dialing side's credential</param>
    /// <returns>校验通过返回 <c>true</c> / true when the credential verifies</returns>
    public static bool Verify(string nonce, int major, int minor, string credential)
    {
        if (!IsEnabled)
        {
            return true;
        }

        if (string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(credential))
        {
            return false;
        }

        var expected = ComputeCredential(nonce, major, minor);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(credential));
    }

    /// <summary>
    /// 重置为未配置状态（测试缝）。
    /// </summary>
    internal static void Reset()
    {
        _sharedSecret = string.Empty;
    }
}
