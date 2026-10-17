// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace GameFrameX.Capability.Online.Operations.HotfixRollback;

/// <summary>
/// 登记期制品获取器（C248 生产实现，BCL <see cref="HttpClient"/> + SHA-256，不引 Core）：
/// 下载 → SHA-256 校验 → 同卷原子落盘至 <c>{CWD}/{Version}/GameFrameX.Hotfix.dll</c>。
/// <para>
/// 维护约束（红线）：
/// ① <b>落盘契约镜像义务</b>——版本目录拼装规则必须与 <c>GameFrameX.Core.Hotfix.HotfixLoadOptions</c>
/// 的版本路径语义逐字节一致（Version 非空整体替代目录段、文件名 <see cref="DefaultAssemblyFileName"/>、
/// 注入防线拒 <c>/</c> <c>\</c> <c>:</c> 与 <c>..</c> 段）；Core 侧规则变更时本常量与防线须同步修订；
/// ② <b>原子性与失败清理</b>——先落 <c>{CWD}/{Version}.staging-{guid}/</c>（同卷才能 rename 原子化），
/// 校验通过后 <see cref="File.Move(string, string)"/> 至版本目录；任何失败路径 finally 清空 staging（零残留），
/// 绝不把半成品暴露给装载路径；
/// ③ <b>幂等与不可变更</b>——版本目录已有 checksum 一致的制品直接通过（不重复下载）；
/// 不一致即拒绝（版本目录一经落盘不可变更，防制品被悄悄替换）；
/// ④ <b>只在登记期联网</b>——本类是全链路唯一发起制品下载的位置；切换执行器路径静态无网络 IO。
/// </para>
/// </summary>
public sealed class OnlineHotfixArtifactFetcher : IOnlineHotfixArtifactFetcher
{
    /// <summary>
    /// 制品文件名（镜像 <c>HotfixLoadOptions.AssemblyFileName</c> 缺省值；C248 R2 裁定 v1 单文件制品不设配置面）。
    /// </summary>
    private const string DefaultAssemblyFileName = "GameFrameX.Hotfix.dll";

    /// <summary>
    /// 版本值禁止出现的路径相关字符（镜像 <c>HotfixLoadOptions</c> 注入防线）。
    /// </summary>
    private static readonly char[] ForbiddenVersionChars = { '/', '\\', ':', };

    /// <summary>
    /// 下载参数（超时 / 重试 / 大小上限）。
    /// </summary>
    private readonly OnlineHotfixArtifactFetchOptions _options;

    /// <summary>
    /// HTTP 客户端（测试经构造器注入桩传输）。
    /// </summary>
    private readonly HttpClient _httpClient;

    /// <summary>
    /// 初始化 <see cref="OnlineHotfixArtifactFetcher"/>。
    /// </summary>
    /// <param name="options">下载参数（null = 缺省值）。</param>
    /// <param name="handler">HTTP 传输（null = 缺省 SocketsHttpHandler；测试注入桩）。</param>
    public OnlineHotfixArtifactFetcher(OnlineHotfixArtifactFetchOptions options = null, HttpMessageHandler handler = null)
    {
        _options = options ?? new OnlineHotfixArtifactFetchOptions();
        _httpClient = new HttpClient(handler ?? new SocketsHttpHandler(), true);
    }

    /// <inheritdoc />
    public async Task EnsureArtifactAsync(string version, string sourceUrl, string artifactSha256, CancellationToken cancellationToken = default)
    {
        ValidateVersion(version);
        var expectedSha256 = NormalizeSha256(artifactSha256);
        ValidateSourceUrl(sourceUrl);
        var targetDirectory = Path.Combine(Environment.CurrentDirectory, version);
        var targetPath = Path.Combine(targetDirectory, DefaultAssemblyFileName);

        // 幂等 / 不可变更：在盘制品一致即通过、不一致即拒绝（本方法任何分支都不改写既有版本目录内容）。
        if (File.Exists(targetPath))
        {
            if (await HashMatchesAsync(targetPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                return;
            }

            throw new InvalidOperationException("目标版本目录已有校验不一致的制品（版本目录一经落盘不可变更）：" + targetPath);
        }

        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            // sha-only 语义 = 校验已在盘制品一致性；未在盘即无物可校（宁拒不静默登记）。
            throw new FileNotFoundException("仅提供校验和但目标制品未落盘（sha-only 语义 = 校验已在盘制品一致性）：" + targetPath, targetPath);
        }

        // staging 与版本目录同在 {CWD} 下（同卷），rename 才是原子操作；GUID 后缀防并发登记互踩。
        var stagingDirectory = Path.Combine(Environment.CurrentDirectory, version + ".staging-" + Guid.NewGuid().ToString("N"));
        var stagingPath = Path.Combine(stagingDirectory, DefaultAssemblyFileName);
        try
        {
            Directory.CreateDirectory(stagingDirectory);
            await DownloadWithRetryAsync(stagingPath, sourceUrl, cancellationToken).ConfigureAwait(false);
            if (!await HashMatchesAsync(stagingPath, expectedSha256, cancellationToken).ConfigureAwait(false))
            {
                throw new InvalidOperationException("制品 SHA-256 校验不符（拒绝落盘）：" + sourceUrl);
            }

            Directory.CreateDirectory(targetDirectory);
            try
            {
                File.Move(stagingPath, targetPath);
            }
            catch (IOException) when (File.Exists(targetPath))
            {
                // 并发登记竞态：另一获取已落盘——按既有内容判幂等，不一致维持拒绝。
                if (!await HashMatchesAsync(targetPath, expectedSha256, cancellationToken).ConfigureAwait(false))
                {
                    throw new InvalidOperationException("目标版本目录已有校验不一致的制品（版本目录一经落盘不可变更）：" + targetPath);
                }
            }
        }
        finally
        {
            DeleteStaging(stagingDirectory);
        }
    }

    /// <summary>
    /// 带重试的下载（总尝试数 = 1 + <see cref="OnlineHotfixArtifactFetchOptions.MaxRetryCount"/>；
    /// 仅网络异常 / 5xx / 408 重试；重试前清掉半成品保持 staging 单一制品语义）。
    /// </summary>
    /// <param name="stagingPath">staging 制品路径。</param>
    /// <param name="sourceUrl">制品下载地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task DownloadWithRetryAsync(string stagingPath, string sourceUrl, CancellationToken cancellationToken)
    {
        var totalAttempts = 1 + Math.Max(0, _options.MaxRetryCount);
        for (var attempt = 1; attempt <= totalAttempts; attempt++)
        {
            try
            {
                await DownloadOnceAsync(stagingPath, sourceUrl, cancellationToken).ConfigureAwait(false);
                return;
            }
            catch (Exception ex) when (attempt < totalAttempts && IsRetryable(ex) && !cancellationToken.IsCancellationRequested)
            {
                TryDeleteFile(stagingPath);
            }
        }
    }

    /// <summary>
    /// 单次下载尝试（单次尝试超时覆盖响应头与响应体全程；Content-Length 预检 + 流式计数双闸限大小）。
    /// </summary>
    /// <param name="stagingPath">staging 制品路径。</param>
    /// <param name="sourceUrl">制品下载地址。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    private async Task DownloadOnceAsync(string stagingPath, string sourceUrl, CancellationToken cancellationToken)
    {
        using var attemptScope = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        attemptScope.CancelAfter(_options.AttemptTimeout);
        using var response = await _httpClient.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, attemptScope.Token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            // 状态失败面统一以 HttpRequestException(StatusCode) 表达；可重试判定读 StatusCode（5xx/408，传输层无状态码同样可重试）。
            throw new HttpRequestException("HTTP " + (int)response.StatusCode + "（" + response.StatusCode + "）：" + sourceUrl, null, response.StatusCode);
        }

        if (response.Content.Headers.ContentLength.HasValue && response.Content.Headers.ContentLength.Value > _options.MaxArtifactBytes)
        {
            throw new InvalidOperationException("制品超过下载大小上限 " + _options.MaxArtifactBytes + " 字节（Content-Length = " + response.Content.Headers.ContentLength.Value + "）");
        }

        await using var httpStream = await response.Content.ReadAsStreamAsync(attemptScope.Token).ConfigureAwait(false);
        await using var fileStream = new FileStream(stagingPath, FileMode.Create, FileAccess.Write, FileShare.None);
        var buffer = new byte[81920];
        long total = 0;
        int read;
        while ((read = await httpStream.ReadAsync(buffer, attemptScope.Token).ConfigureAwait(false)) > 0)
        {
            total += read;
            if (total > _options.MaxArtifactBytes)
            {
                throw new InvalidOperationException("制品超过下载大小上限 " + _options.MaxArtifactBytes + " 字节（流式计数中断）");
            }

            await fileStream.WriteAsync(buffer, 0, read, attemptScope.Token).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 判定下载失败是否可重试（无状态码 = 传输层异常、5xx、408 可重试；其余 4xx 与调用方取消不重试）。
    /// </summary>
    /// <param name="ex">下载异常。</param>
    /// <returns>可重试返回 <c>true</c>。</returns>
    private static bool IsRetryable(Exception ex)
    {
        if (ex is HttpRequestException requestFailure)
        {
            return requestFailure.StatusCode is null
                   || (int)requestFailure.StatusCode >= 500
                   || requestFailure.StatusCode == HttpStatusCode.RequestTimeout;
        }

        return ex is TaskCanceledException;
    }

    /// <summary>
    /// 校验版本号（镜像 <c>HotfixLoadOptions</c> 注入防线：拒分隔符 / 卷分隔符 / 「..」上跳段）。
    /// </summary>
    /// <param name="version">版本号。</param>
    private static void ValidateVersion(string version)
    {
        if (string.IsNullOrWhiteSpace(version) || version.IndexOfAny(ForbiddenVersionChars) >= 0 || version.Contains("..", StringComparison.Ordinal))
        {
            throw new ArgumentException("版本号必填且禁止包含路径分隔符、卷分隔符或「..」段（制品落盘路径注入面）：" + version, nameof(version));
        }
    }

    /// <summary>
    /// 规整并校验 SHA-256（64 位十六进制）。
    /// </summary>
    /// <param name="artifactSha256">原始校验和。</param>
    /// <returns>规整后校验和。</returns>
    private static string NormalizeSha256(string artifactSha256)
    {
        var normalized = artifactSha256?.Trim();
        if (normalized == null || normalized.Length != 64)
        {
            throw new ArgumentException("ArtifactSha256 必须是 64 位十六进制字符串：" + artifactSha256, nameof(artifactSha256));
        }

        foreach (var c in normalized)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
            {
                throw new ArgumentException("ArtifactSha256 必须是 64 位十六进制字符串：" + artifactSha256, nameof(artifactSha256));
            }
        }

        return normalized;
    }

    /// <summary>
    /// 校验下载地址（绝对 http/https；null = sha-only 在盘校验，允许）。
    /// </summary>
    /// <param name="sourceUrl">下载地址。</param>
    private static void ValidateSourceUrl(string sourceUrl)
    {
        if (string.IsNullOrWhiteSpace(sourceUrl))
        {
            return;
        }

        if (!Uri.TryCreate(sourceUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("ArtifactSourceUrl 必须是绝对 http/https 地址：" + sourceUrl, nameof(sourceUrl));
        }
    }

    /// <summary>
    /// 计算文件 SHA-256 并与期望值比较（大小写不敏感）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    /// <param name="expectedSha256">期望 SHA-256 hex。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>一致返回 <c>true</c>。</returns>
    private static async Task<bool> HashMatchesAsync(string path, string expectedSha256, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var hash = await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return string.Equals(Convert.ToHexString(hash), expectedSha256, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 清空 staging 目录（失败零残留收口；清理自身的 IO 异常不再向上传播——残留目录带 staging 前缀可辨识可人工清）。
    /// </summary>
    /// <param name="stagingDirectory">staging 目录。</param>
    private static void DeleteStaging(string stagingDirectory)
    {
        try
        {
            if (Directory.Exists(stagingDirectory))
            {
                Directory.Delete(stagingDirectory, true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 尽力删除文件（重试前清半成品；不存在或失败均静默）。
    /// </summary>
    /// <param name="path">文件路径。</param>
    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
