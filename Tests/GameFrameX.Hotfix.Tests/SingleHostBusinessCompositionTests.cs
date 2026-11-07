using System.Reflection;
using GameFrameX.Apps.ServerRole.Chat.Component;
using GameFrameX.Apps.ServerRole.Chat.Entity;
using GameFrameX.Apps.ServerRole.Guild.Component;
using GameFrameX.Apps.ServerRole.Guild.Entity;
using GameFrameX.Apps.ServerRole.Mail.Component;
using GameFrameX.Apps.ServerRole.Mail.Entity;
using GameFrameX.Apps.ServerRole.Team.Component;
using GameFrameX.Apps.ServerRole.Team.Entity;
using GameFrameX.Core.Actors;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Sqlite;
using GameFrameX.Hotfix.Logic.ServerRole.Chat;
using GameFrameX.Hotfix.Logic.ServerRole.Guild;
using GameFrameX.Hotfix.Logic.ServerRole.Mail;
using GameFrameX.Hotfix.Logic.ServerRole.Team;
using GameFrameX.Proto.Proto;
using GameFrameX.Utility;
using GameFrameX.Utility.Setting;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>单 Game 配置下，领域组件和入口共同解析到本地服务器 Actor。</summary>
[Collection(HotfixManagerStaticStateCollection.Name)]
public sealed class SingleHostBusinessCompositionTests
{
    [Fact]
    public async Task SingleGameSetting_RegistersAndExecutesChatMailTeamAndGuildOnOneActor()
    {
        var previousSetting = GlobalSettings.CurrentSetting;
        var databaseName = "single-host-business-" + Guid.NewGuid().ToString("N");
        GlobalSettings.SetCurrentSetting(new AppSetting { ServerId = 9752, ServerType = "Game", IsDebug = true });
        await ComponentRegister.InitAsync(typeof(ChatComponent).Assembly);
        var module = new HotfixModule(typeof(ChatComponentAgent).Assembly);
        HotfixManager.SetResolverForTesting(new HotfixModuleResolverAdapter(module));
        Assert.True(await GameDatabase.ProcessDefault.InitAsync<SqliteDbService>(new DbOptions
        {
            Name = databaseName,
            ConnectionString = "Data Source=:memory:",
            Provider = DatabaseProviderType.Sqlite,
        }));

        var actorIdentifier = ActorIdGenerator.GetActorId(GlobalConst.ActorTypeServer);
        // 此用例仅激活被验证的领域组件，避免引入跨天调度等其它应用启动副作用。
        var actor = new Actor(actorIdentifier, 0) { Type = GlobalConst.ActorTypeServer };
        try
        {
            foreach (var componentType in typeof(ChatComponent).Assembly.GetTypes().Where(type =>
                         !type.IsAbstract && typeof(BaseComponent).IsAssignableFrom(type) &&
                         type.Namespace?.StartsWith("GameFrameX.Apps.ServerRole.", StringComparison.Ordinal) == true))
            {
                Assert.Equal(GlobalConst.ActorTypeServer, ComponentRegister.GetActorType(componentType));
                Assert.Equal(actorIdentifier, ActorIdGenerator.GetActorId(ComponentRegister.GetActorType(componentType)));
                Assert.NotNull(module.GetAgentType(componentType));
            }

            foreach (var handlerType in new[] { typeof(ReqChatJoinHandler), typeof(ReqMailEnqueueHandler), typeof(ReqTeamCreateHandler), typeof(ReqGuildCreateHandler) })
            {
                var handler = Activator.CreateInstance(handlerType, true);
                var initializeActor = handlerType.GetMethod("InitActorAsync", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.True(await (Task<bool>)initializeActor.Invoke(handler, null));
                Assert.Equal(actorIdentifier, handlerType.GetProperty("ActorId", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(handler));
            }

            var chat = await actor.GetComponentAgentAsync<ChatComponentAgent>();
            var mail = await actor.GetComponentAgentAsync<MailComponentAgent>();
            var team = await actor.GetComponentAgentAsync<TeamComponentAgent>();
            var guild = await actor.GetComponentAgentAsync<GuildComponentAgent>();
            Assert.Same(actor, chat.Actor);
            Assert.Same(actor, mail.Actor);
            Assert.Same(actor, team.Actor);
            Assert.Same(actor, guild.Actor);

            await actor.SendAsync((Func<Task>)(async () =>
            {
                var joined = new RespChatJoin();
                await chat.OnJoinAsync(101, new ReqChatJoin { ChannelId = 1 }, joined);
                Assert.Equal(1, joined.MemberCount);
                var spoken = new RespChatSpeak();
                await chat.OnSpeakAsync(101, new ReqChatSpeak { ChannelId = 1, Text = "local message" }, spoken);
                Assert.Equal(0, spoken.ErrorCode);
                var history = new RespChatPullHistory();
                await chat.OnPullHistoryAsync(101, new ReqChatPullHistory { ChannelId = 1, Limit = 10 }, history);
                Assert.Equal("local message", Assert.Single(history.Messages).Text);

                var enqueued = new RespMailEnqueue();
                await mail.OnEnqueueAsync(101, new ReqMailEnqueue { ToPlayerId = 102, Title = "local mail", Body = "delivery" }, enqueued);
                Assert.Equal(0, enqueued.ErrorCode);
                var pending = new RespMailQueryPending();
                await mail.OnQueryPendingAsync(new ReqMailQueryPending { ToPlayerId = 102, Limit = 10 }, pending);
                Assert.Equal(enqueued.MailId, Assert.Single(pending.Mails).MailId);

                var createdTeam = new RespTeamCreate();
                await team.OnCreateAsync(101, new ReqTeamCreate { Name = "Local team", MaxMemberCount = 4 }, createdTeam);
                Assert.Equal(0, createdTeam.ErrorCode);
                var joinedTeam = new RespTeamJoin();
                await team.OnJoinAsync(102, new ReqTeamJoin { TeamId = createdTeam.TeamId }, joinedTeam);
                Assert.Equal(2, joinedTeam.MemberCount);

                var createdGuild = new RespGuildCreate();
                await guild.OnCreateAsync(101, new ReqGuildCreate { Name = "Local guild" }, createdGuild);
                Assert.Equal(0, createdGuild.ErrorCode);
                var queriedGuild = new RespGuildQuery();
                await guild.OnQueryAsync(new ReqGuildQuery { GuildId = createdGuild.GuildId }, queriedGuild);
                Assert.Equal("Local guild", queriedGuild.Name);
                Assert.Equal(101, queriedGuild.LeaderId);
            }), checkLock: true);
            await actor.SaveAllStateAsync();
            Assert.Single((await GameDb.FindAsync<ChatState>(actorIdentifier)).Channels);
            Assert.Single((await GameDb.FindAsync<MailState>(actorIdentifier)).Mails);
            Assert.Single((await GameDb.FindAsync<TeamState>(actorIdentifier)).Teams);
            Assert.Single((await GameDb.FindAsync<GuildState>(actorIdentifier)).Guilds);
        }
        finally
        {
            await actor.InactiveAsync();
            actor.Dispose();
            await GameDatabase.ProcessDefault.CloseAsync();
            HotfixManager.SetResolverForTesting(null);
            if (previousSetting != null)
            {
                GlobalSettings.SetCurrentSetting(previousSetting);
            }
            else
            {
                typeof(GlobalSettings).GetMethod("ResetCurrentSetting", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            }
        }
    }
}
