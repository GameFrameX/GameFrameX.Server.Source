// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应开源许可证与规定。
//   Usage of this project must strictly comply with applicable open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目 实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   Any legal disputes or liabilities arising from secondary development based on this project
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://cnb.cool/GameFrameX
//   CNB  仓库：https://www.cnb.cool/GameFrameX
//   CNB Repository: https://www.cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Core.Hotfix;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Hotfix admin 受控操作族调度器级测试（C256，复用 C248 测试形态：真执行器 + 真装载断言）：
/// 四 action（register_hotfix_manifest / activate_hotfix_version / check_hotfix_compatibility /
/// rollback_hotfix_version）经 dispatcher 可达且与直调 <see cref="OnlineHotfixRollbackService"/> 行为一致
///（rollback 全链断言审计 / 幂等回放 / 活跃指针 / 实际装载路径）+ 错误码面经 admin 面透传。
/// <para>
/// C294：组合选项自建（不用挂具的空操作执行器）——宿主扩展点 <see cref="IOnlineHotfixRollbackExecutor"/>
/// 以生产 <see cref="HotfixManagerRollbackExecutor"/> Override 注入，玩家目录用空目录桩；业务面先启动
///（热更登记 / 切换发布事件，事件总线 Activate 前拒绝发布），用例结束恒停止。
/// </para>
/// <para>
/// 载体 = Operations 程序集本体（不引 Core ⇒ 无热更注册面类型，ConvertHotfixAssembly 零副作用）；
/// 版本目录以 GUID 命名落测试 bin 目录；与 <see cref="HotfixManagerRollbackExecutorTests"/> 同 Collection
/// 串行（共享 <see cref="HotfixManager"/> 进程级状态；禁并行集合彼此独占执行，组合产物的桥 / 钩子槽位不与其它集合交叠）。
/// </para>
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class OnlineAdminHotfixDispatcherTests : IDisposable
{
    /// <summary>测试用作用域三元组（与组合产物授权作用域一致）。</summary>
    private const string ScopeBody = "\"TenantId\":1,\"AppId\":1,\"ServerId\":1001";

    /// <summary>本实例创建的版本目录（Dispose 统一最佳努力清理）。</summary>
    private readonly List<string> _createdVersions = new List<string>();

    /// <summary>
    /// 构造生产接线的组合选项（族配置段 + 空玩家目录 + 生产热更切换执行器；制品获取器走 Operations 包内默认）。
    /// </summary>
    /// <returns>组合选项。</returns>
    private static CapabilityCompositionOptions CreateProductionExecutorOptions()
    {
        var options = new CapabilityCompositionOptions(OnlineCapabilityTestHarness.FamilySection(), new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions
            {
                TenantId = OnlineCapabilityTestHarness.TenantId,
                AppId = OnlineCapabilityTestHarness.AppId,
                ServerId = OnlineCapabilityTestHarness.ServerId,
            },
        };
        options.Override<IOnlinePlayerDirectory>(new OnlineCapabilityTestHarness.EmptyPlayerDirectory());
        options.Override<IOnlineHotfixRollbackExecutor>(new HotfixManagerRollbackExecutor());
        return options;
    }

    /// <summary>
    /// 组合并启动 Online 族（生产执行器接线），以调度器执行用例主体，结束后恒停止（撤桥 + 释放钩子声明）。
    /// </summary>
    /// <param name="body">用例主体（入参为调度器）。</param>
    /// <returns>异步任务。</returns>
    private static async Task WithDispatcherAsync(Func<AdminApiDispatcher, Task> body)
    {
        var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), CreateProductionExecutorOptions());
        try
        {
            Assert.IsType<HotfixManagerRollbackExecutor>(runtime.Host.Services.GetService<IOnlineHotfixRollbackExecutor>());
            await runtime.Host.StartAsync();
            await body(OnlineCapabilityTestHarness.CreateDispatcher(runtime.Host));
        }
        finally
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// 解析外层信封（Foundation camelCase 形态），取内层 JSON 字符串（data 字段）。
    /// </summary>
    /// <param name="envelopeJson">响应体原文。</param>
    /// <returns>内层 JSON 字符串。</returns>
    private static string ReadInnerJson(string envelopeJson)
    {
        using (var document = JsonDocument.Parse(envelopeJson))
        {
            Assert.Equal(0, document.RootElement.GetProperty("code").GetInt32());
            var data = document.RootElement.GetProperty("data");
            Assert.Equal(JsonValueKind.String, data.ValueKind);
            return data.GetString();
        }
    }

    /// <summary>
    /// 断言内层业务码。
    /// </summary>
    /// <param name="innerJson">内层 JSON。</param>
    /// <param name="expectedCode">期望业务码。</param>
    private static void AssertInnerCode(string innerJson, OnlineErrorCode expectedCode)
    {
        using (var document = JsonDocument.Parse(innerJson))
        {
            Assert.Equal((int)expectedCode, document.RootElement.GetProperty("Code").GetInt32());
        }
    }

    /// <summary>
    /// 制品路径（与 HotfixLoadOptions 版本目录同规则）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    /// <returns>制品完整路径。</returns>
    private static string ArtifactPath(string version)
    {
        return Path.Combine(Environment.CurrentDirectory, version, "GameFrameX.Hotfix.dll");
    }

    /// <summary>
    /// 创建一个真实可装载的版本目录（拷贝 Operations 程序集为 GameFrameX.Hotfix.dll）。
    /// </summary>
    /// <returns>版本目录名。</returns>
    private string NewLoadableVersion()
    {
        var version = "c256-admin-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(Environment.CurrentDirectory, version));
        File.Copy(typeof(OnlineHotfixProtocolManifest).Assembly.Location, ArtifactPath(version));
        _createdVersions.Add(version);
        return version;
    }

    /// <summary>
    /// 真实装载指定版本（镜像生产启动装载）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    /// <returns>异步任务。</returns>
    private static async Task LoadVersion(string version)
    {
        Assert.True(await HotfixManager.LoadHotfixModuleAsync(null, new HotfixLoadOptions { Version = version, }));
    }

    /// <summary>
    /// 调度器级全链：register×2（dispatcher）→ activate v1 → 真装载 v1 → rollback 切到 v2——
    /// 活跃指针（幂等回放证活跃已切）与实际装载路径双断言 + 审计经 admin 查询面可查。
    /// </summary>
    /// <returns>异步任务。</returns>
    [Fact]
    public async Task Dispatch_RollbackHotfixVersion_FullChain_AuditPointerAndLoadPathAsserted()
    {
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();
        const string idempotencyKey = "c256-admin-full-switch";

        await WithDispatcherAsync(async dispatcher =>
        {
            foreach (var version in new[] { v1, v2 })
            {
                var registerEnvelope = await dispatcher.DispatchAsync("register_hotfix_manifest",
                    "{" + ScopeBody + ",\"Version\":\"" + version + "\"}");
                using (var document = JsonDocument.Parse(ReadInnerJson(registerEnvelope)))
                {
                    Assert.Equal(0, document.RootElement.GetProperty("Code").GetInt32());
                    Assert.True(document.RootElement.GetProperty("Data").GetProperty("IsNewRegistered").GetBoolean());
                }
            }

            var activateEnvelope = await dispatcher.DispatchAsync("activate_hotfix_version",
                "{" + ScopeBody + ",\"Version\":\"" + v1 + "\"}");
            using (var activateDocument = JsonDocument.Parse(ReadInnerJson(activateEnvelope)))
            {
                Assert.Equal(0, activateDocument.RootElement.GetProperty("Code").GetInt32());
                Assert.True(activateDocument.RootElement.GetProperty("Data").GetProperty("Activated").GetBoolean());
            }

            await LoadVersion(v1);

            var rollbackEnvelope = await dispatcher.DispatchAsync("rollback_hotfix_version",
                "{" + ScopeBody + ",\"TargetVersion\":\"" + v2 + "\",\"OperatorId\":\"admin-001\",\"Reason\":\"c256 全链\",\"IdempotencyKey\":\"" + idempotencyKey + "\"}");
            using (var rollbackDocument = JsonDocument.Parse(ReadInnerJson(rollbackEnvelope)))
            {
                var data = rollbackDocument.RootElement.GetProperty("Data");
                Assert.Equal(v2, data.GetProperty("TargetVersion").GetString());
                Assert.Equal(v1, data.GetProperty("PreviousVersion").GetString());
                Assert.False(data.GetProperty("IsReplay").GetBoolean());
            }

            // 活跃指针断言：同键重放透传首次结果（IsReplay = true）证明切换命令已落定；实际装载路径断言：真执行器已装载 v2。
            var replayEnvelope = await dispatcher.DispatchAsync("rollback_hotfix_version",
                "{" + ScopeBody + ",\"TargetVersion\":\"" + v2 + "\",\"OperatorId\":\"admin-001\",\"Reason\":\"c256 全链重放\",\"IdempotencyKey\":\"" + idempotencyKey + "\"}");
            using (var replayDocument = JsonDocument.Parse(ReadInnerJson(replayEnvelope)))
            {
                var data = replayDocument.RootElement.GetProperty("Data");
                Assert.True(data.GetProperty("IsReplay").GetBoolean());
                Assert.Equal(v1, data.GetProperty("PreviousVersion").GetString());
            }

            Assert.Equal(ArtifactPath(v2), HotfixManager.HotfixAssembly.Location);

            // 审计断言：回滚审计先行留痕，经统一审计查询面按事件类型可查。
            var auditEnvelope = await dispatcher.DispatchAsync("query_online_audit_events",
                "{" + ScopeBody + ",\"CommandKind\":\"" + OnlineHotfixEvents.RolledBack + "\"}");
            using (var auditDocument = JsonDocument.Parse(ReadInnerJson(auditEnvelope)))
            {
                var items = auditDocument.RootElement.GetProperty("Data").GetProperty("Items");
                Assert.True(items.GetArrayLength() >= 1);
            }
        });
    }

    /// <summary>
    /// check_hotfix_compatibility：只读预检返回兼容报告（切换前后均可演练）。
    /// </summary>
    /// <returns>异步任务。</returns>
    [Fact]
    public async Task Dispatch_CheckHotfixCompatibility_ShouldReturnReport()
    {
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();

        await WithDispatcherAsync(async dispatcher =>
        {
            await dispatcher.DispatchAsync("register_hotfix_manifest", "{" + ScopeBody + ",\"Version\":\"" + v1 + "\"}");
            await dispatcher.DispatchAsync("register_hotfix_manifest", "{" + ScopeBody + ",\"Version\":\"" + v2 + "\"}");
            await dispatcher.DispatchAsync("activate_hotfix_version", "{" + ScopeBody + ",\"Version\":\"" + v1 + "\"}");

            var envelope = await dispatcher.DispatchAsync("check_hotfix_compatibility",
                "{" + ScopeBody + ",\"TargetVersion\":\"" + v2 + "\"}");

            using (var document = JsonDocument.Parse(ReadInnerJson(envelope)))
            {
                Assert.Equal(0, document.RootElement.GetProperty("Code").GetInt32());
                var data = document.RootElement.GetProperty("Data");
                Assert.True(data.GetProperty("IsCompatible").GetBoolean());
                Assert.Equal(0, data.GetProperty("RemovedCount").GetInt32());
                Assert.Equal(0, data.GetProperty("ChangedCount").GetInt32());
            }
        });
    }

    /// <summary>
    /// 错误码面经 admin 面透传：未登记 4002 / 缺必填 4001（幂等键）/ 缺 Reason 4001（审计完整性）/ 制品获取失败 8002。
    /// </summary>
    /// <param name="action">action 名。</param>
    /// <param name="body">请求体。</param>
    /// <param name="expectedCode">期望业务码。</param>
    /// <returns>异步任务。</returns>
    [Theory]
    [InlineData("rollback_hotfix_version", "{" + ScopeBody + ",\"TargetVersion\":\"c256-ghost\",\"OperatorId\":\"admin\",\"Reason\":\"c256\",\"IdempotencyKey\":\"c256-ghost-key\"}", OnlineErrorCode.ResourceNotFound)]
    [InlineData("activate_hotfix_version", "{" + ScopeBody + ",\"Version\":\"c256-ghost\"}", OnlineErrorCode.ResourceNotFound)]
    [InlineData("rollback_hotfix_version", "{" + ScopeBody + ",\"TargetVersion\":\"c256-x\",\"OperatorId\":\"admin\",\"Reason\":\"c256\"}", OnlineErrorCode.ParameterInvalid)]
    [InlineData("register_hotfix_manifest", "{" + ScopeBody + "}", OnlineErrorCode.ParameterInvalid)]
    [InlineData("check_hotfix_compatibility", "{" + ScopeBody + ",\"TargetVersion\":\"c256-x\"}", OnlineErrorCode.ResourceNotFound)]
    public async Task Dispatch_HotfixActions_ErrorCodesTransparent(string action, string body, OnlineErrorCode expectedCode)
    {
        await WithDispatcherAsync(async dispatcher =>
        {
            var envelope = await dispatcher.DispatchAsync(action, body);
            AssertInnerCode(ReadInnerJson(envelope), expectedCode);
        });
    }

    /// <summary>
    /// check_hotfix_compatibility 无活跃版本（已登记目标但未激活基线）：5001 透传。
    /// </summary>
    /// <returns>异步任务。</returns>
    [Fact]
    public async Task Dispatch_CheckHotfixCompatibility_WithoutActiveVersion_ShouldReturn5001()
    {
        var v1 = NewLoadableVersion();

        await WithDispatcherAsync(async dispatcher =>
        {
            await dispatcher.DispatchAsync("register_hotfix_manifest", "{" + ScopeBody + ",\"Version\":\"" + v1 + "\"}");

            var envelope = await dispatcher.DispatchAsync("check_hotfix_compatibility",
                "{" + ScopeBody + ",\"TargetVersion\":\"" + v1 + "\"}");

            AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.StateNotReady);
        });
    }

    /// <summary>
    /// 登记携带制品字段但制品源不可达：获取失败 8002 透传且登记未生效（「已登记」与「已落盘」同时成立红线）。
    /// </summary>
    /// <returns>异步任务。</returns>
    [Fact]
    public async Task Dispatch_RegisterHotfixManifest_UnreachableArtifact_ShouldReturn8002()
    {
        await WithDispatcherAsync(async dispatcher =>
        {
            var envelope = await dispatcher.DispatchAsync("register_hotfix_manifest",
                "{" + ScopeBody + ",\"Version\":\"c256-broken-artifact\",\"ArtifactSourceUrl\":\"http://127.0.0.1:1/GameFrameX.Hotfix.dll\",\"ArtifactSha256\":\"" + new string('a', 64) + "\"}");
            AssertInnerCode(ReadInnerJson(envelope), OnlineErrorCode.DependencyUnavailable);
        });
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
