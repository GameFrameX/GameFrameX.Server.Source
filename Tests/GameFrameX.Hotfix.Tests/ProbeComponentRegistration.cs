using System.Reflection;
using GameFrameX.Core.Abstractions.Attribute;
using GameFrameX.Core.Components;
using Xunit;

namespace GameFrameX.Hotfix.Tests;

/// <summary>只注册当前用例的探针组件，并在用例结束时恢复对应映射。</summary>
internal sealed class ProbeComponentRegistration : IDisposable
{
    private readonly Type _componentType;
    private readonly ushort _actorType;
    private readonly bool _wasRegistered;
    private readonly ushort _previousActorType;
    private readonly HashSet<Type> _previousComponents;
    private readonly Dictionary<ushort, HashSet<Type>> _actorComponents;
    private readonly object _registrationLock;

    public ProbeComponentRegistration(Type componentType)
    {
        _componentType = componentType;
        _actorType = componentType.GetCustomAttribute<ComponentTypeAttribute>().Type;
        var registerType = typeof(ComponentRegister);
        _registrationLock = registerType.GetField("InitLock", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        _actorComponents = (Dictionary<ushort, HashSet<Type>>)registerType
            .GetField("ActorComponentDic", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        lock (_registrationLock)
        {
            _wasRegistered = ComponentRegister.ComponentActorDic.TryGetValue(componentType, out _previousActorType);
            if (_actorComponents.TryGetValue(_actorType, out var components))
            {
                _previousComponents = new HashSet<Type>(components);
            }

            ComponentRegister.InitAsync(new ComponentProbeAssembly(componentType)).GetAwaiter().GetResult();
            Assert.Equal(_actorType, ComponentRegister.GetActorType(componentType));
        }
    }

    public void Dispose()
    {
        lock (_registrationLock)
        {
            if (_wasRegistered)
            {
                ComponentRegister.ComponentActorDic[_componentType] = _previousActorType;
            }
            else
            {
                ComponentRegister.ComponentActorDic.Remove(_componentType);
            }

            if (_previousComponents != null)
            {
                _actorComponents[_actorType] = _previousComponents;
            }
            else
            {
                _actorComponents.Remove(_actorType);
            }
        }
    }

    private sealed class ComponentProbeAssembly(Type componentType) : Assembly
    {
        public override Type[] GetTypes()
        {
            return new[] { componentType };
        }
    }
}
