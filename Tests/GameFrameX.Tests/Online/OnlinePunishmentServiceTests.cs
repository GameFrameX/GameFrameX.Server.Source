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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository: https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official documentation: https://gameframex.doc.alianblank.com/
// ==========================================================================================

using System;
using System.Threading.Tasks;
using GameFrameX.Online.Contracts;
using GameFrameX.Online.Events;
using GameFrameX.Online.Social;
using Xunit;

namespace GameFrameX.Tests.Online
{
    /// <summary>
    /// 处罚服务测试（vault:C7 S6.3 处罚归属：Admin 命令面写处罚、裁决面只读生效处罚）。
    /// <para>
    /// 本类锁定的是「处罚作为账号级事实」的写入与撤销语义：生效窗口的三种状态（未到 / 生效中 / 已过期）、
    /// 撤销的即时性与留档、以及施加处罚的鉴权与参数守卫。
    /// </para>
    /// </summary>
    public class OnlinePunishmentServiceTests
    {
        /// <summary>测试用租户标识。</summary>
        private const long TenantId = 1;

        /// <summary>测试用应用标识。</summary>
        private const long AppId = 10;

        /// <summary>测试用被处罚玩家标识。</summary>
        private const long PlayerId = 1002;

        /// <summary>测试用 Admin 标识。</summary>
        private const long AdminId = 9001;

        /// <summary>
        /// 验证施加永久处罚后立即出现在生效列表中，且原始记录带有施加者与原因。
        /// </summary>
        [Fact]
        public async Task ApplyAsync_ShouldStorePermanentPunishmentVisibleInActiveList()
        {
            // Arrange
            var service = CreateService(out var recorder);

            // Act
            var outcome = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId, "ADM-9");

            // Assert
            Assert.True(outcome.IsSuccess);
            Assert.Equal(OnlinePunishmentKind.Ban, outcome.Data.Kind);
            Assert.Equal("使用外挂", outcome.Data.Reason);
            Assert.Equal(AdminId, outcome.Data.CreatedByAdminId);
            Assert.Equal("ADM-9", outcome.Data.AdminCaseId);
            Assert.False(outcome.Data.Revoked);
            Assert.Equal(0, outcome.Data.ExpiresAtTime);

            var active = await service.ListActiveAsync(TenantId, AppId, PlayerId);
            Assert.True(active.IsSuccess);
            Assert.Single(active.Data);
            Assert.Equal(outcome.Data.PunishmentId, active.Data[0].PunishmentId);

            var events = recorder.Filter(OnlineSocialEvents.PunishmentChanged);
            Assert.Single(events);
            Assert.Equal(OnlineSocialEvents.PunishmentApplied, events[0].PayloadAuditFields["Action"]);
        }

        /// <summary>
        /// 验证限时处罚在窗口内生效、窗口外不再生效（三种时刻各判一次）。
        /// </summary>
        [Fact]
        public async Task ListActiveAsync_ShouldRespectEffectiveWindow()
        {
            // Arrange
            var service = CreateService(out _);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            var effective = now - 1000;
            var expires = now + 60000;
            var applied = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Mute, "刷屏", effective, expires, AdminId);
            Assert.True(applied.IsSuccess);

            // Act & Assert
            var beforeEffective = await service.ListActiveAsync(TenantId, AppId, PlayerId, effective - 1000);
            var duringWindow = await service.ListActiveAsync(TenantId, AppId, PlayerId, now);
            var afterExpiry = await service.ListActiveAsync(TenantId, AppId, PlayerId, expires + 1000);

            Assert.Empty(beforeEffective.Data);
            Assert.Single(duringWindow.Data);
            Assert.Empty(afterExpiry.Data);
        }

        /// <summary>
        /// 验证失效时刻早于或等于生效时刻的处罚被参数拒绝（自始无效的处罚写库只会污染裁决路径）。
        /// </summary>
        [Fact]
        public async Task ApplyAsync_WhenExpiryNotAfterEffective_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            // Act
            var equal = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Mute, "刷屏", now, now, AdminId);
            var earlier = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Mute, "刷屏", now, now - 1000, AdminId);

            // Assert
            Assert.False(equal.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, equal.Code);
            Assert.False(earlier.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, earlier.Code);
        }

        /// <summary>
        /// 验证缺少原因或缺少管理员标识时被参数拒绝（无原因的处罚不可申诉，无施加者的处罚不可追责）。
        /// </summary>
        [Fact]
        public async Task ApplyAsync_WhenReasonOrAdminMissing_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var noReason = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Ban, "   ", 0, 0, AdminId);
            var noAdmin = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, 0);

            // Assert
            Assert.False(noReason.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noReason.Code);
            Assert.False(noAdmin.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noAdmin.Code);
        }

        /// <summary>
        /// 验证定位参数非法（租户 / App / 玩家任一无效）时被参数拒绝。
        /// </summary>
        [Fact]
        public async Task ApplyAsync_WhenLocatorInvalid_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var noTenant = await service.ApplyAsync(0, AppId, PlayerId, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId);
            var noPlayer = await service.ApplyAsync(TenantId, AppId, 0, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId);

            // Assert
            Assert.False(noTenant.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noTenant.Code);
            Assert.False(noPlayer.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, noPlayer.Code);
        }

        /// <summary>
        /// 验证撤销后处罚即时退出生效列表，但**仍在历史中留档**（撤销不是删除，审计要能追）。
        /// </summary>
        [Fact]
        public async Task RevokeAsync_ShouldRemoveFromActiveButKeepHistoryAndPublishEvent()
        {
            // Arrange
            var service = CreateService(out var recorder);
            var applied = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId);
            Assert.True(applied.IsSuccess);

            // Act
            var revoked = await service.RevokeAsync(TenantId, AppId, applied.Data.PunishmentId, AdminId);

            // Assert
            Assert.True(revoked.IsSuccess);
            Assert.True(revoked.Data.Revoked);
            Assert.Equal(AdminId, revoked.Data.RevokedByAdminId);
            Assert.True(revoked.Data.RevokedAtTime > 0);

            var active = await service.ListActiveAsync(TenantId, AppId, PlayerId);
            Assert.Empty(active.Data);

            var history = await service.ListHistoryAsync(TenantId, AppId, PlayerId);
            Assert.Single(history.Data);
            Assert.True(history.Data[0].Revoked);

            var revokedEvents = recorder.Filter(OnlineSocialEvents.PunishmentChanged);
            Assert.Equal(2, revokedEvents.Count);
            Assert.Equal(OnlineSocialEvents.PunishmentRevoked, revokedEvents[1].PayloadAuditFields["Action"]);
        }

        /// <summary>
        /// 验证撤销不存在的处罚返回未找到，而不是静默成功（静默成功会让 Admin 以为处罚已解除）。
        /// </summary>
        [Fact]
        public async Task RevokeAsync_WhenPunishmentMissing_ShouldReturnResourceNotFound()
        {
            // Arrange
            var service = CreateService(out _);

            // Act
            var outcome = await service.RevokeAsync(TenantId, AppId, "pun-missing", AdminId);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ResourceNotFound, outcome.Code);
        }

        /// <summary>
        /// 验证撤销缺少管理员标识时被参数拒绝（撤销同样要留痕追责）。
        /// </summary>
        [Fact]
        public async Task RevokeAsync_WhenAdminMissing_ShouldDenyAsInvalid()
        {
            // Arrange
            var service = CreateService(out _);
            var applied = await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Ban, "使用外挂", 0, 0, AdminId);
            Assert.True(applied.IsSuccess);

            // Act
            var outcome = await service.RevokeAsync(TenantId, AppId, applied.Data.PunishmentId, 0);

            // Assert
            Assert.False(outcome.IsSuccess);
            Assert.Equal(OnlineErrorCode.ParameterInvalid, outcome.Code);
        }

        /// <summary>
        /// 验证历史列表保留全部处罚（含仍生效与已撤销），不因生效窗口而过滤。
        /// </summary>
        [Fact]
        public async Task ListHistoryAsync_ShouldReturnPunishmentsRegardlessOfWindow()
        {
            // Arrange
            var service = CreateService(out _);
            var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            Assert.True((await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Mute, "刷屏", 0, 0, AdminId)).IsSuccess);
            Assert.True((await service.ApplyAsync(TenantId, AppId, PlayerId, OnlinePunishmentKind.Ban, "历史封禁", now - 100000, now - 50000, AdminId)).IsSuccess);

            // Act
            var history = await service.ListHistoryAsync(TenantId, AppId, PlayerId);

            // Assert
            Assert.True(history.IsSuccess);
            Assert.Equal(2, history.Data.Count);
        }

        /// <summary>
        /// 验证处罚的生效判定（<see cref="OnlinePunishment.IsActiveAt"/>）在边界上的一致语义。
        /// </summary>
        [Fact]
        public void IsActiveAt_ShouldJudgeWindowAndRevocation()
        {
            // Arrange
            var punishment = new OnlinePunishment
            {
                PunishmentId = "pun-1",
                TenantId = TenantId,
                AppId = AppId,
                PlayerId = PlayerId,
                Kind = OnlinePunishmentKind.Mute,
                Reason = "刷屏",
                EffectiveAtTime = 1000,
                ExpiresAtTime = 2000,
                Revoked = false,
            };

            // Act & Assert
            Assert.False(punishment.IsActiveAt(999));
            Assert.True(punishment.IsActiveAt(1000));
            Assert.True(punishment.IsActiveAt(1999));
            Assert.False(punishment.IsActiveAt(2000));
            Assert.False(punishment.IsActiveAt(2001));

            // 撤销后任何时刻都不生效，与窗口无关。
            punishment.Revoked = true;
            Assert.False(punishment.IsActiveAt(1500));

            // 永久处罚（失效时刻为 0）在生效之后的任何时刻都生效。
            punishment.Revoked = false;
            punishment.ExpiresAtTime = 0;
            Assert.True(punishment.IsActiveAt(1000));
            Assert.True(punishment.IsActiveAt(long.MaxValue));
        }

        /// <summary>
        /// 创建被测处罚服务。
        /// </summary>
        /// <param name="recorder">事件记录桩。</param>
        /// <returns>处罚服务实例。</returns>
        private static OnlinePunishmentService CreateService(out OnlineEventRecorder recorder)
        {
            recorder = new OnlineEventRecorder();
            return new OnlinePunishmentService(new InMemoryOnlineSocialGraphStore(), recorder);
        }
    }
}
