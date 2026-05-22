#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel.Handlers;
using Castle.Windsor.MsDependencyInjection.Keyed;

namespace Castle.Windsor.MsDependencyInjection;

internal static class ServiceResolveHelper
{
    public static bool IsEnumerable(Type type)
    {
        return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
    }

    public static bool HasNonKeyedComponent(IWindsorContainer container, KeyedServiceRegistry registry, Type serviceType)
    {
        if (!container.Kernel.HasComponent(serviceType))
        {
            return false;
        }

        foreach (var handler in container.Kernel.GetHandlers(serviceType))
        {
            if (!registry.IsKeyedService(handler.ComponentModel.Name))
            {
                return true;
            }
        }

        // Also check open-generic handlers if the type is constructed-generic.
        if (serviceType.IsConstructedGenericType)
        {
            foreach (var handler in container.Kernel.GetHandlers(serviceType.GetGenericTypeDefinition()))
            {
                if (!registry.IsKeyedService(handler.ComponentModel.Name))
                {
                    return true;
                }
            }
        }

        return false;
    }

    public static IEnumerable<string> GetNonKeyedHandlerNames(IWindsorContainer container, KeyedServiceRegistry registry, Type itemType)
    {
        return container.Kernel.GetAssignableHandlers(itemType)
            .Select(handler => handler.ComponentModel.Name)
            .Where(name => !registry.IsKeyedService(name));
    }
    
    public static object Resolve(IWindsorContainer container,Type serviceType, IMsLifetimeScope? trackingScope)
    {
        var instance = container.Resolve(serviceType);
        trackingScope?.AddInstance(instance);
        return instance;
    }

    public static object ResolveByName(IWindsorContainer container, string windsorName, Type serviceType, IMsLifetimeScope? trackingScope)
    {
        var instance = container.Resolve(windsorName, serviceType);
        trackingScope?.AddInstance(instance);
        return instance;
    }

    public static Array ResolveAllByName(IWindsorContainer container, Type itemType, IEnumerable<string> names, IMsLifetimeScope? trackingScope)
    {
        var instances = new List<object>();
        foreach (var name in names)
        {
            try
            {
                instances.Add(ResolveByName(container, name, itemType, trackingScope));
            }
            catch (GenericHandlerTypeMismatchException)
            {
                // Open-generic handler whose constraints can't satisfy this closed type - mirror ResolveAll.
            }
        }

        var array = Array.CreateInstance(itemType, instances.Count);
        ((ICollection)instances).CopyTo(array, 0);
        return array;
    }
}
