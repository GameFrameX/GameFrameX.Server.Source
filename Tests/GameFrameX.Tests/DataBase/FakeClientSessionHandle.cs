// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and relevant international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legal rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository: https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================


using System;
using System.Threading;
using System.Threading.Tasks;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Bindings;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// IClientSessionHandle 手写测试桩：记录提交/中止调用并可注入两者异常，其余成员不可用。
/// </summary>
/// <remarks>
/// Hand-rolled <c>IClientSessionHandle</c> test double (the test project uses no mocking
/// library): it records commit/abort invocations (including the abort cancellation token) and
/// can inject exceptions for either call; every unrelated member throws
/// <see cref="NotSupportedException" /> so accidental use fails loudly.
/// </remarks>
public sealed class FakeClientSessionHandle : IClientSessionHandle
{
    /// <summary>
    /// AbortTransactionAsync 被调用的次数。
    /// </summary>
    public int AbortTransactionCallCount;

    /// <summary>
    /// CommitTransactionAsync 被调用的次数。
    /// </summary>
    public int CommitTransactionCallCount;

    /// <summary>
    /// 最近一次 AbortTransactionAsync 收到的取消令牌。
    /// </summary>
    public CancellationToken LastAbortTransactionCancellationToken;

    /// <summary>
    /// 注入到 CommitTransactionAsync 的异常（null 表示提交成功）。
    /// </summary>
    public Exception CommitTransactionException;

    /// <summary>
    /// 注入到 AbortTransactionAsync 的异常（null 表示中止成功）。
    /// </summary>
    public Exception AbortTransactionException;

    /// <summary>
    /// 所属客户端（本桩不可用）。
    /// </summary>
    public IMongoClient Client => throw new NotSupportedException();

    /// <summary>
    /// 集群时间（本桩不可用）。
    /// </summary>
    public BsonDocument ClusterTime => throw new NotSupportedException();

    /// <summary>
    /// 是否处于事务中（本桩不可用）。
    /// </summary>
    public bool IsInTransaction => throw new NotSupportedException();

    /// <summary>
    /// 操作时间（本桩不可用）。
    /// </summary>
    public BsonTimestamp OperationTime => throw new NotSupportedException();

    /// <summary>
    /// 是否隐式会话（本桩不可用）。
    /// </summary>
    public bool IsImplicit => throw new NotSupportedException();

    /// <summary>
    /// 会话选项（本桩不可用）。
    /// </summary>
    public ClientSessionOptions Options => throw new NotSupportedException();

    /// <summary>
    /// 服务端会话（本桩不可用）。
    /// </summary>
    public IServerSession ServerSession => throw new NotSupportedException();

    /// <summary>
    /// 被包装的核心会话句柄（本桩不可用）。
    /// </summary>
    public ICoreSessionHandle WrappedCoreSession => throw new NotSupportedException();

    /// <summary>
    /// 分叉会话（本桩不可用）。
    /// </summary>
    public IClientSessionHandle Fork()
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 开启事务（本桩不可用）。
    /// </summary>
    public void StartTransaction(TransactionOptions transactionOptions = null)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 推进集群时间（本桩不可用）。
    /// </summary>
    public void AdvanceClusterTime(BsonDocument clusterTime)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 推进操作时间（本桩不可用）。
    /// </summary>
    public void AdvanceOperationTime(BsonTimestamp operationTime)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 以回调形式执行事务（本桩不可用）。
    /// </summary>
    public TResult WithTransaction<TResult>(Func<IClientSessionHandle, CancellationToken, TResult> callback, TransactionOptions transactionOptions = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 以异步回调形式执行事务（本桩不可用）。
    /// </summary>
    public Task<TResult> WithTransactionAsync<TResult>(Func<IClientSessionHandle, CancellationToken, Task<TResult>> callbackAsync, TransactionOptions transactionOptions = null, CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 提交事务（同步形态，本桩不可用）。
    /// </summary>
    public void CommitTransaction(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 异步提交事务：记录调用次数并按注入异常返回。
    /// </summary>
    public Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        CommitTransactionCallCount++;
        return CommitTransactionException == null ? Task.CompletedTask : Task.FromException(CommitTransactionException);
    }

    /// <summary>
    /// 中止事务（同步形态，本桩不可用）。
    /// </summary>
    public void AbortTransaction(CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException();
    }

    /// <summary>
    /// 异步中止事务：记录调用次数与取消令牌并按注入异常返回。
    /// </summary>
    public Task AbortTransactionAsync(CancellationToken cancellationToken = default)
    {
        AbortTransactionCallCount++;
        LastAbortTransactionCancellationToken = cancellationToken;
        return AbortTransactionException == null ? Task.CompletedTask : Task.FromException(AbortTransactionException);
    }

    /// <summary>
    /// 释放会话（本桩无资源可释放）。
    /// </summary>
    public void Dispose()
    {
    }
}
