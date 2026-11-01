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


using System.Collections.Concurrent;
using System.Text;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Hash;
using GameFrameX.Foundation.Http.Normalization;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Utility;
using GameFrameX.Network.Messages;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Network.HTTP;

/// <summary>
/// 基础 HTTP 处理器，用于处理 HTTP 请求的基础逻辑。
/// </summary>
/// <remarks>
/// Base HTTP handler for processing HTTP requests.
/// Provides signature validation and common request handling functionality.
/// </remarks>
public abstract class BaseHttpHandler : IHttpHandler
{
    /// <summary>
    /// 签名有效期窗口（分钟）：timestamp 与当前时间差超过该窗口即拒绝。
    /// </summary>
    /// <remarks>
    /// Signature validity window in minutes: a timestamp whose difference from now exceeds this window is rejected.
    /// </remarks>
    protected virtual int SignValidityWindowMinutes
    {
        get { return 5; }
    }

    /// <summary>
    /// 签名密钥最小建议长度（字符）：低于该长度打一次性弱密钥告警（不阻断请求）。
    /// </summary>
    /// <remarks>
    /// Recommended minimum secret length in characters; a one-time warning is logged below it (requests are not blocked).
    /// </remarks>
    protected const int SignSecretMinimumLength = 32;

    /// <summary>
    /// 弱密钥告警只打一次，避免每请求刷日志。
    /// </summary>
    private static bool _weakSecretWarningLogged;

    /// <summary>
    /// v1 口径弃用告警只打一次。
    /// </summary>
    private static bool _signVersionOneDeprecatedWarningLogged;

    /// <summary>
    /// nonce 重放缓存：key = nonce，value = 过期时间（unix 秒）。窗口外的条目在清理时移除。
    /// </summary>
    private static readonly ConcurrentDictionary<string, long> NonceCache = new ConcurrentDictionary<string, long>();

    /// <summary>
    /// 获取是否需要校验签名，默认为不需要校验。
    /// </summary>
    /// <remarks>
    /// Gets whether signature validation is required, defaults to no validation unless enabled globally or by attribute.
    /// </remarks>
    /// <value>如果需要校验签名则为 <c>true</c>；否则为 <c>false</c> / <c>true</c> if signature validation is required; otherwise <c>false</c></value>
    public virtual bool IsCheckSign
    {
        get { return GlobalSettings.CurrentSetting?.HttpRequireSign == true || Attribute.IsDefined(GetType(), typeof(RequireHttpSignatureAttribute), true); }
    }


    /// <summary>
    /// 处理 HTTP 请求的异步操作，返回字符串结果。
    /// </summary>
    /// <remarks>
    /// Asynchronously processes HTTP request and returns string result.
    /// </remarks>
    /// <param name="context">HTTP 处理管线统一请求上下文 / Unified request context for the HTTP handling pipeline</param>
    /// <returns>处理结果的字符串 / String result of the processing</returns>
    public virtual Task<string> ActionAsync(HttpActionContext context)
    {
        throw new NotImplementedException();
    }


    /// <summary>
    /// 处理 HTTP 请求的异步操作，返回 <see cref="MessageObject"/> 对象。
    /// </summary>
    /// <remarks>
    /// Asynchronously processes HTTP request and returns <see cref="MessageObject"/> result.
    /// </remarks>
    /// <param name="context">HTTP 处理管线统一请求上下文 / Unified request context for the HTTP handling pipeline</param>
    /// <returns>处理结果的 <see cref="MessageObject"/> 对象 / <see cref="MessageObject"/> result of the processing</returns>
    public virtual Task<MessageObject> ActionMessageObjectAsync(HttpActionContext context)
    {
        throw new NotImplementedException();
    }

    /// <summary>
    /// 计算 v1 口径请求签名：Base64(HMAC-SHA256(UTF8(timestamp), UTF8(HttpSignSecret)))。
    /// <para>该口径不覆盖命令与参数、无 nonce，不防篡改与重放，仅为兼容存量客户端保留，建议迁移到
    /// <see cref="GetStringSign(string, string, string, Dictionary{string,object})"/>。</para>
    /// </summary>
    /// <remarks>
    /// Computes the legacy v1 signature: Base64(HMAC-SHA256(UTF8(timestamp), UTF8(HttpSignSecret))).
    /// This scheme covers neither command nor parameters and has no nonce; kept only for legacy clients.
    /// </remarks>
    /// <param name="timestamp">unix 秒时间戳字符串 / Unix-second timestamp string</param>
    /// <returns>Base64 编码的签名 / Base64-encoded signature</returns>
    public string GetStringSign(string timestamp)
    {
        return HmacSha256Helper.Hash(timestamp, GlobalSettings.CurrentSetting.HttpSignSecret);
    }

    /// <summary>
    /// 计算 v2 口径请求签名：Base64(HMAC-SHA256(UTF8(canonicalString), UTF8(HttpSignSecret)))。
    /// <para>canonicalString = command + "\n" + timestamp + "\n" + nonce + "\n" + 按 key 排序（排除 sign/timestamp/nonce）的 "k=v" 串。</para>
    /// </summary>
    /// <remarks>
    /// Computes the v2 signature over the full request: Base64(HMAC-SHA256(UTF8(canonicalString), secret)) where
    /// canonicalString = command + "\n" + timestamp + "\n" + nonce + "\n" + sorted "k=v" pairs (sign/timestamp/nonce excluded).
    /// </remarks>
    /// <param name="command">路由命令 / Route command</param>
    /// <param name="timestamp">unix 秒时间戳字符串 / Unix-second timestamp string</param>
    /// <param name="nonce">一次性随机数 / One-time nonce</param>
    /// <param name="paramMap">参与签名的请求参数字典（构造时会排除 sign/timestamp/nonce）/ Request parameters (sign/timestamp/nonce excluded when building the canonical string)</param>
    /// <returns>Base64 编码的签名 / Base64-encoded signature</returns>
    public string GetStringSign(string command, string timestamp, string nonce, Dictionary<string, object> paramMap)
    {
        return HmacSha256Helper.Hash(BuildSignCanonicalString(command, timestamp, nonce, paramMap), GlobalSettings.CurrentSetting.HttpSignSecret);
    }

    /// <summary>
    /// 构造 v2 口径的规范化签名字符串：command + "\n" + timestamp + "\n" + nonce + "\n" + 按 key 排序（排除 sign/timestamp/nonce）的 "k=v" 串。
    /// </summary>
    /// <remarks>
    /// Builds the canonical string for the v2 scheme.
    /// </remarks>
    internal static string BuildSignCanonicalString(string command, string timestamp, string nonce, Dictionary<string, object> paramMap)
    {
        var builder = new StringBuilder();
        builder.Append(command ?? string.Empty).Append('\n');
        builder.Append(timestamp ?? string.Empty).Append('\n');
        builder.Append(nonce ?? string.Empty);

        if (paramMap != null)
        {
            var keys = new List<string>(paramMap.Keys);
            keys.Sort(StringComparer.Ordinal);
            foreach (var key in keys)
            {
                if (key == GlobalConst.HttpSignKey || key == GlobalConst.HttpTimestampKey || key == GlobalConst.HttpNonceKey)
                {
                    continue;
                }

                builder.Append('\n').Append(key).Append('=').Append(paramMap[key]);
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// 校验签名是否有效（command 传空字符串，等价旧调用方式；v2 下空 command 仅匹配同样以空 command 签名的调用方）。
    /// </summary>
    /// <remarks>
    /// Validates the signature with an empty command; see <see cref="CheckSign(string,Dictionary{string,object},out string)"/>.
    /// </remarks>
    /// <param name="paramMap">请求参数字典 / Request parameter dictionary</param>
    /// <param name="error">错误消息 / Error message</param>
    /// <returns>校验是否通过 / Whether validation succeeds</returns>
    public bool CheckSign(Dictionary<string, object> paramMap, out string error)
    {
        return CheckSign(string.Empty, paramMap, out error);
    }

    /// <summary>
    /// 校验签名是否有效。
    /// <para>HttpSignVersion=1（默认，兼容）：sign = Base64(HMAC-SHA256(UTF8(timestamp), secret))，固定时间比较；首次使用打一次性弃用告警。</para>
    /// <para>HttpSignVersion=2：sign 覆盖 command + timestamp + nonce + 排序参数，nonce 在时间窗内一次性有效（防篡改、防重放）。</para>
    /// <para>时间窗为双向校验：timestamp 与当前时间差（任一方向）超过窗口即拒绝，未来时间戳不再放行。</para>
    /// <para>密钥未配置（空白）时 fail-closed：一律拒绝并记录错误日志，绝不放行。</para>
    /// </summary>
    /// <remarks>
    /// Validates the signature.
    /// Version 1 (default, legacy): sign = Base64(HMAC-SHA256(UTF8(timestamp), secret)), constant-time compare, one-time deprecation warning.
    /// Version 2: the signature covers command + timestamp + nonce + sorted parameters, and the nonce is single-use within the window.
    /// The timestamp window is bidirectional: any direction beyond the window is rejected.
    /// Fail-closed when the secret is blank: every request is rejected with an error log, none is let through.
    /// </remarks>
    /// <param name="command">路由命令（v2 参与签名）/ Route command (signed in v2)</param>
    /// <param name="paramMap">请求参数字典 / Request parameter dictionary</param>
    /// <param name="error">错误消息，如果校验失败则返回具体的错误信息 / Error message, returns specific error information if validation fails</param>
    /// <returns>如果校验成功则返回 <c>true</c>；否则返回 <c>false</c> / <c>true</c> if validation succeeds; otherwise <c>false</c></returns>
    public bool CheckSign(string command, Dictionary<string, object> paramMap, out string error)
    {
        error = string.Empty;
        if (!IsCheckSign)
        {
            // 不需要校验签名
            return true;
        }

        // 内部验证
        if (!paramMap.ContainsKey(GlobalConst.HttpSignKey) || !paramMap.ContainsKey(GlobalConst.HttpTimestampKey))
        {
            error = HttpJsonResultData<string>.ValidationErrorString();
            return false;
        }

        var secret = GlobalSettings.CurrentSetting?.HttpSignSecret;
        if (secret.IsNullOrWhiteSpace())
        {
            // 密钥未配置：fail-closed，开启校验却无密钥时绝不放行
            // Localization: Network.Http.SignSecretMissing - HTTP签名校验已开启，但 HttpSignSecret 未配置，请求一律拒绝
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.NetworkHttp.SignSecretMissing));
            error = HttpJsonResultData<string>.ValidationErrorString();
            return false;
        }

        // 弱密钥一次性告警（不阻断请求，fail-closed 只防“空”，告警提示“弱”）
        if (secret.Length < SignSecretMinimumLength && !_weakSecretWarningLogged)
        {
            _weakSecretWarningLogged = true;
            // Localization: Network.Http.SignSecretTooWeak - HttpSignSecret 长度不足 {0} 字符，存在被离线爆破风险，请更换为高强度随机密钥
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.NetworkHttp.SignSecretTooWeak, SignSecretMinimumLength));
        }

        var sign = paramMap[GlobalConst.HttpSignKey].ToString();
        var time = paramMap[GlobalConst.HttpTimestampKey].ToString();

        // 时间戳必须可解析为 unix 秒：解析失败显式拒绝，不再依赖 1970 兜底（毫秒时间戳会落入双向窗口拒绝）
        if (!long.TryParse(time, out var timeTick))
        {
            error = HttpJsonResultData<string>.IllegalString();
            return false;
        }

        // 双向时间窗：过去或未来超出窗口均拒绝。
        // 直接用 unix 秒做差值比较，避免经 TimerHelper 转 DateTime 时毫秒级时间戳溢出抛异常（会被顶层 catch 归为服务器错误）。
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var differenceSeconds = Math.Abs(nowUnixSeconds - timeTick);
        if (differenceSeconds > SignValidityWindowMinutes * 60)
        {
            error = HttpJsonResultData<string>.IllegalString();
            return false;
        }

        if (GlobalSettings.CurrentSetting?.HttpSignVersion >= 2)
        {
            return CheckSignVersionTwo(command, paramMap, secret, sign, time, out error);
        }

        if (!_signVersionOneDeprecatedWarningLogged)
        {
            _signVersionOneDeprecatedWarningLogged = true;
            // Localization: Network.Http.SignVersionOneDeprecated - HTTP 签名仍使用 v1（仅时间戳）旧口径，不防篡改与重放，建议尽快迁移到 HttpSignVersion=2
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.NetworkHttp.SignVersionOneDeprecated));
        }

        if (HmacSha256Helper.Verify(time, secret, sign))
        {
            return true;
        }

        error = HttpJsonResultData<string>.ValidationErrorString();
        return false;
    }

    /// <summary>
    /// v2 口径验签：全要素签名 + nonce 一次性使用（时间窗校验已完成）。
    /// </summary>
    /// <remarks>
    /// Version-2 validation: full-element signature plus one-time nonce usage (the timestamp window is already checked by the caller).
    /// </remarks>
    private bool CheckSignVersionTwo(string command, Dictionary<string, object> paramMap, string secret, string sign, string timestamp, out string error)
    {
        if (!paramMap.TryGetValue(GlobalConst.HttpNonceKey, out var nonceValue) || nonceValue == null || nonceValue.ToString().IsNullOrWhiteSpace())
        {
            // Localization: Network.Http.SignNonceInvalid - HTTP 签名 nonce 缺失或已在时间窗内使用（疑似重放），请求已拒绝
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.NetworkHttp.SignNonceInvalid));
            error = HttpJsonResultData<string>.ValidationErrorString();
            return false;
        }

        var nonce = nonceValue.ToString();
        var canonicalString = BuildSignCanonicalString(command, timestamp, nonce, paramMap);
        if (!HmacSha256Helper.Verify(canonicalString, secret, sign))
        {
            error = HttpJsonResultData<string>.ValidationErrorString();
            return false;
        }

        // nonce 一次性使用：时间窗内重复即判定重放并拒绝
        var nowUnixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var nonceExpiryUnixSeconds = nowUnixSeconds + SignValidityWindowMinutes * 60;
        if (NonceCache.TryGetValue(nonce, out var existingExpiry) && existingExpiry > nowUnixSeconds)
        {
            // Localization: Network.Http.SignNonceInvalid - HTTP 签名 nonce 缺失或已在时间窗内使用（疑似重放），请求已拒绝
            LogHelper.Warning(LocalizationService.GetString(Localization.Keys.NetworkHttp.SignNonceInvalid));
            error = HttpJsonResultData<string>.IllegalString();
            return false;
        }

        NonceCache[nonce] = nonceExpiryUnixSeconds;
        CleanupExpiredNonces(nowUnixSeconds);

        error = string.Empty;
        return true;
    }

    /// <summary>
    /// 清理已过期的 nonce 条目，避免缓存无限增长；仅在条目数超过阈值时执行。
    /// </summary>
    /// <remarks>
    /// Removes expired nonce entries once the cache grows beyond a threshold, keeping it bounded.
    /// </remarks>
    private static void CleanupExpiredNonces(long nowUnixSeconds)
    {
        if (NonceCache.Count <= 1024)
        {
            return;
        }

        foreach (var pair in NonceCache)
        {
            if (pair.Value <= nowUnixSeconds)
            {
                NonceCache.TryRemove(pair.Key, out _);
            }
        }
    }
}
