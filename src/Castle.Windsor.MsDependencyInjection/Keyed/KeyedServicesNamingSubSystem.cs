#nullable enable

using System;
using System.Linq;
using Castle.MicroKernel;
using Castle.MicroKernel.SubSystems.Naming;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

internal sealed class KeyedServicesNamingSubSystem(INamingSubSystem inner, KeyedServiceRegistry registry) : INamingSubSystem
{
    public int ComponentCount => inner.ComponentCount;
    public void Init(IKernelInternal kernel) { }
    public void Terminate() => inner.Terminate();
    public void AddHandlerSelector(IHandlerSelector selector) => inner.AddHandlerSelector(selector);
    public void AddHandlersFilter(IHandlersFilter filter) => inner.AddHandlersFilter(filter);
    public bool Contains(string name) => inner.Contains(name);
    public bool Contains(Type service) => (WindsorRegistrationHelper.RequiresBoxing(service) || inner.Contains(service)) && GetHandler(service) != null;
    public IHandler[] GetAllHandlers() => inner.GetAllHandlers();
    public IHandler? GetHandler(string name) => inner.GetHandler(name);
    public void Register(IHandler handler) => inner.Register(handler);

    public IHandler? GetHandler(Type service)
    {
        if (WindsorRegistrationHelper.RequiresBoxing(service) && registry.FindUnkeyedValue(service) is { } name) return inner.GetHandler(name);
        var handler = inner.GetHandler(service);
        return handler == null || !IsKeyed(handler)
            ? handler
            : GetHandlers(service).FirstOrDefault(candidate => candidate.Supports(service));
    }

    public IHandler[] GetHandlers(Type service) => WindsorRegistrationHelper.RequiresBoxing(service)
        ? registry.FindUnkeyedValueNames(service).Select(name => inner.GetHandler(name))
            .Concat(inner.GetHandlers(service).Where(handler => !IsKeyed(handler))).ToArray()
        : inner.GetHandlers(service).Where(handler => !IsKeyed(handler)).ToArray();
    public IHandler[] GetAssignableHandlers(Type service) => WindsorRegistrationHelper.RequiresBoxing(service) ? GetHandlers(service)
        : inner.GetAssignableHandlers(service).Where(handler => !IsKeyed(handler)).ToArray();
    internal bool HasKeyedHandlers(Type service) => inner.GetHandlers(service).Any(IsKeyed);
    private bool IsKeyed(IHandler handler) => registry.IsKeyedService(handler.ComponentModel.Name) ||
        handler.ComponentModel.ExtendedProperties[WindsorRegistrationHelper.ServiceTypeProperty] is Type type && WindsorRegistrationHelper.RequiresBoxing(type);
}
