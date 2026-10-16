// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
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
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 登记期制品获取器测试（C248）：下载落盘 + SHA-256 校验 + 失败零残留（网络失败 / 校验不符 / 超大小上限）、
/// 5xx/408 重试与 4xx 快速失败、同版本目录幂等（一致通过 / 不一致拒绝不覆盖）、
/// sha-only 在盘校验语义、版本号路径注入防线拒绝。
/// </summary>
public sealed class OnlineHotfixArtifactFetcherTests : IDisposable
{
    /// <summary>制品样本字节（模拟热更 DLL 载荷）。</summary>
    private static readonly byte[] Payload = { 0x47, 0x46, 0x58, 0x2d, 0x48, 0x4f, 0x54, 0x46, 0x49, 0x58, 0x2d, 0x43, 0x32, 0x34, 0x38, 0x21 };

    /// <summary>与 <see cref="Payload"/> 对应的 SHA-256 hex。</summary>
    private static readonly string PayloadSha256 = Convert.ToHexString(SHA256.HashData(Payload));

    /// <summary>另一份内容字节（校验不符用例）。</summary>
    private static readonly byte[] OtherPayload = { 0x58, 0x58, 0x58, 0x58, 0x58, 0x58, 0x58, 0x58 };

    /// <summary>与 <see cref="OtherPayload"/> 对应的 SHA-256 hex。</summary>
    private static readonly string OtherPayloadSha256 = Convert.ToHexString(SHA256.HashData(OtherPayload));

    /// <summary>本测试实例创建的版本目录名（Dispose 统一最佳努力清理）。</summary>
    private readonly string[] _createdVersions = new string[16];

    /// <summary>桩 HTTP 传输（记录调用次数；可注入响应序列或网络异常）。</summary>
    private sealed class StubHandler : HttpMessageHandler
    {
        /// <summary>已发生的请求数。</summary>
        public int CallCount
        {
            get;
            private set;
        }

        /// <summary>响应工厂（按尝试序号返回响应或抛网络异常；null = 200 + <see cref="Payload"/>）。</summary>
        public Func<int, HttpResponseMessage> Responder
        {
            get;
            set;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            if (Responder != null)
            {
                return Task.FromResult(Responder(CallCount));
            }

            return Task.FromResult(Ok(Payload));
        }
    }

    /// <summary>
    /// 200 响应（携带字节数组载荷）。
    /// </summary>
    /// <param name="content">响应载荷。</param>
    /// <returns>HTTP 响应。</returns>
    private static HttpResponseMessage Ok(byte[] content)
    {
        return new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content),
        };
    }

    /// <summary>
    /// 构造被测获取器（默认参数：重试 1 次、大小上限 8 字节可被用例覆写）。
    /// </summary>
    /// <param name="handler">桩传输。</param>
    /// <param name="maxRetryCount">失败重试次数。</param>
    /// <param name="maxArtifactBytes">下载大小上限。</param>
    /// <returns>获取器。</returns>
    private static OnlineHotfixArtifactFetcher CreateFetcher(StubHandler handler, int maxRetryCount = 1, long maxArtifactBytes = 1024)
    {
        return new OnlineHotfixArtifactFetcher(new OnlineHotfixArtifactFetchOptions
        {
            MaxRetryCount = maxRetryCount,
            MaxArtifactBytes = maxArtifactBytes,
            AttemptTimeout = TimeSpan.FromSeconds(5),
        }, handler);
    }

    /// <summary>
    /// 分配唯一版本目录名（登记并纳入清理）。
    /// </summary>
    /// <returns>版本目录名。</returns>
    private string NewVersion()
    {
        for (var i = 0; i < _createdVersions.Length; i++)
        {
            if (_createdVersions[i] == null)
            {
                _createdVersions[i] = "c248-art-" + Guid.NewGuid().ToString("N");
                return _createdVersions[i];
            }
        }

        throw new InvalidOperationException("测试版本目录额度用尽");
    }

    /// <summary>
    /// 目标制品路径（与获取器落盘契约同规则：{CWD}/{版本}/GameFrameX.Hotfix.dll）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    /// <returns>制品完整路径。</returns>
    private static string TargetPath(string version)
    {
        return Path.Combine(Environment.CurrentDirectory, version, "GameFrameX.Hotfix.dll");
    }

    /// <summary>
    /// 断言 staging 零残留（任何失败路径不得遗留 {版本}.staging-* 目录）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    private static void AssertNoStagingResidue(string version)
    {
        Assert.Empty(Directory.GetDirectories(Environment.CurrentDirectory, version + ".staging-*"));
    }

    /// <summary>
    /// 预置在盘制品（幂等用例前置）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    /// <param name="content">制品字节。</param>
    private static void StageExistingArtifact(string version, byte[] content)
    {
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, version));
        File.WriteAllBytes(TargetPath(version), content);
    }

    /// <summary>
    /// 成功下载应把校验通过的制品原子落盘到版本目录，且无 staging 残留。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_DownloadLandsVerifiedArtifactAtomically()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler);
        var version = NewVersion();

        await fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256);

        Assert.True(File.Exists(TargetPath(version)));
        Assert.Equal(Payload, await File.ReadAllBytesAsync(TargetPath(version)));
        Assert.Equal(1, handler.CallCount);
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// SHA-256 校验不符应拒绝（不落盘目标制品）且 staging 零残留。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ChecksumMismatch_RejectedWithZeroResidue()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler);
        var version = NewVersion();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", OtherPayloadSha256));

        Assert.False(File.Exists(TargetPath(version)));
        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 网络异常应重试至额度耗尽后失败，磁盘零残留。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_NetworkFailureRetriedThenFails_LeavesZeroResidue()
    {
        var handler = new StubHandler
        {
            Responder = _ => throw new HttpRequestException("connection reset"),
        };
        var fetcher = CreateFetcher(handler, maxRetryCount: 1);
        var version = NewVersion();

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256));

        Assert.Equal(2, handler.CallCount);
        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 5xx 应重试且成功后正常落盘（总尝试数 = 1 + 重试次数）。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ServerErrorRetriedUntilSuccess()
    {
        var handler = new StubHandler
        {
            Responder = attempt => attempt == 1
                ? new HttpResponseMessage(HttpStatusCode.InternalServerError)
                : Ok(Payload),
        };
        var fetcher = CreateFetcher(handler, maxRetryCount: 1);
        var version = NewVersion();

        await fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256);

        Assert.Equal(2, handler.CallCount);
        Assert.True(File.Exists(TargetPath(version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 4xx（非 408）应快速失败不重试。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ClientErrorFailsFastWithoutRetry()
    {
        var handler = new StubHandler
        {
            Responder = _ => new HttpResponseMessage(HttpStatusCode.NotFound),
        };
        var fetcher = CreateFetcher(handler, maxRetryCount: 2);
        var version = NewVersion();

        await Assert.ThrowsAsync<HttpRequestException>(() => fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256));

        Assert.Equal(1, handler.CallCount);
        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 超过下载大小上限应拒绝且不重试（Content-Length 预检即断）。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ExceedsSizeCap_Rejected()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler, maxRetryCount: 2, maxArtifactBytes: 4);
        var version = NewVersion();

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256));

        Assert.Equal(1, handler.CallCount);
        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 目标版本目录已有 checksum 一致的制品应幂等通过且不发起任何 HTTP 请求。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ExistingMatchingArtifact_IdempotentWithoutHttp()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler);
        var version = NewVersion();
        StageExistingArtifact(version, Payload);

        await fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256);

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(Payload, await File.ReadAllBytesAsync(TargetPath(version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 目标版本目录已有 checksum 不一致的制品应拒绝且不覆盖既有内容（版本目录一经落盘不可变更）。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ExistingMismatchedArtifact_RejectedWithoutOverwrite()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler);
        var version = NewVersion();
        StageExistingArtifact(version, OtherPayload);

        await Assert.ThrowsAsync<InvalidOperationException>(() => fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256));

        Assert.Equal(0, handler.CallCount);
        Assert.Equal(OtherPayload, await File.ReadAllBytesAsync(TargetPath(version)));
        AssertNoStagingResidue(version);
    }

    /// <summary>
    /// 只给校验和（无下载地址）且制品未在盘应失败——sha-only 语义 = 校验已在盘制品一致性。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ShaOnlyWithoutOnDiskArtifact_Fails()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler);
        var version = NewVersion();

        await Assert.ThrowsAsync<FileNotFoundException>(() => fetcher.EnsureArtifactAsync(version, null, PayloadSha256));

        Assert.Equal(0, handler.CallCount);
        Assert.False(Directory.Exists(Path.Combine(Environment.CurrentDirectory, version)));
    }

    /// <summary>
    /// 只给校验和且在盘制品一致应通过且不发起 HTTP 请求。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_ShaOnlyMatchingOnDiskArtifact_PassesWithoutHttp()
    {
        var handler = new StubHandler();
        var fetcher = CreateFetcher(handler);
        var version = NewVersion();
        StageExistingArtifact(version, Payload);

        await fetcher.EnsureArtifactAsync(version, null, PayloadSha256);

        Assert.Equal(0, handler.CallCount);
        Assert.True(File.Exists(TargetPath(version)));
    }

    /// <summary>
    /// 版本号含路径分隔符 / 卷分隔符 / 「..」上跳段应一律拒绝（落盘路径注入面，与 HotfixLoadOptions 同防线）。
    /// </summary>
    [Theory]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("..")]
    [InlineData("a/../b")]
    [InlineData("1:0")]
    [InlineData("")]
    public async Task EnsureArtifactAsync_ForbiddenVersion_Throws(string version)
    {
        var fetcher = CreateFetcher(new StubHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.EnsureArtifactAsync(version, "http://artifacts.test/hotfix.dll", PayloadSha256));
    }

    /// <summary>
    /// 校验和非 64 位十六进制应拒绝。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_MalformedSha_Throws()
    {
        var fetcher = CreateFetcher(new StubHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.EnsureArtifactAsync(NewVersion(), "http://artifacts.test/hotfix.dll", "not-a-sha"));
    }

    /// <summary>
    /// 下载地址非绝对 http/https 应拒绝。
    /// </summary>
    [Fact]
    public async Task EnsureArtifactAsync_NonHttpSourceUrl_Throws()
    {
        var fetcher = CreateFetcher(new StubHandler());

        await Assert.ThrowsAsync<ArgumentException>(() => fetcher.EnsureArtifactAsync(NewVersion(), "ftp://artifacts.test/hotfix.dll", PayloadSha256));
    }

    /// <summary>
    /// 最佳努力清理本实例创建的版本目录（装载映射中的文件删除失败不视为测试失败）。
    /// </summary>
    void IDisposable.Dispose()
    {
        foreach (var version in _createdVersions)
        {
            if (version == null)
            {
                continue;
            }

            try
            {
                Directory.Delete(Path.Combine(Environment.CurrentDirectory, version), true);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
