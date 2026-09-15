// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
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
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using GameFrameX.Foundation.Json;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Scope;

namespace GameFrameX.Online.Runtime.AdminApi;

/// <summary>
/// LiveOps 域 admin 命令（publishRemoteConfig / syncDeviceGroup / syncScheduledTask / pushAnnouncement /
/// syncPlayerSegment / publishConfigRollout / rollbackConfigRollout）。
/// <para>
/// 维护约束：变更 C122 决策⑧②——InMemory 追加式注册表为最小承载（admin 面下发留痕与最新态查询），
/// 生产级配置分发管道（存储 / 灰度计算 / 客户端推送）登记为后续 change；七个 action 均为 Admin 无幂等键族，
/// 不做调度器层幂等包裹；ConfigKind 线缆数字 1～4 固定映射 RemoteConfig / Announcement / DeviceGroup / ScheduledTask。
/// </para>
/// </summary>
public sealed class OnlineAdminLiveOpsHandlers
{
    /// <summary>
    /// 宿主。
    /// </summary>
    private readonly OnlineRuntimeHost _host;

    /// <summary>
    /// 初始化 <see cref="OnlineAdminLiveOpsHandlers"/>。
    /// </summary>
    /// <param name="host">宿主。</param>
    public OnlineAdminLiveOpsHandlers(OnlineRuntimeHost host)
    {
        _host = host ?? throw new ArgumentNullException(nameof(host));
    }

    /// <summary>
    /// 注册 LiveOps 域 action。
    /// </summary>
    /// <param name="dispatcher">调度器。</param>
    public void Register(OnlineAdminApiDispatcher dispatcher)
    {
        dispatcher.Register("publishRemoteConfig", PublishRemoteConfigAsync, false);
        dispatcher.Register("syncDeviceGroup", SyncDeviceGroupAsync, false);
        dispatcher.Register("syncScheduledTask", SyncScheduledTaskAsync, false);
        dispatcher.Register("pushAnnouncement", PushAnnouncementAsync, false);
        dispatcher.Register("syncPlayerSegment", SyncPlayerSegmentAsync, false);
        dispatcher.Register("publishConfigRollout", PublishConfigRolloutAsync, false);
        dispatcher.Register("rollbackConfigRollout", RollbackConfigRolloutAsync, false);
    }

    /// <summary>
    /// publishRemoteConfig：远程配置发布留痕。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> PublishRemoteConfigAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var configId = RequireId(request, "ConfigId");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var entry = _host.LiveOps.Publish("RemoteConfig", configId.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty,
            reason, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, 0));
    }

    /// <summary>
    /// syncDeviceGroup：设备分组同步留痕。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> SyncDeviceGroupAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var deviceGroupId = RequireId(request, "DeviceGroupId");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var entry = _host.LiveOps.Sync("DeviceGroup", deviceGroupId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, 0));
    }

    /// <summary>
    /// syncScheduledTask：定时任务同步留痕。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> SyncScheduledTaskAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var jobId = RequireId(request, "JobId");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var entry = _host.LiveOps.Sync("ScheduledTask", jobId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, 0));
    }

    /// <summary>
    /// pushAnnouncement：公告推送留痕。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> PushAnnouncementAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var announcementId = RequireId(request, "AnnouncementId");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var entry = _host.LiveOps.Publish("Announcement", announcementId.ToString(System.Globalization.CultureInfo.InvariantCulture), string.Empty,
            reason, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, 0));
    }

    /// <summary>
    /// syncPlayerSegment：玩家分群同步留痕（载荷 = 名称 + 条件原文）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> SyncPlayerSegmentAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var segmentId = RequireId(request, "SegmentId");
        var name = OnlineAdminApiContract.RequireString(request, "Name");
        var conditions = OnlineAdminApiContract.ReadOptionalString(request, "Conditions") ?? string.Empty;
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var payload = JsonHelper.Serialize(new SegmentPayload { Name = name, Conditions = conditions, Reason = reason, });
        var entry = _host.LiveOps.Sync("PlayerSegment", segmentId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, segmentId));
    }

    /// <summary>
    /// publishConfigRollout：配置灰度发布留痕（ConfigKind 1～4 + 键 + 版本 + 分群 + 百分比）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> PublishConfigRolloutAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var configKind = ReadConfigKind(request);
        var configKey = OnlineAdminApiContract.RequireString(request, "ConfigKey");
        var configVersion = RequireVersion(request, "ConfigVersion");
        var segmentId = request.ReadNullableInt64("SegmentId") ?? 0;
        var percent = request.ReadNullableInt32("Percent") ?? 100;
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var payload = JsonHelper.Serialize(new RolloutPayload
        {
            ConfigKind = configKind,
            SegmentId = segmentId,
            Percent = percent,
            Reason = reason,
        });
        var entry = _host.LiveOps.Publish(RolloutKind(configKind), configKey, configVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            payload, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, segmentId));
    }

    /// <summary>
    /// rollbackConfigRollout：配置灰度回滚留痕（目标版本即回滚后生效版本）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="scope">作用域。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>LiveOps 回执。</returns>
    public Task<object> RollbackConfigRolloutAsync(OnlineAdminApiRequest request, OnlineScope scope, CancellationToken cancellationToken)
    {
        var configKind = ReadConfigKind(request);
        var configKey = OnlineAdminApiContract.RequireString(request, "ConfigKey");
        var targetVersion = RequireVersion(request, "TargetVersion");
        var reason = OnlineAdminApiContract.RequireString(request, "Reason");
        var entry = _host.LiveOps.Rollback(RolloutKind(configKind), configKey, targetVersion.ToString(System.Globalization.CultureInfo.InvariantCulture),
            reason, "admin", request.ReadRequestId());
        return Task.FromResult<object>(BuildResponse(entry, 0));
    }

    /// <summary>
    /// 读取必填正整数标识。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">字段名。</param>
    /// <returns>标识。</returns>
    private static long RequireId(OnlineAdminApiRequest request, string name)
    {
        var value = request.ReadNullableInt64(name);
        if (value.HasValue && value.Value > 0)
        {
            return value.Value;
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, name + " must be a positive number.");
    }

    /// <summary>
    /// 读取必填正版本号。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <param name="name">字段名。</param>
    /// <returns>版本号。</returns>
    private static int RequireVersion(OnlineAdminApiRequest request, string name)
    {
        var value = request.ReadNullableInt32(name);
        if (value.HasValue && value.Value > 0)
        {
            return value.Value;
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, name + " must be a positive number.");
    }

    /// <summary>
    /// 读取 ConfigKind（线缆数字 1～4）。
    /// </summary>
    /// <param name="request">请求。</param>
    /// <returns>ConfigKind 数字。</returns>
    private static int ReadConfigKind(OnlineAdminApiRequest request)
    {
        var value = request.ReadNullableInt32("ConfigKind");
        if (value.HasValue && value.Value >= 1 && value.Value <= 4)
        {
            return value.Value;
        }

        throw new OnlineServiceException(OnlineErrorCode.ParameterInvalid, "ConfigKind must be a number in [1,4] (RemoteConfig/Announcement/DeviceGroup/ScheduledTask).");
    }

    /// <summary>
    /// ConfigKind 数字 → 灰度注册表 kind 前缀。
    /// </summary>
    /// <param name="configKind">ConfigKind 数字。</param>
    /// <returns>注册表 kind。</returns>
    private static string RolloutKind(int configKind)
    {
        switch (configKind)
        {
            case 1:
                return "ConfigRollout:RemoteConfig";
            case 2:
                return "ConfigRollout:Announcement";
            case 3:
                return "ConfigRollout:DeviceGroup";
            default:
                return "ConfigRollout:ScheduledTask";
        }
    }

    /// <summary>
    /// 构造 LiveOps 回执（ConfirmVersion = 版本号，缺省回退注册表序号）。
    /// </summary>
    /// <param name="entry">注册表条目。</param>
    /// <param name="segmentId">分群标识（无关命令传 0）。</param>
    /// <returns>LiveOps 回执。</returns>
    private static OnlineLiveOpsResponse BuildResponse(OnlineLiveOpsEntry entry, long segmentId)
    {
        return new OnlineLiveOpsResponse
        {
            ConfirmVersion = string.IsNullOrEmpty(entry.Version)
                ? entry.Sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
                : entry.Version,
            SegmentId = segmentId,
            ServerTime = OnlineAdminApiContract.NowSeconds() * 1000,
        };
    }

    /// <summary>
    /// 分群载荷。
    /// </summary>
    public sealed class SegmentPayload
    {
        /// <summary>获取或设置分群名称。</summary>
        public string Name
        {
            get;
            set;
        }

        /// <summary>获取或设置条件原文。</summary>
        public string Conditions
        {
            get;
            set;
        }

        /// <summary>获取或设置原因。</summary>
        public string Reason
        {
            get;
            set;
        }
    }

    /// <summary>
    /// 灰度载荷。
    /// </summary>
    public sealed class RolloutPayload
    {
        /// <summary>获取或设置 ConfigKind（1=RemoteConfig/2=Announcement/3=DeviceGroup/4=ScheduledTask）。</summary>
        public int ConfigKind
        {
            get;
            set;
        }

        /// <summary>获取或设置分群标识。</summary>
        public long SegmentId
        {
            get;
            set;
        }

        /// <summary>获取或设置灰度百分比（0～100）。</summary>
        public int Percent
        {
            get;
            set;
        }

        /// <summary>获取或设置原因。</summary>
        public string Reason
        {
            get;
            set;
        }
    }

    /// <summary>
    /// LiveOps 回执（对齐 Admin OnlineLiveOpsResponse）。
    /// </summary>
    public sealed class OnlineLiveOpsResponse
    {
        /// <summary>获取或设置确认版本。</summary>
        public string ConfirmVersion
        {
            get;
            set;
        }

        /// <summary>获取或设置分群标识（未返回为 0）。</summary>
        public long SegmentId
        {
            get;
            set;
        }

        /// <summary>获取或设置消息（未返回为 null）。</summary>
        public string Message
        {
            get;
            set;
        }

        /// <summary>获取或设置服务端时刻（UTC 毫秒）。</summary>
        public long ServerTime
        {
            get;
            set;
        }
    }
}
