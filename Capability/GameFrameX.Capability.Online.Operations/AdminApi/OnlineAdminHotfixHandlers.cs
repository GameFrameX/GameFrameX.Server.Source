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
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
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

using System.Text.Json;
using GameFrameX.Capability.Hosting.AdminApi;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Abstractions.Scope;

namespace GameFrameX.Capability.Online.Operations.AdminApi;

/// <summary>
/// Hotfix 版本切换受控操作族 admin action（C256：register_hotfix_manifest / activate_hotfix_version /
/// check_hotfix_compatibility / rollback_hotfix_version）——运行期热更版本切换的唯一运维触发面
///（旧 <c>ReloadHttpHandler</c> HTTP 通道已退役）。
/// <para>
/// 维护约束：四 action 直接映射 <see cref="OnlineHotfixRollbackService"/> 服务面；
/// 作用域两键（TenantId/AppId）从 admin scope 锚定（清单是 App 级资产，ServerId 归一为 0，body 不携带作用域字段）；
/// dispatcher 幂等包裹 = <c>false</c>——服务自幂等（登记按版本判重 / 激活幂等 / 切换命令服务内幂等先行），
/// 不做双层包裹（grant 族先例）。
/// </para>
/// </summary>
public static class OnlineAdminHotfixHandlers
{
    /// <summary>
    /// 注册 Hotfix 受控操作族 action（幂等包裹 false：服务自幂等，见类注释）。
    /// </summary>
    /// <param name="registry">动作注册表。</param>
    /// <param name="service">Hotfix 回滚受控服务（幂等基建缺席未装配时为 null，action 不可达）。</param>
    public static void RegisterHotfixActions(AdminActionRegistry registry, OnlineHotfixRollbackService service)
    {
        registry.Register("register_hotfix_manifest", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => RegisterHotfixManifestAsync(request, scope, cancellationToken, service)), false);
        registry.Register("activate_hotfix_version", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => ActivateHotfixVersionAsync(request, scope, cancellationToken, service)), false);
        registry.Register("check_hotfix_compatibility", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => CheckHotfixCompatibilityAsync(request, scope, cancellationToken, service)), false);
        registry.Register("rollback_hotfix_version", OnlineAdminApiAdapters.Wrap((request, scope, cancellationToken) => RollbackHotfixVersionAsync(request, scope, cancellationToken, service)), false);
    }

    /// <summary>
    /// register_hotfix_manifest：登记版本协议清单（发布流水线簿记；可携制品字段经获取器链先行落盘）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域（两键锚定：TenantId/AppId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="service">Hotfix 回滚受控服务。</param>
    /// <returns>登记回执。</returns>
    private static async Task<object> RegisterHotfixManifestAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken, OnlineHotfixRollbackService service)
    {
        var manifest = new OnlineHotfixProtocolManifest
        {
            Version = OnlineAdminApiContract.RequireString(request, "Version"),
            TenantId = scope.TenantId,
            AppId = scope.AppId,
            Messages = ReadProtocolMessages(request),
            RegisteredTime = OnlineAdminApiContract.ReadOptionalInt64(request, "RegisteredTime") ?? OnlineAdminApiContract.NowSeconds() * 1000,
            ArtifactSourceUrl = OnlineAdminApiContract.ReadOptionalString(request, "ArtifactSourceUrl"),
            ArtifactSha256 = OnlineAdminApiContract.ReadOptionalString(request, "ArtifactSha256"),
        };
        var isNew = OnlineAdminApiContract.Unwrap(await service.RegisterAsync(manifest, cancellationToken).ConfigureAwait(false));
        return new HotfixManifestRegisterResponse
        {
            Version = manifest.Version,
            IsNewRegistered = isNew,
        };
    }

    /// <summary>
    /// activate_hotfix_version：激活已登记版本为活跃基线（装配面初始化入口）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域（两键锚定：TenantId/AppId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="service">Hotfix 回滚受控服务。</param>
    /// <returns>激活回执。</returns>
    private static async Task<object> ActivateHotfixVersionAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken, OnlineHotfixRollbackService service)
    {
        var version = OnlineAdminApiContract.RequireString(request, "Version");
        OnlineAdminApiContract.Unwrap(await service.ActivateAsync(new OnlineScope(scope.TenantId, scope.AppId, 0), version, cancellationToken).ConfigureAwait(false));
        return new HotfixVersionActivateResponse
        {
            Version = version,
            Activated = true,
        };
    }

    /// <summary>
    /// check_hotfix_compatibility：协议兼容只读预检（回滚演练 Runbook 面；不经幂等、不落审计、不触执行器）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域（两键锚定：TenantId/AppId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="service">Hotfix 回滚受控服务。</param>
    /// <returns>兼容检查报告。</returns>
    private static async Task<object> CheckHotfixCompatibilityAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken, OnlineHotfixRollbackService service)
    {
        var targetVersion = OnlineAdminApiContract.RequireString(request, "TargetVersion");
        var report = OnlineAdminApiContract.Unwrap(await service.CheckProtocolCompatibilityAsync(new OnlineScope(scope.TenantId, scope.AppId, 0), targetVersion, cancellationToken).ConfigureAwait(false));
        return new HotfixCompatibilityCheckResponse
        {
            TargetVersion = targetVersion,
            IsCompatible = report.IsCompatible,
            RemovedCount = report.RemovedCount,
            ChangedCount = report.ChangedCount,
            AddedCount = report.AddedCount,
        };
    }

    /// <summary>
    /// rollback_hotfix_version：热更版本受控切换命令（幂等先行 → 兼容闸 → 审计先行 → 执行器 → 切活跃 → 事件）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域（两键锚定：TenantId/AppId）。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <param name="service">Hotfix 回滚受控服务。</param>
    /// <returns>切换回执。</returns>
    private static async Task<object> RollbackHotfixVersionAsync(AdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken, OnlineHotfixRollbackService service)
    {
        var rollbackRequest = new OnlineHotfixRollbackRequest
        {
            Scope = new OnlineScope(scope.TenantId, scope.AppId, 0),
            TargetVersion = OnlineAdminApiContract.RequireString(request, "TargetVersion"),
            OperatorId = OnlineAdminApiContract.RequireString(request, "OperatorId"),
            OperatorName = OnlineAdminApiContract.ReadOptionalString(request, "OperatorName"),
            Reason = OnlineAdminApiContract.RequireString(request, "Reason"),
            IdempotencyKey = OnlineAdminApiContract.RequireString(request, "IdempotencyKey"),
        };
        var result = OnlineAdminApiContract.Unwrap(await service.RollbackAsync(rollbackRequest, cancellationToken).ConfigureAwait(false));
        return new HotfixVersionRollbackResponse
        {
            TargetVersion = result.TargetVersion,
            PreviousVersion = result.PreviousVersion,
            IsReplay = result.IsReplay,
            CompatibilitySummary = result.CompatibilitySummary,
        };
    }

    /// <summary>
    /// 读取协议消息契约行集合（线缆形态：[{ MessageName, MessageId }]；缺失返回空集合 = 无对外协议面）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>消息契约行集合。</returns>
    private static List<OnlineHotfixProtocolMessage> ReadProtocolMessages(AdminApiRequest request)
    {
        var messages = new List<OnlineHotfixProtocolMessage>();
        if (OnlineAdminApiContract.TryGetElement(request.Root, "Messages", out var element) && element.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in element.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string messageName = null;
                var messageId = 0;
                foreach (var property in item.EnumerateObject())
                {
                    if (string.Equals(property.Name, "MessageName", StringComparison.OrdinalIgnoreCase))
                    {
                        messageName = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : property.Value.GetRawText();
                    }
                    else if (string.Equals(property.Name, "MessageId", StringComparison.OrdinalIgnoreCase)
                             && property.Value.ValueKind == JsonValueKind.Number && property.Value.TryGetInt32(out messageId))
                    {
                        // MessageId 数字解析成功即保留；失败保持 0（登记处校验拒绝）。
                    }
                }

                messages.Add(new OnlineHotfixProtocolMessage
                {
                    MessageName = messageName,
                    MessageId = messageId,
                });
            }
        }

        return messages;
    }

    /// <summary>
    /// 登记回执。
    /// </summary>
    public sealed class HotfixManifestRegisterResponse
    {
        /// <summary>获取或设置版本号。</summary>
        public string Version { get; set; }

        /// <summary>获取或设置是否新登记（同 (作用域, 版本号) 已存在为 false，幂等回执不覆盖）。</summary>
        public bool IsNewRegistered { get; set; }
    }

    /// <summary>
    /// 激活回执。
    /// </summary>
    public sealed class HotfixVersionActivateResponse
    {
        /// <summary>获取或设置版本号。</summary>
        public string Version { get; set; }

        /// <summary>获取或设置是否激活成功（重复激活同版本幂等返回成功）。</summary>
        public bool Activated { get; set; }
    }

    /// <summary>
    /// 兼容预检报告。
    /// </summary>
    public sealed class HotfixCompatibilityCheckResponse
    {
        /// <summary>获取或设置目标版本号。</summary>
        public string TargetVersion { get; set; }

        /// <summary>获取或设置是否兼容（Removed 与 Changed 均为 0 才允许切换）。</summary>
        public bool IsCompatible { get; set; }

        /// <summary>获取或设置 Removed 差异数（不兼容）。</summary>
        public int RemovedCount { get; set; }

        /// <summary>获取或设置 Changed 差异数（不兼容）。</summary>
        public int ChangedCount { get; set; }

        /// <summary>获取或设置 Added 差异数（兼容差异）。</summary>
        public int AddedCount { get; set; }
    }

    /// <summary>
    /// 切换回执。
    /// </summary>
    public sealed class HotfixVersionRollbackResponse
    {
        /// <summary>获取或设置目标（切换到）版本号。</summary>
        public string TargetVersion { get; set; }

        /// <summary>获取或设置切换前活跃版本号。</summary>
        public string PreviousVersion { get; set; }

        /// <summary>获取或设置是否幂等回放（重复命令透传首次结果）。</summary>
        public bool IsReplay { get; set; }

        /// <summary>获取或设置协议兼容报告摘要。</summary>
        public string CompatibilitySummary { get; set; }
    }
}
