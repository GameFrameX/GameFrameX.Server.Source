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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织及其贡献者不承担任何责任，开发者应自行承担。
//   The organization and contributors assume no responsibility; developers shall bear the responsibility themselves.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:    https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Apps.Player.Friend.Entity;
using GameFrameX.Apps.ServerRole.Friend.Entity;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Events;
using GameFrameX.Capability.Online.Abstractions.Idempotency;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Foundation.Session;
using GameFrameX.Capability.Online.Social;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Launcher.Online;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 旧好友数据一次性迁移测试（C244 T3）：域 120 旧链 FriendRelationState 与域 660 旧 FriendState
/// 档案经真实写路径收敛为 Online 好友关系；只读单向复制（旧数据零改动）、幂等重放、软删行不迁移。
/// </summary>
/// <remarks>
/// Tests for the one-shot legacy friend-data migration (C244 T3): the legacy domain-120
/// FriendRelationState rows and the legacy domain-660 FriendState profiles converge into Online
/// friendships through the real write path; the migration is a read-only one-way copy (legacy data
/// untouched), idempotent on replay, and skips soft-deleted rows.
/// </remarks>
[Collection("GameDbStaticStateCollection")]
public sealed class OnlineFriendLegacyMigrationTests
{
    /// <summary>测试作用域租户标识。</summary>
    private const long TenantId = 9L;

    /// <summary>测试作用域 App 标识。</summary>
    private const long AppId = 4L;

    /// <summary>测试作用域区服标识。</summary>
    private const long ServerId = 1001;

    /// <summary>
    /// 门面初始化后必须归还（关闭 + 复位静态注册表；串行集合保证静态门面独占）。
    /// </summary>
    private static async Task WithSqliteFacadeAsync(Func<Task> action)
    {
        var opened = await GameDb.InitAsync<SqliteDbService>(new DbOptions
        {
            ConnectionString = "Data Source=:memory:",
            Name = "c244-friend-migration-tests",
        });
        Assert.True(opened);
        try
        {
            await action();
        }
        finally
        {
            await GameDb.CloseAsync();
            GameDb.ResetForTesting();
        }
    }

    /// <summary>
    /// 构造已装配 social 运行时的宿主（好友 store 保持 InMemory 默认，迁移写路径与生产一致）。
    /// </summary>
    private static CapabilityHost BuildSocialHost()
    {
        var builder = new CapabilityHostBuilder();
        builder.UseService(new OnlineScope(TenantId, AppId, ServerId));
        var eventPublisher = new OnlineEventPublisher(new GameFrameX.Foundation.Idempotency.InMemoryEventPublisher());
        builder.UseService<IOnlineEventPublisher>(eventPublisher);
        builder.UseService(new OnlineIdempotencyService(new GameFrameX.Foundation.Idempotency.IdempotencyCoordinator(
            new GameFrameX.Foundation.Idempotency.InMemoryIdempotencyStore(),
            GameFrameX.Foundation.Idempotency.SystemClock.Instance,
            new GameFrameX.Foundation.Idempotency.IdempotencyOptions())));
        var sessionStore = new InMemoryOnlineSessionStore();
        builder.UseService<IOnlineSessionStore>(sessionStore);
        builder.UseService(new OnlineSessionTokenService(sessionStore, eventPublisher));
        builder.AddModule(new FoundationStubModule());
        builder.AddOnlineSocialRuntime();
        return builder.Build(new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId });
    }

    /// <summary>
    /// Foundation 桩模块（满足 online-social 的 Requires 拓扑）。
    /// </summary>
    private sealed class FoundationStubModule : ICapabilityModule
    {
        /// <inheritdoc />
        public CapabilityManifest Manifest { get; } = new("online-foundation", "1.0.0", Array.Empty<string>());

        /// <inheritdoc />
        public void Register(CapabilityRegistrationContext context)
        {
        }
    }

    /// <summary>
    /// 预置旧数据：一条已建立关系（11-22，Status=0）、一条软删关系（33-44，Status=1）、
    /// 一份 660 档案（55 → Outgoing[66]）。
    /// </summary>
    private static async Task SeedLegacyDataAsync()
    {
        await GameDb.AddOrUpdateAsync(new FriendRelationState { Id = 1, PlayerIdA = 11, PlayerIdB = 22, Status = 0, CreateTime = DateTime.UtcNow, CreatedBy = 11 });
        await GameDb.AddOrUpdateAsync(new FriendRelationState { Id = 2, PlayerIdA = 33, PlayerIdB = 44, Status = 1, CreateTime = DateTime.UtcNow, CreatedBy = 33 });

        var legacyProfiles = new FriendState { Id = 3 };
        legacyProfiles.Profiles[55] = new FriendProfileState { PlayerId = 55 };
        legacyProfiles.Profiles[55].Outgoing.Add(66);
        await GameDb.AddOrUpdateAsync(legacyProfiles);
    }

    /// <summary>
    /// 迁移收敛：已建立对成友、待答申请入列、软删行不迁移、旧数据零改动。
    /// </summary>
    [Fact]
    public async Task Migrate_ConvergesLegacyData_WithoutTouchingLegacyRows()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await SeedLegacyDataAsync();
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();
            var service = host.Services.GetService<OnlineFriendService>();

            var (established, pending, failed) = await OnlineFriendLegacyMigration.MigrateAsync(host);

            Assert.Equal(1, established);
            Assert.Equal(1, pending);
            Assert.Equal(0, failed);

            // 11-22 已成友；55-66 待答；33-44（软删）无关系。
            var friendPair = await service.FindAsync(new OnlineScope(TenantId, AppId, ServerId, 11), 22);
            Assert.True(friendPair.IsSuccess);
            Assert.NotNull(friendPair.Data);
            Assert.Equal(OnlineFriendshipState.Accepted, friendPair.Data.State);
            var pendingPair = await service.FindAsync(new OnlineScope(TenantId, AppId, ServerId, 55), 66);
            Assert.True(pendingPair.IsSuccess);
            Assert.NotNull(pendingPair.Data);
            Assert.Equal(OnlineFriendshipState.Requested, pendingPair.Data.State);
            var deletedPair = await service.FindAsync(new OnlineScope(TenantId, AppId, ServerId, 33), 44);
            Assert.True(deletedPair.IsSuccess);
            Assert.Null(deletedPair.Data);

            // 旧数据零改动：行数、状态、档案内容原样。
            var relations = await GameDb.FindListAsync<FriendRelationState>(state => true);
            Assert.Equal(2, relations.Count);
            Assert.Contains(relations, r => r.PlayerIdA == 11 && r.PlayerIdB == 22 && r.Status == 0);
            Assert.Contains(relations, r => r.PlayerIdA == 33 && r.PlayerIdB == 44 && r.Status == 1);
            var profiles = await GameDb.FindListAsync<FriendState>(state => state.Id >= 0);
            var profile = Assert.Single(profiles);
            Assert.True(profile.Profiles.TryGetValue(55, out var legacy));
            Assert.Equal(new List<long> { 66 }, legacy.Outgoing);
        });
    }

    /// <summary>
    /// 迁移幂等：重放不产生第二条关系、不重复计数失败（SaveIfAbsent / 幂等收敛）。
    /// </summary>
    [Fact]
    public async Task Migrate_Replay_IsIdempotent()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await SeedLegacyDataAsync();
            var host = BuildSocialHost();
            var domain = host.Services.GetService<IOnlineFriendDomain>();

            await OnlineFriendLegacyMigration.MigrateAsync(host);
            var (established, pending, failed) = await OnlineFriendLegacyMigration.MigrateAsync(host);

            Assert.Equal(1, established);
            Assert.Equal(1, pending);
            Assert.Equal(0, failed);

            var relations = await domain.ListRelationsAsync(new OnlineScope(TenantId, AppId, ServerId, 11));
            Assert.Single(relations.Data);
            var outgoing = await domain.ListRelationsAsync(new OnlineScope(TenantId, AppId, ServerId, 55));
            var only = Assert.Single(outgoing.Data);
            Assert.Equal(66, only.OtherPlayerId);
            Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, only.Kind);
        });
    }
}
