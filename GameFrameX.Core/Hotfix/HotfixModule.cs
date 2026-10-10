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
using GameFrameX.NetWork.Abstractions;
using GameFrameX.NetWork.HTTP;
using GameFrameX.Foundation.Extensions;
using GameFrameX.Foundation.Logger;
using GameFrameX.Foundation.Localization.Core;
using GameFrameX.Utility.Setting;

namespace GameFrameX.Core.Hotfix;

/// <summary>
/// 热更模块，负责热更新相关的初始化、卸载、解析DLL等工作。
/// </summary>
internal sealed class HotfixModule
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
    /// 消息ID到处理器类型的映射。
    /// </summary>
    private readonly ConcurrentDictionary<int, Type> _tcpHandlerMap = new ConcurrentDictionary<int, Type>();

    /// <summary>
    /// 消息处理类型列表
    /// </summary>
    private readonly List<Type> _tcpHandlerTypes = new List<Type>(512);

    /// <summary>
    /// 类型缓存。
    /// </summary>
    private readonly ConcurrentDictionary<string, object> _typeCacheMap = new ConcurrentDictionary<string, object>();

    /// <summary>
    /// 是否使用代理包装。
    /// </summary>
    private readonly bool _useAgentWrapper = true;

    /// <summary>
    /// DLL加载器。
    /// </summary>
    private DllLoader _dllLoader;

    /// <summary>
    /// 热更程序集。
    /// </summary>
    internal Assembly HotfixAssembly;

    /// <summary>
    /// 构造函数，接受DLL路径。
    /// </summary>
    /// <param name="dllPath">DLL路径。</param>
    internal HotfixModule(string dllPath)
    {
        _dllPath = dllPath;
    }

    /// <summary>
    /// 默认构造函数，初始化热更程序集并解析DLL。
    /// </summary>
    internal HotfixModule()
    {
        HotfixAssembly = Assembly.GetEntryAssembly();
        ParseDll();
    }

    /// <summary>
    /// 测试用构造函数：直接扫描指定程序集并解析注册（不经磁盘 DLL 加载）。
    /// </summary>
    /// <param name="assembly">要解析的热更程序集。</param>
    internal HotfixModule(Assembly assembly)
    {
        ArgumentNullException.ThrowIfNull(assembly);
        HotfixAssembly = assembly;
        ParseDll();
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
            _dllLoader = new DllLoader(_dllPath);
            HotfixAssembly = _dllLoader.HotfixDll;
            if (!reload)
            {
                // 启动服务器时加载关联的DLL
                LoadRefAssemblies();
            }

            ParseDll();

            LogHelper.Info(LocalizationService.GetString(Localization.Keys.Core.HotfixModule.DllInitializationSuccess, _dllPath));
            success = true;
        }
        catch (Exception e)
        {
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.Core.HotfixModule.DllInitializationFailed, e));
            if (!reload)
            {
                throw;
            }
        }

        return success;
    }

    /// <summary>
    /// 卸载热更模块。
    /// </summary>
    public void Unload()
    {
        if (_dllLoader != null)
        {
            var weak = _dllLoader.Unload();
            if (GlobalSettings.CurrentSetting.IsDebug)
            {
                // 检查热更DLL是否已经释放
                Task.Run(async () =>
                {
                    var tryCount = 0;
                    while (weak.IsAlive && tryCount++ < 10)
                    {
                        await Task.Delay(100);
                        // 可回收 AssemblyLoadContext 卸载后需强制 GC 以回收可卸载程序集并判定其是否真正释放，是 .NET 官方推荐模式，故此处有意保留 GC.Collect
#pragma warning disable S1215 // 垃圾回收器不应被显式调用
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
#pragma warning restore S1215
                    }

                    LogHelper.Warning(LocalizationService.GetString(Localization.Keys.Core.HotfixModule.DllUninstall, weak.IsAlive ? "failure" : "successful"));
                });
            }
        }
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
    private void ParseDll()
    {
        var fullName = typeof(IHotfixBridge).FullName;
        var types = HotfixAssembly.GetTypes();
        foreach (var type in types)
        {
            if (!AddAgent(type)
                && !AddEvent(type)
                && !AddTcpHandler(type)
                && !AddHttpHandler(type))
            {
                if (HotfixBridge.IsNull() && type.GetInterface(fullName) != null)
                {
                    var bridge = (IHotfixBridge)Activator.CreateInstance(type);

                    HotfixBridge = bridge;
                }
            }

            AddRpcHandler(type);
        }
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

        var handler = (BaseHttpHandler)Activator.CreateInstance(type);
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

        // Localization: CoreExceptions.Hotfix.WrongTcpProcessorType - 错误的TCP处理器类型:{0}
        throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongTcpProcessorType, instance.GetType().FullName));
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
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.HttpProcessorRepeatedlyRegistered, attribute.RequestMessage));
            return false;
        }

        _rpcHandlerMap.TryAdd(attribute.RequestMessage.GetType(), attribute.ResponseMessage.GetType());

        return true;
    }

    /// <summary>
    /// 添加TCP处理器。
    /// </summary>
    /// <param name="type">处理器类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddTcpHandler(Type type)
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

        if (_tcpHandlerTypes.Contains(attribute.MessageType))
        {
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongTcpProcessorType, type.FullName));
            return false;
        }

        var msgIdField = (MessageTypeHandlerAttribute)attribute.MessageType.GetCustomAttribute(typeof(MessageTypeHandlerAttribute), true);
        if (msgIdField == null)
        {
            return false;
        }

        var msgId = msgIdField.MessageId;
        if (!_tcpHandlerMap.TryAdd(msgId, type))
        {
            LogHelper.Error(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongTcpProcessorType, type));
        }

        _tcpHandlerTypes.Add(attribute.MessageType);
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
        listeners.Add((IEventListener)Activator.CreateInstance(type));

        return true;
    }

    /// <summary>
    /// 添加组件代理。
    /// </summary>
    /// <param name="type">代理类型。</param>
    /// <returns>是否添加成功。</returns>
    private bool AddAgent(Type type)
    {
        ArgumentNullException.ThrowIfNull(type, nameof(type));
        if (!type.IsImplWithInterface(typeof(IComponentAgent)))
        {
            return false;
        }

        var fullName = type.FullName;
        if (fullName == null)
        {
            return false;
        }

        if (fullName == "GameFrameX.Launcher.Logic.Server.ServerComp")
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
    /// 获取TCP处理器。
    /// </summary>
    /// <param name="msgId">消息ID。</param>
    /// <returns>TCP处理器实例。</returns>
    internal IMessageHandler GetTcpHandler(int msgId)
    {
        if (!_tcpHandlerMap.TryGetValue(msgId, out var handlerType))
        {
            return default;
        }

        var instance = Activator.CreateInstance(handlerType);
        if (instance is IMessageHandler handler)
        {
            return handler;
        }

        // Localization: CoreExceptions.Hotfix.WrongTcpProcessorType - 错误的TCP处理器类型:{0}
        throw new Exception(LocalizationService.GetString(Localization.Keys.CoreExceptions.HotfixModule.WrongTcpProcessorType, instance.GetType().FullName));

        //throw new HandlerNotFoundException($"消息ID：{msgId}");
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
        List<BaseHttpHandler> list = new List<BaseHttpHandler>(values.Count / 2);
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
        if (_compAgentMap.TryGetValue(type, out var agentType))
        {
            T agent = null;
            if (_useAgentWrapper)
            {
                if (_agentAgentWrapperMap.TryGetValue(agentType, out var warpType))
                {
                    agent = (T)Activator.CreateInstance(warpType);
                }
            }

            if (agent.IsNull())
            {
                agent = (T)Activator.CreateInstance(agentType);
            }

            if (agent.IsNull())
            {
                throw new ArgumentNullException(nameof(agent));
            }

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