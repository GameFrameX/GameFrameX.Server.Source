// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations.
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository:  https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Capability.Online.Persistence.Social;
using GameFrameX.Capability.Online.Social;
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using GameFrameX.Tests.DataBase;
using Xunit;

namespace GameFrameX.Tests.Online;

/// <summary>
/// 好友 store 存量行补全竞态回归测试：存量行（RowVersion = null）补全被并发写者抢先时，
/// 重读到的行必须复检作用域与前置条件——否则非法状态迁移会被写进库。
/// </summary>
/// <remarks>
/// Regression test for the legacy-row backfill race in the friend store: when a concurrent writer
/// wins the legacy <c>RowVersion</c> backfill, the re-read row must be re-validated (scope and
/// precondition) before the transition is applied, otherwise an illegal state transition lands.
/// The database is a scripted facade double, so the race is deterministic.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class GameDbOnlineFriendStoreBackfillRaceTests : IDisposable
{
    private const long TenantId = 1;
    private const long AppId = 10;

    /// <summary>
    /// 构造函数：重置门面静态状态，避免与其它用例互相影响。
    /// </summary>
    public GameDbOnlineFriendStoreBackfillRaceTests()
    {
        GameDb.ResetForTesting();
    }

    /// <summary>
    /// 释放：重置门面静态状态。
    /// </summary>
    public void Dispose()
    {
        GameDb.ResetForTesting();
    }

    /// <summary>
    /// 存量行补全被并发抢先时：重读行的状态已变（不再等于期望态），
    /// 迁移必须按 CAS 失败返回 null，且不得发出任何条件写。
    /// </summary>
    [Fact]
    public async Task UpdateState_BackfillLostToConcurrentWriter_ShouldNotApplyIllegalTransition()
    {
        // 存量旧行（RowVersion = null，状态 Requested）：首次读出的形态。
        var legacyRow = GameDbOnlineFriendStorePersistenceTests.CreateFriendshipRow(9101, "fs-legacy-race", (int)OnlineFriendshipState.Requested, null, 1001);

        // 并发写者已把该行推进到 Rejected（行版本 7）：补全 CAS 失败后重读到的就是这行。
        var winnerRow = GameDbOnlineFriendStorePersistenceTests.CreateFriendshipRow(9101, "fs-legacy-race", (int)OnlineFriendshipState.Rejected, 7, 7000);

        var database = new ScriptedFriendDatabase(legacyRow, winnerRow);
        MultiDbRegistry.Register("friend_race_db", database.Service);
        GameDb.SetDefault("friend_race_db");

        var store = new GameDbOnlineFriendStore();
        var updated = await store.UpdateStateAsync(TenantId, AppId, new FriendshipStateTransition
        {
            FriendshipId = "fs-legacy-race",
            ExpectedState = OnlineFriendshipState.Requested,
            NewState = OnlineFriendshipState.Accepted,
            NowUnixMilliseconds = 5000,
            Responded = true,
        });

        Assert.Null(updated);
        Assert.Empty(database.ConditionalWrites);
    }

    /// <summary>
    /// 脚本化门面替身：按调用序返回预置行，并记录所有条件写调用。
    /// </summary>
    /// <remarks>
    /// A scripted facade double: returns the preset rows in call order and records every
    /// conditional-write call.
    /// </remarks>
    private sealed class ScriptedFriendDatabase
    {
        private readonly Queue<OnlineFriendshipRowState> _readRows;

        public IDatabaseService Service { get; }

        public List<string> ConditionalWrites { get; } = new();

        public ScriptedFriendDatabase(OnlineFriendshipRowState firstRow, OnlineFriendshipRowState secondRow)
        {
            _readRows = new Queue<OnlineFriendshipRowState>(new[] { firstRow, secondRow });
            Service = CreateService(this);
        }

        private static IDatabaseService CreateService(ScriptedFriendDatabase owner)
        {
            object proxy = CreateProxy(typeof(IDatabaseService), typeof(ScriptedServiceProxy));
            ((ScriptedServiceProxy)proxy).Owner = owner;
            return (IDatabaseService)proxy;
        }

        private static object CreateProxy(Type interfaceType, Type proxyType)
        {
            var createMethod = typeof(DispatchProxy).GetMethods()
                .Single(method => method.Name == nameof(DispatchProxy.Create) && method.IsGenericMethodDefinition
                                  && method.GetGenericArguments().Length == 2 && method.GetParameters().Length == 0);
            return createMethod.MakeGenericMethod(interfaceType, proxyType).Invoke(null, null);
        }

        /// <summary>
        /// 取下一条预置行（用尽后返回 null）。
        /// </summary>
        public OnlineFriendshipRowState NextRow()
        {
            return _readRows.Count > 0 ? _readRows.Dequeue() : null;
        }

        /// <summary>
        /// 记录一次条件写调用。
        /// </summary>
        public void RecordConditionalWrite(string description)
        {
            ConditionalWrites.Add(description);
        }

        /// <summary>
        /// 脚本化服务代理：按方法名分派到宿主替身。
        /// </summary>
        private class ScriptedServiceProxy : DispatchProxy
        {
            public ScriptedFriendDatabase Owner;

            protected override object Invoke(MethodInfo targetMethod, object[] args)
            {
                switch (targetMethod.Name)
                {
                    case nameof(IDatabaseService.OpenAsync):
                        return Task.FromResult(true);
                    case nameof(IDatabaseService.CloseAsync):
                        return Task.CompletedTask;
                    case nameof(IDatabaseService.FindAsync):
                    {
                        var row = Owner.NextRow();
                        var resultType = targetMethod.ReturnType.GetGenericArguments()[0];
                        var fromResult = typeof(Task).GetMethod(nameof(Task.FromResult)).MakeGenericMethod(resultType);
                        return fromResult.Invoke(null, new object[] { row });
                    }

                    case nameof(IDatabaseService.UpdateIfRowVersionIsNullAsync):
                        // 存量补全被并发写者抢先：条件写失败。
                        return Task.FromResult(false);
                    case nameof(IDatabaseService.UpdateIfRowVersionAsync):
                    {
                        var expectedRowVersion = (long)args[1];
                        var written = args[2] as OnlineFriendshipRowState;
                        Owner.RecordConditionalWrite("version=" + expectedRowVersion + ",state=" + (written == null ? "null" : written.State.ToString()));
                        return Task.FromResult(true);
                    }

                    default:
                        throw new NotSupportedException("ScriptedFriendDatabase does not implement " + targetMethod.Name + ".");
                }
            }
        }
    }
}
