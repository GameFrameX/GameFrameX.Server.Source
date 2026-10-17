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
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameFrameX.Core.Hotfix;
using GameFrameX.Foundation.Idempotency;
using GameFrameX.Capability.Online.Operations.Audit;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Hotfix 版本切换执行器生产路径测试（C248）：真实程序集装载的真版本双目录切换
///（向前 + 向后，活跃版本指针与实际装载路径双断言）、切换时刻零下载（目标未落盘即拒、原版本不动）、
/// 损坏制品失败恢复原版本、并发切换互斥、同路径幂等短路、服务全链（登记 → 切换 → 审计 → 事件）。
/// <para>
/// 载体 = Operations 程序集本体（不引 Core ⇒ 无热更注册面类型，ParseDll 零副作用）；
/// 版本目录以 GUID 命名落测试 bin 目录；同类内测试串行（共享 <see cref="HotfixManager"/> 进程级状态）。
/// </para>
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class HotfixManagerRollbackExecutorTests : IDisposable
{
    /// <summary>测试用租户标识。</summary>
    private const long TenantId = 9211;

    /// <summary>测试用应用标识。</summary>
    private const long AppId = 9212;

    /// <summary>本实例创建的版本目录（Dispose 统一最佳努力清理）。</summary>
    private readonly List<string> _createdVersions = new List<string>();

    /// <summary>步进时钟（Foundation 协调器依赖）。</summary>
    private sealed class StepClock : IClock
    {
        private long _nowMilliseconds = 1760000000000;

        public long UtcNowTime
        {
            get
            {
                _nowMilliseconds += 10;
                return _nowMilliseconds;
            }
        }
    }

    /// <summary>制品路径（与 HotfixLoadOptions 版本目录同规则）。</summary>
    private static string ArtifactPath(string version)
    {
        return Path.Combine(Environment.CurrentDirectory, version, "GameFrameX.Hotfix.dll");
    }

    /// <summary>
    /// 装配一个真实执行器 + 真实服务链（InMemory store / 幂等 / 审计 / 事件记录）。
    /// </summary>
    /// <param name="store">版本清单存储。</param>
    /// <returns>服务 + 事件记录器。</returns>
    private static (OnlineHotfixRollbackService Service, OnlineEventRecorder Recorder) CreateProductionService(InMemoryOnlineHotfixVersionStore store)
    {
        var recorder = new OnlineEventRecorder();
        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), new IdempotencyOptions
        {
            ConcurrentWaitTimeoutMilliseconds = 1,
            FailedReplayPolicy = FailedReplayPolicy.ReExecute,
        });
        var idempotency = new OnlineIdempotencyService(coordinator, null);
        var audit = new OnlineAuditService(new InMemoryOnlineAuditStore());
        var service = new OnlineHotfixRollbackService(store, idempotency, audit, new HotfixManagerRollbackExecutor(), null, recorder);
        return (service, recorder);
    }

    /// <summary>
    /// 构造回滚命令请求。
    /// </summary>
    /// <param name="targetVersion">目标版本。</param>
    /// <param name="idempotencyKey">幂等键。</param>
    /// <returns>回滚命令。</returns>
    private static OnlineHotfixRollbackRequest CreateRollbackRequest(string targetVersion, string idempotencyKey)
    {
        return new OnlineHotfixRollbackRequest
        {
            Scope = new OnlineScope(TenantId, AppId, 0),
            TargetVersion = targetVersion,
            OperatorId = "admin-001",
            OperatorName = "运维一号",
            Reason = "线上协议异常，切换到目标版本",
            IdempotencyKey = idempotencyKey,
        };
    }

    /// <summary>
    /// 构造一份版本协议清单。
    /// </summary>
    /// <param name="version">版本号。</param>
    /// <returns>版本清单。</returns>
    private static OnlineHotfixProtocolManifest CreateManifest(string version)
    {
        return new OnlineHotfixProtocolManifest
        {
            Version = version,
            TenantId = TenantId,
            AppId = AppId,
            Messages = new List<OnlineHotfixProtocolMessage>(),
            RegisteredTime = 1_760_000_000_000L,
        };
    }

    /// <summary>
    /// 创建一个真实可装载的版本目录（拷贝 Operations 程序集为 GameFrameX.Hotfix.dll）。
    /// </summary>
    /// <returns>版本目录名。</returns>
    private string NewLoadableVersion()
    {
        var version = "c248-exec-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, version));
        File.Copy(typeof(OnlineHotfixProtocolManifest).Assembly.Location, ArtifactPath(version));
        _createdVersions.Add(version);
        return version;
    }

    /// <summary>
    /// 创建一个损坏制品的版本目录（非程序集字节，装载必败）。
    /// </summary>
    /// <returns>版本目录名。</returns>
    private string NewCorruptVersion()
    {
        var version = "c248-exec-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, version));
        File.WriteAllBytes(ArtifactPath(version), new byte[] { 0x4e, 0x6f, 0x74, 0x41, 0x44, 0x6c, 0x6c });
        _createdVersions.Add(version);
        return version;
    }

    /// <summary>
    /// 登记一个目录名（未创建内容；目标未落盘用例）。
    /// </summary>
    /// <returns>版本目录名。</returns>
    private string NewMissingVersion()
    {
        var version = "c248-exec-" + Guid.NewGuid().ToString("N");
        _createdVersions.Add(version);
        return version;
    }

    /// <summary>
    /// 桩 HTTP 传输（返回固定制品字节）。
    /// </summary>
    private sealed class PayloadHandler : HttpMessageHandler
    {
        /// <summary>响应载荷。</summary>
        private readonly byte[] _payload;

        /// <summary>已发生请求数。</summary>
        public int CallCount
        {
            get;
            private set;
        }

        /// <summary>
        /// 初始化 <see cref="PayloadHandler"/>。
        /// </summary>
        /// <param name="payload">制品字节。</param>
        public PayloadHandler(byte[] payload)
        {
            _payload = payload;
        }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_payload),
            });
        }
    }

    /// <summary>
    /// 真实装载指定版本（镜像生产启动装载）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    private static async Task LoadVersion(string version)
    {
        Assert.True(await HotfixManager.LoadHotfixModule(null, new HotfixLoadOptions { Version = version, }));
    }

    /// <summary>
    /// 向前向后切换均真实切换装载路径（活跃指针断言由服务全链用例覆盖）。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_SwitchesForwardAndBackward_RealAssemblyLoaded()
    {
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();
        await LoadVersion(v1);
        var executor = new HotfixManagerRollbackExecutor();

        await executor.RollbackAsync(v2);

        Assert.Equal(ArtifactPath(v2), HotfixManager.HotfixAssembly.Location);

        await executor.RollbackAsync(v1);

        Assert.Equal(ArtifactPath(v1), HotfixManager.HotfixAssembly.Location);
    }

    /// <summary>
    /// 目标制品未落盘：拒绝（切换时刻零下载红线）且原版本装载不动。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_TargetNotOnDisk_ThrowsFileNotFound_OriginalIntact()
    {
        var v1 = NewLoadableVersion();
        await LoadVersion(v1);
        var executor = new HotfixManagerRollbackExecutor();

        await Assert.ThrowsAsync<FileNotFoundException>(() => executor.RollbackAsync(NewMissingVersion()));

        Assert.Equal(ArtifactPath(v1), HotfixManager.HotfixAssembly.Location);
    }

    /// <summary>
    /// 目标制品损坏（装载失败）：上抛失败且自动恢复原版本装载（失败恢复红线）。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_CorruptTarget_FailsAndRecoversOriginal()
    {
        var v1 = NewLoadableVersion();
        var corrupt = NewCorruptVersion();
        await LoadVersion(v1);
        var executor = new HotfixManagerRollbackExecutor();

        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RollbackAsync(corrupt));

        Assert.Equal(ArtifactPath(v1), HotfixManager.HotfixAssembly.Location);
    }

    /// <summary>
    /// 并发切换被互斥串行化：两命令都成功完成、终态装载路径为二者之一、进程无异常。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_ConcurrentSwitches_SerializedNoCorruption()
    {
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();
        var v3 = NewLoadableVersion();
        await LoadVersion(v1);
        var executor = new HotfixManagerRollbackExecutor();

        await Task.WhenAll(executor.RollbackAsync(v2), executor.RollbackAsync(v3));

        Assert.Contains(HotfixManager.HotfixAssembly.Location, new[] { ArtifactPath(v2), ArtifactPath(v3), });
    }

    /// <summary>
    /// 目标即当前装载路径：同路径幂等守卫短路成功，装载路径不变。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_SameVersionAsCurrent_IdempotentShortCircuit()
    {
        var v1 = NewLoadableVersion();
        await LoadVersion(v1);
        var executor = new HotfixManagerRollbackExecutor();

        await executor.RollbackAsync(v1);

        Assert.Equal(ArtifactPath(v1), HotfixManager.HotfixAssembly.Location);
    }

    /// <summary>
    /// 非法版本号（路径注入面）拒绝。
    /// </summary>
    [Theory]
    [InlineData("../escape")]
    [InlineData("a/b")]
    [InlineData("1:0")]
    public async Task RollbackAsync_ForbiddenVersion_Throws(string version)
    {
        var executor = new HotfixManagerRollbackExecutor();

        await Assert.ThrowsAsync<ArgumentException>(() => executor.RollbackAsync(version));
    }

    /// <summary>
    /// 服务全链（登记 → 激活 → 切换）：向前切到更新已登记版本与向后回滚同一路径，
    /// 活跃版本指针与实际装载路径双断言，审计与事件完整。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_ServiceFullChain_ForwardThenBackward_PointerAndLoadPathBothAsserted()
    {
        var store = new InMemoryOnlineHotfixVersionStore();
        var (service, recorder) = CreateProductionService(store);
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();
        Assert.True((await service.RegisterAsync(CreateManifest(v1))).Data);
        Assert.True((await service.RegisterAsync(CreateManifest(v2))).Data);
        Assert.True((await service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), v1)).Data);
        await LoadVersion(v1);

        // 向前：切到更新登记的 v2。
        var forward = await service.RollbackAsync(CreateRollbackRequest(v2, "c248-full-forward"));
        Assert.True(forward.IsSuccess, forward.Message);
        Assert.Equal(v2, (await store.FindActiveAsync(TenantId, AppId)).Version);
        Assert.Equal(ArtifactPath(v2), HotfixManager.HotfixAssembly.Location);

        // 向后：回滚到 v1。
        var backward = await service.RollbackAsync(CreateRollbackRequest(v1, "c248-full-backward"));
        Assert.True(backward.IsSuccess, backward.Message);
        Assert.Equal(v1, (await store.FindActiveAsync(TenantId, AppId)).Version);
        Assert.Equal(ArtifactPath(v1), HotfixManager.HotfixAssembly.Location);

        Assert.Equal(2, recorder.Filter(OnlineHotfixEvents.RolledBack).Count);
    }

    /// <summary>
    /// 服务全链失败面：目标制品未落盘（登记后目录被清）→ 8002 真失败（非恒定），原版本装载与活跃指针均不动。
    /// </summary>
    [Fact]
    public async Task RollbackAsync_ServiceFullChain_TargetNotOnDisk_Rejected8002WithOriginalIntact()
    {
        var store = new InMemoryOnlineHotfixVersionStore();
        var (service, _) = CreateProductionService(store);
        var v1 = NewLoadableVersion();
        var ghost = NewMissingVersion();
        Assert.True((await service.RegisterAsync(CreateManifest(v1))).Data);
        Assert.True((await service.RegisterAsync(CreateManifest(ghost))).Data);
        Assert.True((await service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), v1)).Data);
        await LoadVersion(v1);

        var failure = await service.RollbackAsync(CreateRollbackRequest(ghost, "c248-ghost-target"));

        Assert.False(failure.IsSuccess);
        Assert.Equal(OnlineErrorCode.DependencyUnavailable, failure.Code);
        Assert.Equal(v1, (await store.FindActiveAsync(TenantId, AppId)).Version);
        Assert.Equal(ArtifactPath(v1), HotfixManager.HotfixAssembly.Location);
    }

    /// <summary>
    /// 端到端（C248 全链）：携带 SourceUrl 登记 → 真实获取器下载 + SHA-256 校验 + 原子落盘 → 清单落簿 →
    /// 切换命令装载该版本（活跃指针与实际装载路径双断言）——「已登记」与「已落盘」同时成立的直接证明。
    /// </summary>
    [Fact]
    public async Task RegisterWithUrlThenRollback_EndToEnd_ArtifactLandedAndSwitched()
    {
        var artifactBytes = await File.ReadAllBytesAsync(typeof(OnlineHotfixProtocolManifest).Assembly.Location);
        var handler = new PayloadHandler(artifactBytes);
        var fetcher = new OnlineHotfixArtifactFetcher(new OnlineHotfixArtifactFetchOptions { MaxRetryCount = 0, }, handler);
        var store = new InMemoryOnlineHotfixVersionStore();
        var recorder = new OnlineEventRecorder();
        var coordinator = new IdempotencyCoordinator(new InMemoryIdempotencyStore(), new StepClock(), new IdempotencyOptions
        {
            ConcurrentWaitTimeoutMilliseconds = 1,
            FailedReplayPolicy = FailedReplayPolicy.ReExecute,
        });
        var service = new OnlineHotfixRollbackService(store, new OnlineIdempotencyService(coordinator, null), new OnlineAuditService(new InMemoryOnlineAuditStore()), new HotfixManagerRollbackExecutor(), fetcher, recorder);
        var v1 = NewLoadableVersion();
        var v2 = NewMissingVersion();
        var manifestV1 = CreateManifest(v1);
        var manifestV2 = CreateManifest(v2);
        manifestV2.ArtifactSourceUrl = "http://artifacts.test/" + v2 + "/GameFrameX.Hotfix.dll";
        manifestV2.ArtifactSha256 = Convert.ToHexString(SHA256.HashData(artifactBytes));
        Assert.True((await service.RegisterAsync(manifestV1)).Data);
        Assert.True((await service.RegisterAsync(manifestV2)).Data);
        Assert.True(File.Exists(ArtifactPath(v2)));
        Assert.True(handler.CallCount >= 1);
        Assert.True((await service.ActivateAsync(new OnlineScope(TenantId, AppId, 0), v1)).Data);
        await LoadVersion(v1);

        var rollback = await service.RollbackAsync(CreateRollbackRequest(v2, "c248-e2e-switch"));

        Assert.True(rollback.IsSuccess, rollback.Message);
        Assert.Equal(v2, (await store.FindActiveAsync(TenantId, AppId)).Version);
        Assert.Equal(ArtifactPath(v2), HotfixManager.HotfixAssembly.Location);
    }

    /// <summary>
    /// 最佳努力清理本实例创建的版本目录（collectible ALC 退役宽限期内文件仍被映射，删除失败静默——目录名带 GUID 不复用）。
    /// </summary>
    void IDisposable.Dispose()
    {
        foreach (var version in _createdVersions)
        {
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
