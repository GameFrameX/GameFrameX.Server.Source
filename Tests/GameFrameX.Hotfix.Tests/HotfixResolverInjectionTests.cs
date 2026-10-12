// ==========================================================================================
//   GameFrameX 组织及其衍生项目的版权、商标、专利及其他相关权利
//   GameFrameX organization and its derivative projects' copyrights, trademarks, patents, and related rights
//   均受中华人民共和国及相关国际法律法规保护。
//   are protected by the laws of the People's Republic of China and related international regulations.
//   使用本项目须严格遵守相应法律法规之开源许可证之规定。
//   Usage of this project must strictly comply with applicable laws, regulations, and open-source licenses.
//   本项目采用 Apache License 2.0 单协议分发，
//   This project is licensed solely under the Apache License 2.0,
//   完整许可证文本请参见源代码根目录下的 LICENSE 文件。
//   please see the LICENSE file in the root directory of the source code for the full license text.
//   禁止利用本项目实施任何危害国家安全、破坏社会秩序、
//   It is prohibited to use this project to engage in any activities that endanger national security, disrupt social order,
//   侵犯他人合法权益等法律法规所禁止的行为！
//   or infringe upon the legitimate rights or interests of others, as prohibited by laws and regulations.
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
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

using System.Reflection;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.Abstractions.Hotfix;
using GameFrameX.Core.Components;
using GameFrameX.Core.Hotfix;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Core.Abstractions.Agent;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>
/// 解析契约注入缝测试：<see cref="HotfixManager.SetResolverForTesting"/> 注入替身后，
/// 门面与 Core 子系统消费路径应全部改走替身（可替换性实证）。
/// Resolution-contract seam tests: after injecting a substitute via
/// <see cref="HotfixManager.SetResolverForTesting"/>, the facade and Core subsystem consumption paths
/// must all route through it (substitutability evidence).
/// </summary>
public sealed class HotfixResolverInjectionTests
{
    /// <summary>
    /// 测试替身：返回预设的监听器 / 实例 / 过渡态，并记录调用。
    /// Test double: returns preset listeners / instances / transition state and records calls.
    /// </summary>
    private sealed class FakeResolver : IHotfixResolver
    {
        public List<IEventListener> ListenersResult
        {
            get;
        } = new();

        public object InstanceResult
        {
            get;
            set;
        } = new object();

        public bool InTransitionResult
        {
            get;
            set;
        }

        public int FindListenersCallCount
        {
            get;
            private set;
        }

        public int GetInstanceCallCount
        {
            get;
            private set;
        }

        public Assembly HotfixAssembly
        {
            get { return typeof(FakeResolver).Assembly; }
        }

        public bool InTransition
        {
            get { return InTransitionResult; }
        }

        public Type GetAgentType(Type componentType)
        {
            return null;
        }

        public Type GetComponentType(Type agentType)
        {
            return null;
        }

        public T GetAgent<T>(BaseComponent component, Type refAssemblyType) where T : class, IComponentAgent
        {
            return null;
        }

        public IMessageHandler GetRpcHandler(Type requestType)
        {
            return null;
        }

        public IMessageHandler GetMessageHandler(int messageId)
        {
            return null;
        }

        public BaseHttpHandler GetHttpHandler(string command)
        {
            return null;
        }

        public List<BaseHttpHandler> GetListHttpHandler()
        {
            return new List<BaseHttpHandler>();
        }

        public List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
        {
            FindListenersCallCount++;
            return ListenersResult;
        }

        public List<IEventListener> FindListeners(Type eventArgsType)
        {
            FindListenersCallCount++;
            return ListenersResult;
        }

        public T GetInstance<T>(string typeName, Type refAssemblyType = null)
        {
            GetInstanceCallCount++;
            return (T)InstanceResult;
        }
    }

    /// <summary>
    /// 注入替身后，门面解析应命中替身结果；复原后回到生产宿主。
    /// With the substitute injected, facade resolution must hit the substitute's results; after restore,
    /// the production host serves again.
    /// </summary>
    [Fact]
    public void SetResolver_FacadeRoutesThroughSubstituteAndRestores()
    {
        var fake = new FakeResolver();
        var original = HotfixManager.Resolver;
        try
        {
            HotfixManager.SetResolverForTesting(fake);

            // 门面 → 契约 → 替身
            Assert.Same(fake.ListenersResult, HotfixManager.FindListeners(typeof(EventArgs)));
            Assert.Same(fake.ListenersResult, HotfixManager.FindListeners(1, typeof(EventArgs)));
            Assert.Same(fake.InstanceResult, HotfixManager.GetInstance<object>("AnyTypeName"));
            Assert.Equal(2, fake.FindListenersCallCount);
            Assert.Equal(1, fake.GetInstanceCallCount);
            Assert.Same(typeof(FakeResolver).Assembly, HotfixManager.HotfixAssembly);
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }

        // 复原后回到生产宿主（不再是替身）
        Assert.NotSame(fake, HotfixManager.Resolver);
        Assert.Same(original, HotfixManager.Resolver);
    }

    /// <summary>
    /// BaseComponent 的代理缓存失效判定应消费替身的 InTransition。
    /// BaseComponent's agent-cache invalidation must consume the substitute's InTransition.
    /// </summary>
    [Fact]
    public void SetResolver_ComponentCacheInvalidationConsumesSubstituteInTransition()
    {
        // 经门面读取过渡标志：BaseComponent 缓存失效判定的同一信号源
        var fake = new FakeResolver { InTransitionResult = true };
        try
        {
            HotfixManager.SetResolverForTesting(fake);
            Assert.True(HotfixManager.Resolver.InTransition);

            fake.InTransitionResult = false;
            Assert.False(HotfixManager.Resolver.InTransition);
        }
        finally
        {
            HotfixManager.SetResolverForTesting(null);
        }
    }
}
