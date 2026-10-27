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
using GameFrameX.DataBase.Abstractions;
using GameFrameX.DataBase.Mongo;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// MongoDbService 事务提交最终失败后的显式中止行为测试。
/// </summary>
/// <remarks>
/// Regression tests for the explicit transaction abort after a finally-failed commit: when the
/// commit is given up (non-retryable error — no real server involved, the session is a hand-rolled
/// fake), the shell must call <c>AbortTransactionAsync</c> explicitly with
/// <c>CancellationToken.None</c> instead of relying on session disposal, and an abort failure must
/// never swallow or replace the original commit failure.
/// </remarks>
public sealed class MongoDbServiceTransactionAbortTests
{
    /// <summary>
    /// 提交抛不可重试异常：显式中止事务恰被调用一次，最终异常仍以提交异常为内层。
    /// </summary>
    [Fact]
    public async Task CommitTransaction_NonRetryableFailure_ShouldAbortTransactionExplicitly()
    {
        var commitException = new InvalidOperationException("commit boom");
        var session = new FakeClientSessionHandle { CommitTransactionException = commitException };
        var service = new MongoDbService();

        var thrown = await Assert.ThrowsAsync<DatabaseUnavailableException>(
            () => service.CommitTransactionWithRetryAsync(session, CancellationToken.None));

        Assert.Equal(1, session.CommitTransactionCallCount);
        Assert.Equal(1, session.AbortTransactionCallCount);
        Assert.Equal(CancellationToken.None, session.LastAbortTransactionCancellationToken);
        Assert.Same(commitException, thrown.InnerException);
    }

    /// <summary>
    /// 提交与中止均失败：中止失败不吞掉也不替换原始提交异常（仅记 Warning 日志）。
    /// </summary>
    [Fact]
    public async Task CommitTransaction_NonRetryableFailure_WhenAbortAlsoFails_ShouldKeepOriginalException()
    {
        var commitException = new InvalidOperationException("commit boom");
        var abortException = new InvalidOperationException("abort boom");
        var session = new FakeClientSessionHandle
        {
            CommitTransactionException = commitException,
            AbortTransactionException = abortException,
        };
        var service = new MongoDbService();

        var thrown = await Assert.ThrowsAsync<DatabaseUnavailableException>(
            () => service.CommitTransactionWithRetryAsync(session, CancellationToken.None));

        Assert.Equal(1, session.AbortTransactionCallCount);
        Assert.Same(commitException, thrown.InnerException);
        Assert.NotSame(abortException, thrown.InnerException);
        Assert.NotSame(abortException, thrown);
    }

    /// <summary>
    /// 提交成功：不触发任何中止调用（正常路径零行为变化）。
    /// </summary>
    [Fact]
    public async Task CommitTransaction_Success_ShouldNeverAbort()
    {
        var session = new FakeClientSessionHandle();
        var service = new MongoDbService();

        await service.CommitTransactionWithRetryAsync(session, CancellationToken.None);

        Assert.Equal(1, session.CommitTransactionCallCount);
        Assert.Equal(0, session.AbortTransactionCallCount);
    }
}
