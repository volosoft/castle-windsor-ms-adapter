#nullable enable

using System;
using System.Linq;
using Castle.MicroKernel;
using Castle.MicroKernel.SubSystems.Naming;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

internal sealed class KeyedServicesNamingSubSystem : INamingSubSystem
{
    private readonly INamingSubSystem _inner;
    private readonly KeyedServiceRegistry _registry;

    public int ComponentCount => _inner.ComponentCount;

    public KeyedServicesNamingSubSystem(INamingSubSystem inner, KeyedServiceRegistry registry)
    {
        _inner = inner;
        _registry = registry;
    }

    public void Init(IKernelInternal kernel)
    {
    }

    public void Terminate() => _inner.Terminate();

    public void AddHandlerSelector(IHandlerSelector selector) => _inner.AddHandlerSelector(selector);

    public void AddHandlersFilter(IHandlersFilter filter) => _inner.AddHandlersFilter(filter);

    public bool Contains(string name) => _inner.Contains(name);

    public bool Contains(Type service) => (WindsorRegistrationHelper.RequiresBoxing(service) || _inner.Contains(service)) && GetHandler(service) != null;

    public IHandler[] GetAllHandlers() => _inner.GetAllHandlers();

    public IHandler? GetHandler(string name) => _inner.GetHandler(name);

    public void Register(IHandler handler) => _inner.Register(handler);

    public IHandler? GetHandler(Type service)
    {
        if (WindsorRegistrationHelper.RequiresBoxing(service) && _registry.FindUnkeyedValue(service) is { } name)
        {
            return _inner.GetHandler(name);
        }

        var handler = _inner.GetHandler(service);
        return handler == null || !IsKeyed(handler)
            ? handler
            : GetHandlers(service).FirstOrDefault(candidate => candidate.Supports(service));
    }

    public IHandler[] GetHandlers(Type service) => WindsorRegistrationHelper.RequiresBoxing(service)
        ? _registry.FindUnkeyedValueNames(service).Select(name => _inner.GetHandler(name))
            .Concat(_inner.GetHandlers(service).Where(handler => !IsKeyed(handler))).ToArray()
        : _inner.GetHandlers(service).Where(handler => !IsKeyed(handler)).ToArray();

    public IHandler[] GetAssignableHandlers(Type service) => WindsorRegistrationHelper.RequiresBoxing(service) ? GetHandlers(service)
        : _inner.GetAssignableHandlers(service).Where(handler => !IsKeyed(handler)).ToArray();

    internal bool HasKeyedHandlers(Type service) => _inner.GetHandlers(service).Any(IsKeyed);

    private bool IsKeyed(IHandler handler) => _registry.IsKeyedService(handler.ComponentModel.Name) ||
        handler.ComponentModel.ExtendedProperties[WindsorRegistrationHelper.ServiceTypeProperty] is Type type && WindsorRegistrationHelper.RequiresBoxing(type);
}
