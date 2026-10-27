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

using System.Collections.Concurrent;
using System.Reflection;
using GameFrameX.Core.Abstractions.Agent;
using GameFrameX.Core.Abstractions.Events;
using GameFrameX.Core.BaseHandler;
using GameFrameX.Core.Components;
using GameFrameX.Network.Abstractions;
using GameFrameX.Network.HTTP;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Hotload;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Hotfix;

/// <summary>
/// 热更模块，负责热更新相关的初始化、卸载、解析DLL等工作。
/// </summary>
internal sealed class HotfixModule : ICollectibleAssemblyModule
{
    /// <summary>
    /// 角色类型到事件参数类型到监听者的映射（事件绑定键为 GameEventArgs 子类的 Type）。
    /// </summary>
    private readonly Dictionary<ushort, Dictionary<Type, List<IEventListener>>> _actorEvtListeners = new Dictionary<ushort, Dictionary<Type, List<IEventListener>>>(512);

    /// <summary>
    /// 代理类型到代理包装类型的映射。
    /// </summary>
    private readonly Dictionary<Type, Type> _agentAgentWrapperMap = new Dictionary<Type, Type>(512);

    /// <summary>
    /// 组件类型到代理类型的映射。
    /// </summary>
    private readonly Dictionary<Type, Type> _agentCompMap = new Dictionary<Type, Type>(512);

    /// <summary>
    /// 组件类型到代理类型的映射。
    /// </summary>
    private readonly Dictionary<Type, Type> _compAgentMap = new Dictionary<Type, Type>(512);

    /// <summary>
    /// 组件类型到代理实例工厂的映射（已按 _useAgentWrapper 选好包装类型或代理类型，注册期构建）。
    /// </summary>
    private readonly Dictionary<Type, Func<IComponentAgent>> _agentFactoryMap = new Dictionary<Type, Func<IComponentAgent>>(512);

    /// <summary>
    /// DLL路径。
    /// </summary>
    private readonly string _dllPath;

    /// <summary>
    /// HTTP命令到处理器的映射。
    /// </summary>
    private readonly ConcurrentDictionary<string, BaseHttpHandler> _httpHandlerMap = new ConcurrentDictionary<string, BaseHttpHandler>();

    /// <summary>
    /// RPC请求类型到响应类型的映射。
    /// </summary>
    private readonly ConcurrentDictionary<Type, Type> _rpcHandlerMap = new ConcurrentDictionary<Type, Type>();

    /// <summary>
    /// 消息ID到处理器实例工厂的映射（注册期构建；处理器有状态，工厂每次调用创建新实例）。
    /// </summary>
    private readonly ConcurrentDictionary<int, Func<IMessageHandler>> _messageHandlerMap = new ConcurrentDictionary<int, Func<IMessageHandler>>();

    /// <summary>
    /// 消息处理类型列表
    /// </summary>
    private readonly List<Type> _messageHandlerTypes = new List<Type>(512);

    /// <summary>
    /// 类型缓存。
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _typeCacheMap = new ConcurrentDictionary<string, object>();

    /// <summary>
    /// 是否使用代理包装。
    /// </summary>
    private readonly bool _useAgentWrapper = true;

    /// <summary>
    /// 可收集加载上下文（模块生命周期与卸载验证经 <see cref="ICollectibleAssemblyModule"/> 契约由模块链驱动）。
    /// The collectible load context (module lifecycle and unload verification are driven by the module chain via the <see cref="ICollectibleAssemblyModule"/> contract).
    /// </summary>
    private CollectibleAssemblyLoadContext _loadContext;

    /// <summary>
    /// 热更程序集。
    /// </summary>
    internal Assembly HotfixAssembly;

    /// <summary>
    /// 获取模块装载的程序集（<see cref="ICollectibleAssemblyModule"/> 契约成员，供退役链按程序集身份路由）。
    /// Gets the assembly loaded by this module (the <see cref="ICollectibleAssemblyModule"/> contract member used
    /// for assembly-identity routing on the retirement chain).
    /// </summary>
    Assembly ICollectibleAssemblyModule.LoadedAssembly
    {
        get { return HotfixAssembly; }
    }

    /// <summary>
    /// 构造函数，接受DLL路径。
    /// </summary>
    /// <param name="dllPath">DLL路径。</param>
    internal HotfixModule(string dllPath)
    {
        _dllPath = dllPath;
    }

    /// <summary>
    /// 获取模块的 DLL 完整路径（装载入口幂等判重用：同路径重复装载时跳过）。
    /// </summary>
    /// <remarks>
    /// Gets the module's full DLL path (used by the load entries for same-path idempotency:
    /// repeated loads of the same path are skipped).
    /// </remarks>
    internal string DllPath
    {
        get { return _dllPath; }
    }

    /// <summary>
    /// 默认构造函数，初始化热更程序集并解析DLL。
    /// </summary>
    internal HotfixModule()
    {
        HotfixAssembly = Assembly.GetEntryAssembly();
        ConvertHotfixAssembly();
    }

    /// <summary>
    /// 测试用构造函数：直接扫描指定程序集并解析注册（不经磁盘 DLL 加载）。
    /// </summary>
    /// <param name="assembly">要解析的热更程序集。</param>
    internal HotfixModule(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        HotfixAssembly = assembly;
        ConvertHotfixAssembly();
    }

    /// <summary>
    /// 热更桥接接口。
    /// </summary>
    internal IHotfixBridge HotfixBridge { get; private set; }

    /// <summary>
    /// 初始化热更模块。
    /// </summary>
    /// <param name="reload">是否重新加载。</param>
    /// <returns>初始化是否成功。</returns>
    internal bool Init(bool reload)
    {
        var success = false;
        try
        {
            _loadContext = new CollectibleAssemblyLoadContext();
            HotfixAssembly = _loadContext.LoadFromAssemblyPath(_dllPath);
            if (!reload)
            {
                // 启动服务器时加载关联的DLL
                LoadRefAssemblies();
            }

            ConvertHotfixAssembly();

            // Localization: Core.Hotfix.DllInitializationSuccess - 热更DLL初始化成功: {0}
            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Core.HotfixModule.DllInitializationSuccess, _dllPath));
            success = true;
        }
        catch (Exception e)
        {
            // Localization: Core.Hotfix.DllInitializationFailed - 热更DLL初始化失败...\n{0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.HotfixModule.DllInitializationFailed, e));
            if (!reload)
            {
                throw;
            }
        }

        return success;
    }

    /// <summary>
    /// 卸载热更模块底层的可收集上下文（契约成员）。
    /// Unloads the module's underlying collectible context (contract member).
    /// </summary>
    /// <returns>
    /// 指向已卸载上下文的弱引用（供模块链做 GC 释放验证）；未持有上下文（如测试构造）时返回 <c>null</c>。
    /// A weak reference to the unloaded context (for the module chain's GC release verification); <c>null</c>
    /// when no context is owned (e.g. test-constructed modules).
    /// </returns>
    public WeakReference Unload()
    {
        if (_loadContext == null)
        {
            return null;
        }

        _loadContext.Unload();
        return new WeakReference(_loadContext);
    }

    /// <summary>
    /// 加载引用的程序集。
    /// </summary>
    private void LoadRefAssemblies()
    {
        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
        var nameSet = new HashSet<string>(assemblies.Select(t => t.GetName().Name));
        var hotfixRefAssemblies = HotfixAssembly.GetReferencedAssemblies();
        foreach (var refAssembly in hotfixRefAssemblies)
        {
            if (nameSet.Contains(refAssembly.Name))
            {
                continue;
            }

            var refPath = $"{Environment.CurrentDirectory}/{refAssembly.Name}.dll";
            if (File.Exists(refPath))
            {
                Assembly.LoadFrom(refPath);
            }
        }
    }

    /// <summary>
    /// 解析DLL中的类型并进行注册。
    /// </summary>
    private void ConvertHotfixAssembly()
    {
        var fullName = typeof(IHotfixBridge).FullName;
        var types = HotfixAssembly.GetTypes();
        foreach (var type in types)
        {
            if (!AddAgent(type)
                && !AddEvent(type)
                && !AddMessageHandler(type)
                && !AddHttpHandler(type))
            {
                if (HotfixBridge.IsNull() && type.GetInterface(fullName) != null)
                {
                    HotfixBridge = CreateFactory<IHotfixBridge>(type)();
                }
            }

            AddRpcHandler(type);
        }

        BuildAgentFactories();
    }

    /// <summary>
    /// 为每个已注册组件构建代理实例工厂（启用包装且存在对应包装类型时优先包装类型），注册期即校验实现类型的可实例化条件。
    /// </summary>
    /// <remarks>
    /// 工厂必须在类型枚举循环结束后统一构建：类型枚举顺序不确定，登记某个代理类型时其包装类型可能尚未被扫描登记。
    /// </remarks>
    private void BuildAgentFactories()
    {
        foreach (var (componentType, agentType) in _compAgentMap)
        {
            var implementationType = agentType;
            if (_useAgentWrapper && _agentAgentWrapperMap.TryGetValue(agentType, out var wrapperType))
            {
                implementationType = wrapperType;
            }

            _agentFactoryMap[componentType] = CreateFactory<IComponentAgent>(implementationType);
        }
    }

    /// <summary>
    /// 注册期创建并缓存实例工厂：校验实现类型是否为可实例化的封闭类型、是否实现 <typeparamref name="TService"/>、是否具有公共无参构造函数。
    /// </summary>
    /// <param name="implementationType">实现类型。</param>
    /// <typeparam name="TService">服务接口或基类类型。</typeparam>
    /// <returns>创建 <typeparamref name="TService"/> 实例的强类型工厂委托。</returns>
    /// <exception cref="InvalidOperationException">实现类型不满足上述条件。</exception>
    private static Func<TService> CreateFactory<TService>(Type implementationType) where TService : class
    {
        if (implementationType.IsAbstract
            || implementationType.ContainsGenericParameters
            || !typeof(TService).IsAssignableFrom(implementationType)
            || implementationType.GetConstructor(Type.EmptyTypes) == null)
        {
            // Localization: CoreExceptions.Hotfix.MissingParameterlessConstructor - {0} 必须是实现 {1} 的非抽象封闭类型，且具有公共无参构造函数
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.MissingParameterlessConstructor, implementationType.FullName, typeof(TService).FullName));
        }

        var createInstanceMethod = typeof(HotfixModule)
                                   .GetMethod(nameof(CreateInstance), BindingFlags.NonPublic | BindingFlags.Static)
                                   .MakeGenericMethod(typeof(TService), implementationType);
        return createInstanceMethod.CreateDelegate<Func<TService>>();
    }

    /// <summary>
    /// 泛型实例化目标方法：经 <see cref="CreateFactory{TService}" /> 的泛型方法实例化转为强类型工厂委托，运行时直接调用无参构造，避免每次调用走反射激活。
    /// </summary>
    /// <typeparam name="TService">服务接口或基类类型。</typeparam>
    /// <typeparam name="TImplementation">实现类型（受 new() 约束，实例化时由运行时直接调用其无参构造）。</typeparam>
    /// <returns>新创建的实现实例。</returns>
    private static TService CreateInstance<TService, TImplementation>() where TImplementation : TService, new()
    {
        return new TImplementation();
    }

    /// <summary>
    /// 注册期创建并缓存实例工厂：校验实现类型是否为可实例化的封闭类型、是否实现 <typeparamref name="TService"/>、是否具有公共无参构造函数。
    /// </summary>
    /// <param name="implementationType">实现类型。</param>
    /// <typeparam name="TService">服务接口或基类类型。</typeparam>
    /// <returns>创建 <typeparamref name="TService"/> 实例的强类型工厂委托。</returns>
    /// <exception cref="InvalidOperationException">实现类型不满足上述条件。</exception>
    private static Func<TService> CreateFactory<TService>(Type implementationType) where TService : class
    {
        if (implementationType.IsAbstract
            || implementationType.ContainsGenericParameters
            || !typeof(TService).IsAssignableFrom(implementationType)
            || implementationType.GetConstructor(Type.EmptyTypes) == null)
        {
            // Localization: CoreExceptions.Hotfix.MissingParameterlessConstructor - {0} 必须是实现 {1} 的非抽象封闭类型，且具有公共无参构造函数
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.MissingParameterlessConstructor, implementationType.FullName, typeof(TService).FullName));
        }

        var createInstanceMethod = typeof(HotfixModule)
                                   .GetMethod(nameof(CreateInstance), BindingFlags.NonPublic | BindingFlags.Static)
                                   .MakeGenericMethod(typeof(TService), implementationType);
        return createInstanceMethod.CreateDelegate<Func<TService>>();
    }

    /// <summary>
    /// 泛型实例化目标方法：经 <see cref="CreateFactory{TService}" /> 的泛型方法实例化转为强类型工厂委托，运行时直接调用无参构造，避免每次调用走反射激活。
    /// </summary>
    /// <typeparam name="TService">服务接口或基类类型。</typeparam>
    /// <typeparam name="TImplementation">实现类型（受 new() 约束，实例化时由运行时直接调用其无参构造）。</typeparam>
    /// <returns>新创建的实现实例。</returns>
    private static TService CreateInstance<TService, TImplementation>() where TImplementation : TService, new()
    {
        return new TImplementation();
    }

    /// <summary>
    /// 添加HTTP处理器。
    /// </summary>
    /// <param name="type">处理器类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddHttpHandler(Type type)
    {
        if (!type.IsSubclassOf(typeof(BaseHttpHandler)))
        {
            return false;
        }

        var attr = (HttpMessageMappingAttribute)type.GetCustomAttribute(typeof(HttpMessageMappingAttribute));
        if (attr.IsNull())
        {
            // 不是最终实现类
            return true;
        }

        var handler = CreateFactory<BaseHttpHandler>(type)();
        // 注册原始命令
        if (!_httpHandlerMap.TryAdd(attr.OriginalCmd, handler))
        {
            // Localization: CoreExceptions.Hotfix.HttpProcessorRepeatedlyRegistered - HTTP处理器命令重复注册，命令:{0}
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.HttpProcessorRepeatedlyRegistered, attr.OriginalCmd));
        }

        // 注册标准化的命名
        if (!_httpHandlerMap.TryAdd(attr.StandardCmd, handler))
        {
            // Localization: CoreExceptions.Hotfix.HttpProcessorRepeatedlyRegistered - HTTP处理器命令重复注册，命令:{0}
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.HttpProcessorRepeatedlyRegistered, attr.OriginalCmd));
        }

        return true;
    }


    /// <summary>
    /// 获取RPC处理器实例。
    /// </summary>
    /// <param name="type">请求消息的类型。</param>
    /// <returns>对应的 <see cref="IMessageHandler"/> 实例，如果未找到则返回 null。</returns>
    internal IMessageHandler GetRpcHandler(Type type)
    {
        if (!_rpcHandlerMap.TryGetValue(type, out var handlerType))
        {
            return default;
        }

        var instance = Activator.CreateInstance(handlerType);
        if (instance is IMessageHandler handler)
        {
            return handler;
        }

        // Localization: CoreExceptions.Hotfix.WrongMessageHandlerType - 错误的消息处理器类型:{0}
        throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongMessageHandlerType, instance.GetType().FullName));
    }

    /// <summary>
    /// 添加RPC处理器。
    /// </summary>
    /// <param name="type">处理器类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddRpcHandler(Type type)
    {
        var attribute = (MessageRpcMappingAttribute)type.GetCustomAttribute(typeof(MessageRpcMappingAttribute), true);
        if (attribute.IsNull())
        {
            return false;
        }

        var isHas = _rpcHandlerMap.TryGetValue(attribute.RequestMessage.GetType(), out var requestHandler);
        if (isHas && requestHandler?.GetType() == attribute.ResponseMessage.GetType())
        {
            // Localization: CoreExceptions.Hotfix.HttpProcessorRepeatedlyRegistered - HTTP处理器命令重复注册，命令:{0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.HttpProcessorRepeatedlyRegistered, attribute.RequestMessage));
            return false;
        }

        _rpcHandlerMap.TryAdd(attribute.RequestMessage.GetType(), attribute.ResponseMessage.GetType());

        return true;
    }

    /// <summary>
    /// 添加消息处理器。
    /// </summary>
    /// <param name="type">处理器类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddMessageHandler(Type type)
    {
        var attribute = (MessageMappingAttribute)type.GetCustomAttribute(typeof(MessageMappingAttribute), true);
        if (attribute == null)
        {
            return false;
        }

        var classFullName = type.FullName;
        if (classFullName == null)
        {
            return false;
        }

        if (!type.IsSealed)
        {
            // Localization: CoreExceptions.Hotfix.ClassMustBeSealed - {0} 必须是标记为sealed的类
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.ClassMustBeSealed, classFullName));
        }

        if (!classFullName.EndsWith(GlobalConst.ComponentHandlerNameSuffix))
        {
            // Localization: CoreExceptions.Hotfix.MessageProcessorWrongSuffix - 消息处理器必须以[{0}]结尾，{1}
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.MessageProcessorWrongSuffix, GlobalConst.ComponentHandlerNameSuffix, classFullName));
        }

        if (_messageHandlerTypes.Contains(attribute.MessageType))
        {
            // Localization: CoreExceptions.Hotfix.WrongMessageHandlerType - 错误的消息处理器类型:{0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongMessageHandlerType, type.FullName));
            return false;
        }

        var msgIdField = (MessageTypeHandlerAttribute)attribute.MessageType.GetCustomAttribute(typeof(MessageTypeHandlerAttribute), true);
        if (msgIdField == null)
        {
            return false;
        }

        var msgId = msgIdField.MessageId;
        var messageHandlerFactory = CreateFactory<IMessageHandler>(type);
        if (!_messageHandlerMap.TryAdd(msgId, messageHandlerFactory))
        {
            // Localization: CoreExceptions.Hotfix.WrongMessageHandlerType - 错误的消息处理器类型:{0}
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongMessageHandlerType, type));
        }

        _messageHandlerTypes.Add(attribute.MessageType);
        return true;
    }

    /// <summary>
    /// 添加事件监听者：读取 <see cref="EventAttribute" /> 按事件参数类型注册。
    /// </summary>
    /// <param name="type">监听者类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddEvent(Type type)
    {
        if (!type.IsImplWithInterface(typeof(IEventListener)))
        {
            return false;
        }

        var classFullName = type.FullName;
        if (classFullName == null)
        {
            return false;
        }

        if (!type.IsSealed)
        {
            // Localization: CoreExceptions.Hotfix.ClassMustBeSealed - {0} 必须是标记为sealed的类
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.ClassMustBeSealed, classFullName));
        }

        if (!classFullName.EndsWith(GlobalConst.EventListenerNameSuffix))
        {
            // Localization: CoreExceptions.Hotfix.EventHandlerWrongSuffix - 事件处理器必须以[{0}]结尾，{1}
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.EventHandlerWrongSuffix, GlobalConst.EventListenerNameSuffix, classFullName));
        }

        var compAgentType = type.BaseType.GetGenericArguments()[0];
        var compType = compAgentType.BaseType.GetGenericArguments()[0];
        var actorType = ComponentRegister.ComponentActorDic[compType];
        var evtListenersDic = _actorEvtListeners.GetOrAddValue(actorType);

        var eventAttribute = type.GetCustomAttribute<EventAttribute>();
        if (eventAttribute == null)
        {
            // Localization: CoreExceptions.Hotfix.NoEventsToListen - IEventListener:{0} 没有指定要监听的事件
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.NoEventsToListen, type.FullName));
        }

        var eventArgsType = eventAttribute.EventArgsType;
        if (!typeof(GameEventArgs).IsAssignableFrom(eventArgsType))
        {
            // Localization: CoreExceptions.Hotfix.EventArgsTypeInvalid - 事件监听器:{0} 绑定的类型 {1} 必须是 GameEventArgs 的子类
            throw new InvalidOperationException(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.EventArgsTypeInvalid, type.FullName, eventArgsType.FullName));
        }

        var listeners = evtListenersDic.GetOrAddValue(eventArgsType);
        listeners.Add(CreateFactory<IEventListener>(type)());

        return true;
    }

    /// <summary>
    /// 添加组件代理。
    /// </summary>
    /// <param name="type">代理类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddAgent(Type type)
    {
        ArgumentNullException.ThrowIfNull(type);
        if (!type.IsImplWithInterface(typeof(IComponentAgent)))
        {
            return false;
        }

        var fullName = type.FullName;
        if (fullName == null)
        {
            return false;
        }

        // 这里处理SourceGeneratedCode 生成的代码的 Warp 对象,注意命名空间的识别
        if (fullName.StartsWith(GlobalConst.HotfixNameSpaceNamePrefix) && fullName.EndsWith(GlobalConst.ComponentAgentWrapperNameSuffix))
        {
            _agentAgentWrapperMap[type.BaseType] = type;
            return true;
        }

        if (!fullName.EndsWith(GlobalConst.ComponentAgentNameSuffix))
        {
            // Localization: CoreExceptions.Hotfix.ComponentAgentWrongSuffix - 组件代理必须以[{0}]结尾，{1}
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.ComponentAgentWrongSuffix, GlobalConst.ComponentAgentNameSuffix, fullName));
        }

        var compType = type.BaseType.GetGenericArguments()[0];
        if (!_compAgentMap.TryAdd(compType, type))
        {
            // Localization: CoreExceptions.Hotfix.MultipleAgents - 组件:[{0}] 存在多个代理
            throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.MultipleAgents, compType.FullName));
        }

        _agentCompMap[type] = compType;
        return true;
    }


    /// <summary>
    /// 获取消息处理器。
    /// </summary>
    /// <param name="msgId">消息ID。</param>
    /// <returns>消息处理器实例。</returns>
    internal IMessageHandler GetMessageHandler(int msgId)
    {
        if (!_messageHandlerMap.TryGetValue(msgId, out var messageHandlerFactory))
        {
            return default;
        }

        return messageHandlerFactory();
    }

    /// <summary>
    /// 获取HTTP处理器。
    /// </summary>
    /// <param name="cmd">命令。</param>
    /// <returns>HTTP处理器实例。</returns>
    internal BaseHttpHandler GetHttpHandler(string cmd)
    {
        if (_httpHandlerMap.TryGetValue(cmd, out var handler))
        {
            return handler;
        }

        return null;
        // throw new HttpHandlerNotFoundException($"未注册的HTTP命令:{cmd}");
    }

    /// <summary>
    /// 获取HTTP处理器列表。
    /// </summary>
    /// <returns>HTTP处理器列表。</returns>
    internal List<BaseHttpHandler> GetListHttpHandler()
    {
        var values = _httpHandlerMap.Values;
        var list = new List<BaseHttpHandler>(values.Count / 2);
        foreach (var handler in values)
        {
            if (list.Contains(handler))
            {
                continue;
            }

            list.Add(handler);
        }

        return list;
    }

    /// <summary>
    /// 获取组件代理。
    /// </summary>
    /// <param name="component">组件实例。</param>
    /// <typeparam name="T">代理类型。</typeparam>
    /// <returns>代理实例。</returns>
    internal T GetAgent<T>(BaseComponent component) where T : class, IComponentAgent
    {
        var type = component.GetType();
        if (_agentFactoryMap.TryGetValue(type, out var agentFactory))
        {
            var agent = (T)agentFactory();
            agent.SetOwner(component);
            return agent;
        }

        // Localization: Exceptions.Hotfix.ComponentAgentMapNotFound - 未找到组件代理映射：{0} ===> {1}
        throw new KeyNotFoundException(LocalizationService.GetString(Localization.Keys.Exceptions.Component_Agent_Map_Not_Found, nameof(_compAgentMap), type.FullName));
    }

    /// <summary>
    /// 查找事件监听者。
    /// </summary>
    /// <param name="actorType">角色类型。</param>
    /// <param name="eventArgsType">事件参数类型（事件绑定键）。</param>
    /// <returns>事件监听者列表。</returns>
    internal List<IEventListener> FindListeners(ushort actorType, Type eventArgsType)
    {
        if (_actorEvtListeners.TryGetValue(actorType, out var eventListeners) && eventListeners.TryGetValue(eventArgsType, out var listeners))
        {
            return listeners;
        }

        return default;
    }

    /// <summary>
    /// 查找事件监听者。
    /// </summary>
    /// <param name="eventArgsType">事件参数类型（事件绑定键）。</param>
    /// <returns>事件监听者列表。</returns>
    internal List<IEventListener> FindListeners(Type eventArgsType)
    {
        var listenerList = new List<IEventListener>(32);
        foreach (var actorEvtListener in _actorEvtListeners)
        {
            if (actorEvtListener.Value.TryGetValue(eventArgsType, out var listeners))
            {
                listenerList.AddRange(listeners);
            }
        }

        return listenerList;
    }

    /// <summary>
    /// 获取实例（主要用于获取Event, Timer, Schedule的处理器实例）。
    /// </summary>
    /// <param name="typeName">类型名称。</param>
    /// <typeparam name="T">实例类型。</typeparam>
    /// <returns>实例对象。</returns>
    internal T GetInstance<T>(string typeName)
    {
        return (T)_typeCacheMap.GetOrAdd(typeName, k => HotfixAssembly.CreateInstance(k));
    }

    /// <summary>
    /// 获取代理类型。
    /// </summary>
    /// <param name="compType">组件类型。</param>
    /// <returns>代理类型。</returns>
    internal Type GetAgentType(Type compType)
    {
        _compAgentMap.TryGetValue(compType, out var agentType);
        return agentType;
    }

    /// <summary>
    /// 获取组件类型。
    /// </summary>
    /// <param name="agentType">代理类型。</param>
    /// <returns>组件类型。</returns>
    internal Type GetComponentType(Type agentType)
    {
        _agentCompMap.TryGetValue(agentType, out var compType);
        return compType;
    }
}