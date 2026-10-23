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

using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Core.Hotfix;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// Hotfix admin 受控操作族调度器级测试（真执行器 + 真装载断言）：
/// 四 action（register_hotfix_manifest / activate_hotfix_version / check_hotfix_compatibility /
/// rollback_hotfix_version）经 dispatcher 可达且与直调 <see cref="OnlineHotfixRollbackService"/> 行为一致
///（rollback 全链断言审计 / 幂等回放 / 活跃指针 / 实际装载路径）+ 错误码面经 admin 面透传。
/// <para>
/// 载体 = Operations 程序集本体（不引 Core ⇒ 无热更注册面类型，ConvertHotfixAssembly 零副作用）；
/// 版本目录以 GUID 命名落测试 bin 目录；与 <see cref="HotfixManagerRollbackExecutorTests"/> 同 Collection
/// 串行（共享 <see cref="HotfixManager"/> 进程级状态）。
/// </para>
/// </summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class OnlineAdminHotfixDispatcherTests : System.IDisposable
{
    /// <summary>测试用作用域三元组（与组合产物授权作用域一致）。</summary>
    private const string ScopeBody = "\"TenantId\":1,\"AppId\":1,\"ServerId\":1001";

    /// <summary>本实例创建的版本目录（Dispose 统一最佳努力清理）。</summary>
    private readonly System.Collections.Generic.List<string> _createdVersions = new System.Collections.Generic.List<string>();

    /// <summary>
    /// 构造经注册反转组装的调度器（组合产物装配真执行器 + 真制品获取器）。
    /// </summary>
    /// <returns>调度器。</returns>
    private static GameFrameX.Capability.Hosting.AdminApi.AdminApiDispatcher CreateDispatcher()
    {
        var composition = OnlineCapabilityComposition.AddOnlineCapabilities(options =>
        {
            options.TenantId = 1;
            options.AppId = 1;
            options.ServerId = 1001;
            options.EnableRuntime = true;
        });
        return OnlineAdminApiComposition.CreateDispatcher(composition.Runtime);
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
    private static void AssertInnerCode(string innerJson, GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode expectedCode)
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
        return Path.Combine(System.Environment.CurrentDirectory, version, "GameFrameX.Hotfix.dll");
    }

    /// <summary>
    /// 创建一个真实可装载的版本目录（拷贝 Operations 程序集为 GameFrameX.Hotfix.dll）。
    /// </summary>
    /// <returns>版本目录名。</returns>
    private string NewLoadableVersion()
    {
        var version = "c256-admin-" + System.Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(Path.Combine(System.Environment.CurrentDirectory, version));
        File.Copy(typeof(OnlineHotfixProtocolManifest).Assembly.Location, ArtifactPath(version));
        _createdVersions.Add(version);
        return version;
    }

    /// <summary>
    /// 真实装载指定版本（镜像生产启动装载）。
    /// </summary>
    /// <param name="version">版本目录名。</param>
    private static async Task LoadVersion(string version)
    {
        Assert.True(await HotfixManager.LoadHotfixModuleAsync(null, new HotfixLoadOptions { Version = version, }));
    }

    /// <summary>
    /// 调度器级全链：register×2（dispatcher）→ activate v1 → 真装载 v1 → rollback 切到 v2——
    /// 活跃指针（幂等回放证活跃已切）与实际装载路径双断言 + 审计经 admin 查询面可查。
    /// </summary>
    [Fact]
    public async Task Dispatch_RollbackHotfixVersion_FullChain_AuditPointerAndLoadPathAsserted()
    {
        var dispatcher = CreateDispatcher();
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();
        const string idempotencyKey = "c256-admin-full-switch";

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
    }

    /// <summary>
    /// check_hotfix_compatibility：只读预检返回兼容报告（切换前后均可演练）。
    /// </summary>
    [Fact]
    public async Task Dispatch_CheckHotfixCompatibility_ShouldReturnReport()
    {
        var dispatcher = CreateDispatcher();
        var v1 = NewLoadableVersion();
        var v2 = NewLoadableVersion();
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
    }

    /// <summary>
    /// 错误码面经 admin 面透传：未登记 4002 / 缺必填 4001（幂等键）/ 缺 Reason 4001（审计完整性）/ 制品获取失败 8002。
    /// </summary>
    /// <param name="action">action 名。</param>
    /// <param name="body">请求体。</param>
    /// <param name="expectedCode">期望业务码。</param>
    [Theory]
    [InlineData("rollback_hotfix_version", "{" + ScopeBody + ",\"TargetVersion\":\"c256-ghost\",\"OperatorId\":\"admin\",\"Reason\":\"c256\",\"IdempotencyKey\":\"c256-ghost-key\"}", GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.ResourceNotFound)]
    [InlineData("activate_hotfix_version", "{" + ScopeBody + ",\"Version\":\"c256-ghost\"}", GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.ResourceNotFound)]
    [InlineData("rollback_hotfix_version", "{" + ScopeBody + ",\"TargetVersion\":\"c256-x\",\"OperatorId\":\"admin\",\"Reason\":\"c256\"}", GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.ParameterInvalid)]
    [InlineData("register_hotfix_manifest", "{" + ScopeBody + "}", GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.ParameterInvalid)]
    [InlineData("check_hotfix_compatibility", "{" + ScopeBody + ",\"TargetVersion\":\"c256-x\"}", GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.ResourceNotFound)]
    public async Task Dispatch_HotfixActions_ErrorCodesTransparent(string action, string body, GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode expectedCode)
    {
        var dispatcher = CreateDispatcher();
        var envelope = await dispatcher.DispatchAsync(action, body);
        AssertInnerCode(ReadInnerJson(envelope), expectedCode);
    }

    /// <summary>
    /// check_hotfix_compatibility 无活跃版本（已登记目标但未激活基线）：5001 透传。
    /// </summary>
    [Fact]
    public async Task Dispatch_CheckHotfixCompatibility_WithoutActiveVersion_ShouldReturn5001()
    {
        var dispatcher = CreateDispatcher();
        var v1 = NewLoadableVersion();
        await dispatcher.DispatchAsync("register_hotfix_manifest", "{" + ScopeBody + ",\"Version\":\"" + v1 + "\"}");

        var envelope = await dispatcher.DispatchAsync("check_hotfix_compatibility",
            "{" + ScopeBody + ",\"TargetVersion\":\"" + v1 + "\"}");

        AssertInnerCode(ReadInnerJson(envelope), GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.StateNotReady);
    }
    /// <summary>
    /// 登记携带制品字段但制品源不可达：获取失败 8002 透传且登记未生效（「已登记」与「已落盘」同时成立红线）。
    /// </summary>
    [Fact]
    public async Task Dispatch_RegisterHotfixManifest_UnreachableArtifact_ShouldReturn8002()
    {
        var dispatcher = CreateDispatcher();
        var envelope = await dispatcher.DispatchAsync("register_hotfix_manifest",
            "{" + ScopeBody + ",\"Version\":\"c256-broken-artifact\",\"ArtifactSourceUrl\":\"http://127.0.0.1:1/GameFrameX.Hotfix.dll\",\"ArtifactSha256\":\"" + new string('a', 64) + "\"}");
        AssertInnerCode(ReadInnerJson(envelope), GameFrameX.Capability.Online.Abstractions.Contracts.OnlineErrorCode.DependencyUnavailable);
    }

    /// <summary>
    /// 最佳努力清理本实例创建的版本目录（collectible ALC 退役宽限期内文件仍被映射，删除失败静默——目录名带 GUID 不复用）。
    /// </summary>
    void System.IDisposable.Dispose()
    {
        foreach (var version in _createdVersions)
        {
            try
            {
                Directory.Delete(Path.Combine(System.Environment.CurrentDirectory, version), true);
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
