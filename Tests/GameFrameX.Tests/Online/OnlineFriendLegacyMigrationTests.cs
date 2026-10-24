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
using System.Text.Json;
using System.Threading.Tasks;
using GameFrameX.Apps.Player.Friend.Entity;
using GameFrameX.Apps.ServerRole.Friend.Entity;
using GameFrameX.Capability.Hosting.Abstractions;
using GameFrameX.Capability.Hosting.Core;
using GameFrameX.Capability.Online.Abstractions;
using GameFrameX.Capability.Online.Abstractions.Contracts;
using GameFrameX.Capability.Online.Abstractions.Scope;
using GameFrameX.Capability.Online.Operations.HotfixRollback;
using GameFrameX.Capability.Online.Social;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 旧好友数据一次性迁移测试（C244 T3 → C294 机制化）：域 120 旧链 FriendRelationState 与域 660 旧 FriendState
/// 档案经真实写路径收敛为 Online 好友关系；只读单向复制（旧数据零改动）、幂等重放、软删行不迁移；
/// 配置开关 <c>online-social.Friend.MigrateLegacyFriendData</c> 打开时社交能力在 Activate 期自动执行迁移。
/// </summary>
/// <remarks>
/// Tests for the one-shot legacy friend-data migration (C244 T3, mechanism rewrite C294): the legacy domain-120
/// FriendRelationState rows and the legacy domain-660 FriendState profiles converge into Online
/// friendships through the real write path; the migration is a read-only one-way copy (legacy data
/// untouched), idempotent on replay, skips soft-deleted rows, and runs automatically during activation
/// when the social option switch is on.
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
    /// 组合 foundation + social（族配置段作用域 9/4/1001；好友 store 保持 InMemory 默认，迁移写路径与生产一致）并启动，
    /// 执行用例主体后恒停止撤桥。
    /// </summary>
    /// <param name="body">用例主体（入参：已启动的组合产物）。</param>
    /// <param name="settingsJson">Settings 子对象原文（null = 不写 Settings 键）。</param>
    /// <returns>异步任务。</returns>
    private static async Task WithStartedSocialRuntimeAsync(Func<CapabilityRuntime, Task> body, string settingsJson = null)
    {
        var settings = string.IsNullOrEmpty(settingsJson) ? string.Empty : ",\"Settings\":" + settingsJson;
        var section = JsonDocument.Parse("{\"TenantId\":9,\"AppId\":4,\"ServerId\":1001" + settings + "}").RootElement.Clone();
        var options = new CapabilityCompositionOptions(section, new CollectingCapabilityAssemblyLog())
        {
            HostOptions = new CapabilityHostOptions { TenantId = TenantId, AppId = AppId, ServerId = ServerId, },
            Filter = new CapabilityAssemblyFilter(new[] { "online-foundation", "online-social" }),
        };
        options.Override<IOnlinePlayerDirectory>(new OnlineCapabilityTestHarness.EmptyPlayerDirectory());
        options.Override<IOnlineHotfixRollbackExecutor>(new OnlineCapabilityTestHarness.NoopHotfixRollbackExecutor());

        var runtime = CapabilityComposition.Compose(new OnlineCapabilityFamily(), options);
        try
        {
            await runtime.Host.StartAsync();
            await body(runtime);
        }
        finally
        {
            await runtime.StopAsync();
        }
    }

    /// <summary>
    /// 构造迁移作用域内的玩家作用域。
    /// </summary>
    /// <param name="playerId">玩家标识。</param>
    /// <returns>玩家作用域。</returns>
    private static OnlineScope PlayerScope(long playerId)
    {
        return new OnlineScope(TenantId, AppId, ServerId, playerId);
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
    /// 断言迁移收敛结果：11-22 已成友；55-66 待答；33-44（软删）无关系。
    /// </summary>
    /// <param name="friends">好友服务。</param>
    /// <returns>异步任务。</returns>
    private static async Task AssertConvergedRelationsAsync(OnlineFriendService friends)
    {
        var friendPair = await friends.FindAsync(PlayerScope(11), 22);
        Assert.True(friendPair.IsSuccess);
        Assert.NotNull(friendPair.Data);
        Assert.Equal(OnlineFriendshipState.Accepted, friendPair.Data.State);
        var pendingPair = await friends.FindAsync(PlayerScope(55), 66);
        Assert.True(pendingPair.IsSuccess);
        Assert.NotNull(pendingPair.Data);
        Assert.Equal(OnlineFriendshipState.Requested, pendingPair.Data.State);
        var deletedPair = await friends.FindAsync(PlayerScope(33), 44);
        Assert.True(deletedPair.IsSuccess);
        Assert.Null(deletedPair.Data);
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
            await WithStartedSocialRuntimeAsync(async runtime =>
            {
                var friends = runtime.Host.Services.GetService<OnlineFriendService>();

                var (established, pending, failed) = await OnlineFriendLegacyMigration.MigrateAsync(friends, TenantId, AppId, ServerId);

                Assert.Equal(1, established);
                Assert.Equal(1, pending);
                Assert.Equal(0, failed);
                await AssertConvergedRelationsAsync(friends);

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
        });
    }

    /// <summary>
    /// 迁移幂等：重放不产生第二条关系、不重复计数失败（SaveIfAbsent / 幂等收敛）；好友域桥视角关系唯一。
    /// </summary>
    [Fact]
    public async Task Migrate_Replay_IsIdempotent()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await SeedLegacyDataAsync();
            await WithStartedSocialRuntimeAsync(async runtime =>
            {
                var friends = runtime.Host.Services.GetService<OnlineFriendService>();

                await OnlineFriendLegacyMigration.MigrateAsync(friends, TenantId, AppId, ServerId);
                var (established, pending, failed) = await OnlineFriendLegacyMigration.MigrateAsync(friends, TenantId, AppId, ServerId);

                Assert.Equal(1, established);
                Assert.Equal(1, pending);
                Assert.Equal(0, failed);

                Assert.True(CapabilityBridge<IOnlineFriendDomain>.TryAcquire(out var lease));
                var domain = lease.Contract;
                var relations = await domain.ListRelationsAsync(PlayerScope(11));
                Assert.Single(relations.Data);
                var outgoing = await domain.ListRelationsAsync(PlayerScope(55));
                var only = Assert.Single(outgoing.Data);
                Assert.Equal(66, only.OtherPlayerId);
                Assert.Equal(OnlineFriendRelationKind.OutgoingRequest, only.Kind);
            });
        });
    }

    /// <summary>
    /// 配置开关打开（<c>online-social.Friend.MigrateLegacyFriendData = true</c>）：社交能力在 Activate 期自动执行迁移，
    /// 启动完成即可观测到收敛后的关系。
    /// </summary>
    [Fact]
    public async Task Activate_WithMigrateLegacyFriendDataEnabled_MigratesAutomatically()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await SeedLegacyDataAsync();
            await WithStartedSocialRuntimeAsync(async runtime =>
            {
                var friends = runtime.Host.Services.GetService<OnlineFriendService>();
                await AssertConvergedRelationsAsync(friends);
            }, "{\"online-social\":{\"Friend\":{\"MigrateLegacyFriendData\":true}}}");
        });
    }

    /// <summary>
    /// 配置开关缺省关闭：启动不触发迁移（旧数据在场，Online 侧仍无关系）。
    /// </summary>
    [Fact]
    public async Task Activate_WithMigrateLegacyFriendDataDefault_DoesNotMigrate()
    {
        await WithSqliteFacadeAsync(async () =>
        {
            await SeedLegacyDataAsync();
            await WithStartedSocialRuntimeAsync(async runtime =>
            {
                var friends = runtime.Host.Services.GetService<OnlineFriendService>();
                var friendPair = await friends.FindAsync(PlayerScope(11), 22);
                Assert.True(friendPair.IsSuccess);
                Assert.Null(friendPair.Data);
            });
        });
    }
}
