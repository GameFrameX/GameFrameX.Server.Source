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
//   or infringe upon the legitimate rights and interests of others, as prohibited by laws and regulations!
//   因基于本项目二次开发所产生的一切法律纠纷与责任，
//   Any legal disputes and liabilities arising from secondary development based on this project
//   本项目组织与贡献者概不承担。
//   shall be borne solely by the developer; the project organization and contributors assume no responsibility.
//   GitHub 仓库：https://github.com/GameFrameX
//   GitHub Repository: https://github.com/GameFrameX
//   Gitee  仓库：https://gitee.com/GameFrameX
//   Gitee Repository:  https://gitee.com/GameFrameX
//   CNB  仓库：https://cnb.cool/GameFrameX
//   CNB Repository:  https://cnb.cool/GameFrameX
//   官方文档：https://gameframex.doc.alianblank.com/
//   Official Documentation: https://gameframex.doc.alianblank.com/
//  ==========================================================================================

#nullable enable

using Microsoft.CodeAnalysis;

namespace GameFrameX.Architecture.Analyzers;

/// <summary>
/// 从编译上下文中一次性解析所有架构分析器需要引用的"知名类型"符号。
/// </summary>
/// <remarks>
/// 在编译开始时通过 <see cref="Create"/> 创建一次，随后在所有分析器之间共享。
/// 属性值可能为 null（当被引用的程序集未被项目引用时），各分析器需做 null 安全处理。
/// </remarks>
public sealed class ArchitectureSymbols
{
    private ArchitectureSymbols(
        INamedTypeSymbol? baseCacheState,
        INamedTypeSymbol? cacheState,
        INamedTypeSymbol? stateComponent,
        INamedTypeSymbol? stateComponentAgent,
        INamedTypeSymbol? baseHttpHandler,
        INamedTypeSymbol? hotfixBridge,
        INamedTypeSymbol? componentAgent,
        INamedTypeSymbol? messageMappingAttribute,
        INamedTypeSymbol? messageRpcMappingAttribute,
        INamedTypeSymbol? eventListener,
        INamedTypeSymbol? timerHandler,
        INamedTypeSymbol? mongoDbService,
        INamedTypeSymbol? multiDbRegistry)
    {
        BaseCacheState = baseCacheState;
        CacheState = cacheState;
        StateComponent = stateComponent;
        StateComponentAgent = stateComponentAgent;
        BaseHttpHandler = baseHttpHandler;
        HotfixBridge = hotfixBridge;
        ComponentAgent = componentAgent;
        MessageMappingAttribute = messageMappingAttribute;
        MessageRpcMappingAttribute = messageRpcMappingAttribute;
        EventListener = eventListener;
        TimerHandler = timerHandler;
        MongoDbService = mongoDbService;
        MultiDbRegistry = multiDbRegistry;
    }

    /// <summary>GameFrameX.DataBase.BaseCacheState — 数据库缓存状态的抽象基类。</summary>
    public INamedTypeSymbol? BaseCacheState { get; }

    /// <summary>GameFrameX.DataBase.Mongo.CacheState — MongoDB 缓存状态的默认实现（分析时跳过此类型自身）。</summary>
    public INamedTypeSymbol? CacheState { get; }

    /// <summary>GameFrameX.Core.Components.StateComponent`1 — 持有 CacheState 的状态组件基类。</summary>
    public INamedTypeSymbol? StateComponent { get; }

    /// <summary>GameFrameX.Core.Hotfix.Agent.StateComponentAgent`2 — 状态组件代理基类。</summary>
    public INamedTypeSymbol? StateComponentAgent { get; }

    /// <summary>GameFrameX.NetWork.HTTP.BaseHttpHandler — HTTP 请求处理器的基类。</summary>
    public INamedTypeSymbol? BaseHttpHandler { get; }

    /// <summary>GameFrameX.Core.Hotfix.IHotfixBridge — 热更新桥接接口，用于状态层调用逻辑层。</summary>
    public INamedTypeSymbol? HotfixBridge { get; }

    /// <summary>GameFrameX.Core.Abstractions.Agent.IComponentAgent — 组件代理接口（ECS 中的 System 角色）。</summary>
    public INamedTypeSymbol? ComponentAgent { get; }

    /// <summary>GameFrameX.NetWork.Abstractions.MessageMappingAttribute — 标记消息处理器的特性。</summary>
    public INamedTypeSymbol? MessageMappingAttribute { get; }

    /// <summary>GameFrameX.NetWork.Abstractions.MessageRpcMappingAttribute — 标记 RPC 消息处理器的特性。</summary>
    public INamedTypeSymbol? MessageRpcMappingAttribute { get; }

    /// <summary>GameFrameX.Core.Abstractions.Events.IEventListener — 事件监听器接口。</summary>
    public INamedTypeSymbol? EventListener { get; }

    /// <summary>GameFrameX.Core.Timer.Handler.ITimerHandler — 定时器回调处理器接口。</summary>
    public INamedTypeSymbol? TimerHandler { get; }

    /// <summary>GameFrameX.DataBase.Mongo.MongoDbService — MongoDB 数据服务实现，仅限 Mongo 实现层引用。</summary>
    public INamedTypeSymbol? MongoDbService { get; }

    /// <summary>GameFrameX.DataBase.MultiDbRegistry — 多库注册表，仅限 GameFrameX.DataBase 内部引用。</summary>
    public INamedTypeSymbol? MultiDbRegistry { get; }

    /// <summary>
    /// 从编译上下文中解析所有知名类型符号。每个编译只调用一次。
    /// </summary>
    public static ArchitectureSymbols Create(Compilation compilation)
    {
        return new ArchitectureSymbols(
            compilation.GetTypeByMetadataName("GameFrameX.DataBase.BaseCacheState"),
            compilation.GetTypeByMetadataName("GameFrameX.DataBase.Mongo.CacheState"),
            compilation.GetTypeByMetadataName("GameFrameX.Core.Components.StateComponent`1"),
            compilation.GetTypeByMetadataName("GameFrameX.Core.Hotfix.Agent.StateComponentAgent`2"),
            compilation.GetTypeByMetadataName("GameFrameX.NetWork.HTTP.BaseHttpHandler"),
            compilation.GetTypeByMetadataName("GameFrameX.Core.Hotfix.IHotfixBridge"),
            compilation.GetTypeByMetadataName("GameFrameX.Core.Abstractions.Agent.IComponentAgent"),
            compilation.GetTypeByMetadataName("GameFrameX.NetWork.Abstractions.MessageMappingAttribute"),
            compilation.GetTypeByMetadataName("GameFrameX.NetWork.Abstractions.MessageRpcMappingAttribute"),
            compilation.GetTypeByMetadataName("GameFrameX.Core.Abstractions.Events.IEventListener"),
            compilation.GetTypeByMetadataName("GameFrameX.Core.Timer.Handler.ITimerHandler"),
            compilation.GetTypeByMetadataName("GameFrameX.DataBase.Mongo.MongoDbService"),
            compilation.GetTypeByMetadataName("GameFrameX.DataBase.MultiDbRegistry"));
    }
}
