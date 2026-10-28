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
using GameFrameX.DataBase;
using GameFrameX.DataBase.Abstractions;
using Xunit;

namespace GameFrameX.Tests.DataBase;

/// <summary>
/// GameDb.CloseAsync 关停隔离回归测试：单个库 Close 抛出不得阻断其余库关停，
/// 门面仍完成卸载并保留原始异常供调用方观察。
/// </summary>
/// <remarks>
/// Regression tests for GameDb.CloseAsync shutdown isolation: a throwing Close must not stop the
/// remaining databases from closing, the facade still tears down, and the original exception
/// propagates to the caller.
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class GameDbCloseIsolationTests
{
    private class CloseBehaviorProxy : DispatchProxy
    {
        public Func<Task> CloseHandler { get; set; }

        protected override object Invoke(MethodInfo targetMethod, object[] args)
        {
            if (targetMethod.Name == nameof(IDatabaseService.CloseAsync))
            {
                return CloseHandler();
            }

            throw new NotSupportedException("CloseBehaviorProxy only supports CloseAsync.");
        }
    }

    private static IDatabaseService CreateService(Func<Task> closeHandler)
    {
        object proxy = CreateProxy(typeof(IDatabaseService), typeof(CloseBehaviorProxy));
        ((CloseBehaviorProxy)proxy).CloseHandler = closeHandler;
        return (IDatabaseService)proxy;
    }
    private static object CreateProxy(Type interfaceType, Type proxyType)
    {
        var createMethod = typeof(DispatchProxy).GetMethods()
            .Single(method => method.Name == nameof(DispatchProxy.Create) && method.IsGenericMethodDefinition
                              && method.GetGenericArguments().Length == 2 && method.GetParameters().Length == 0);
        return createMethod.MakeGenericMethod(interfaceType, proxyType).Invoke(null, null);
    }

    private static void SetDefaultImplementation(IDatabaseService service)
    {
        var field = typeof(GameDb).GetField("_dbServiceImplementation", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(field);
        field.SetValue(null, service);
    }

    [Fact]
    public async Task CloseAsync_ThrowingService_DoesNotBlockRemainingCloses()
    {
        GameDb.ResetForTesting();
        var healthyClosed = false;
        var shutdownFailure = new InvalidOperationException("close failed");
        try
        {
            var failing = CreateService(() => Task.FromException(shutdownFailure));
            var healthy = CreateService(() => { healthyClosed = true; return Task.CompletedTask; });
            MultiDbRegistry.Register("failing_library", failing);
            MultiDbRegistry.Register("healthy_library", healthy);
            SetDefaultImplementation(healthy);

            var thrown = await Assert.ThrowsAsync<InvalidOperationException>(() => GameDb.CloseAsync());

            Assert.Same(shutdownFailure, thrown);
            Assert.True(healthyClosed, "后续库的 Close 必须仍被调用");
            Assert.Equal(0, MultiDbRegistry.RegisteredCount);
        }
        finally
        {
            GameDb.ResetForTesting();
        }
    }
}
