// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and applicable international regulations.
//   使用本项目须严格遵守相应法律法规与开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please refer to the LICENSE file at the root of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes or liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne by the developer; the project organization and contributors assume no responsibility.
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
using System.Collections.Generic;
using System.Threading.Tasks;
using GameFrameX.Discovery;
using Xunit;
using GameFrameX.Tests.DataBase;

namespace GameFrameX.Tests.Discovery;

/// <summary>
/// InProcDiscoveryRuntime 契约测试（单进程拓扑：自指路由表 + no-op 心跳面）。
/// </summary>
/// <remarks>
/// Contract tests for <c>InProcDiscoveryRuntime</c> (the single-process topology): the
/// self-referential route table resolves every hosted role to one Active self instance, activation
/// is idempotent, validation mirrors the provider runtimes, and <c>MarkActiveAsync</c> is a no-op.
/// Joined into the GameDbStaticStateCollection to avoid cross-class parallel races on the static
/// discovery slots (the isolation stance shared by the sibling discovery suites).
/// </remarks>
[Collection(nameof(GameDbStaticStateCollection))]
public sealed class InProcDiscoveryRuntimeTests
{
    /// <summary>
    /// 测试激活后自指路由表：每个 HostedRole 的 Role 视图解析到唯一 Active 自指实例。
    /// </summary>
    [Fact]
    public void Activate_MultipleRoles_ShouldResolveEachRoleToSingleActiveSelfInstance()
    {
        ResetAndRun(() =>
        {
            InProcDiscoveryRuntime.Activate(CreateOptions("Game", "Gateway"));

            var provider = InProcDiscoveryRuntime.TableProvider;
            Assert.NotNull(provider);
            Assert.NotNull(provider.Current);

            foreach (var role in new[] { "Game", "Gateway" })
            {
                var instances = provider.Current.GetActiveInstances(role);
                Assert.Single(instances);
                Assert.Equal(role, instances[0].Role);
                Assert.Equal(InstanceStatus.Active, instances[0].Status);
                Assert.False(string.IsNullOrWhiteSpace(instances[0].AdvertiseEndpoint));
                Assert.False(string.IsNullOrWhiteSpace(instances[0].InstanceId));
            }

            Assert.Equal(2, provider.Current.GetAllInstances().Count);
            Assert.Empty(provider.Current.GetActiveInstances("NotHosted"));
        });
    }

    /// <summary>
    /// 测试激活幂等：二次 Activate no-op（路由表提供者引用不变）。
    /// </summary>
    [Fact]
    public void Activate_Twice_ShouldBeIdempotentNoOp()
    {
        ResetAndRun(() =>
        {
            InProcDiscoveryRuntime.Activate(CreateOptions("Game"));
            var firstProvider = InProcDiscoveryRuntime.TableProvider;
            Assert.NotNull(firstProvider);

            InProcDiscoveryRuntime.Activate(CreateOptions("Other"));

            Assert.Same(firstProvider, InProcDiscoveryRuntime.TableProvider);
            Assert.Single(InProcDiscoveryRuntime.TableProvider.Current.GetActiveInstances("Game"));
            Assert.Empty(InProcDiscoveryRuntime.TableProvider.Current.GetActiveInstances("Other"));
        });
    }

    /// <summary>
    /// 测试校验异常：null options / 缺 ConnectionName / null HostedRoleNames。
    /// </summary>
    [Fact]
    public void Activate_WithInvalidOptions_ShouldThrow()
    {
        ResetAndRun(() =>
        {
            Assert.Throws<ArgumentNullException>(() => InProcDiscoveryRuntime.Activate(null));

            var missingConnectionName = Assert.Throws<ArgumentException>(() => InProcDiscoveryRuntime.Activate(new DiscoveryActivationOptions
            {
                ConnectionName = string.Empty,
                HostedRoleNames = new[] { "Game" },
            }));
            Assert.Equal("options", missingConnectionName.ParamName);

            Assert.Throws<ArgumentNullException>(() => InProcDiscoveryRuntime.Activate(new DiscoveryActivationOptions
            {
                ConnectionName = "game",
                HostedRoleNames = null,
            }));
        });
    }

    /// <summary>
    /// 测试 MarkActiveAsync：registry 槽为 null 时 no-op 完成（不抛异常）。
    /// </summary>
    [Fact]
    public async Task MarkActiveAsync_AfterActivate_ShouldCompleteAsNoOp()
    {
        ResetAndRun(async () =>
        {
            InProcDiscoveryRuntime.Activate(CreateOptions("Game"));

            // registry 槽保持 null（单进程拓扑无心跳写侧）→ no-op 完成
            var exception = await Record.ExceptionAsync(() => ActiveDiscoveryRuntime.MarkActiveAsync());
            Assert.Null(exception);
        });
    }

    /// <summary>
    /// 构造激活参数。
    /// </summary>
    private static DiscoveryActivationOptions CreateOptions(params string[] roles)
    {
        return new DiscoveryActivationOptions
        {
            ConnectionName = "game",
            HostedRoleNames = roles,
        };
    }

    /// <summary>
    /// 测试 harness：前置重置静态装配态，结束后复位（隔离）。
    /// </summary>
    private static void ResetAndRun(Action action)
    {
        InProcDiscoveryRuntime.ResetForTest();
        try
        {
            action();
        }
        finally
        {
            InProcDiscoveryRuntime.ResetForTest();
        }
    }
}
