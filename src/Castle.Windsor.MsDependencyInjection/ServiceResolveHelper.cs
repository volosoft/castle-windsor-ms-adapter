#nullable enable

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Handlers;
using Castle.MicroKernel.SubSystems.Conversion;
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
        // Exact-service-type handler set (like MS DI's IEnumerable<T>), not the wider
        // GetAssignableHandlers. Reverse restores registration order for the adapter's .IsDefault()
        // registrations (Castle head-inserts them); keep it a non-mutating LINQ Reverse - GetHandlers
        // returns Castle's cached array, so an in-place Array.Reverse would corrupt it.
        return container.Kernel.GetHandlers(itemType)
            .Reverse()
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

    /// <summary>
    /// Shared core of the ctor-injected <c>IEnumerable&lt;T&gt;</c> paths
    /// (<see cref="MsCompatibleCollectionResolver"/> and the non-keyed branch of
    /// <see cref="KeyedServicesSubResolver"/>): a port of <c>DefaultKernel.ResolveAll</c> that drops
    /// keyed components (keyed/non-keyed isolation) and resolves each handler inside
    /// <paramref name="context"/>. Resolving in-context - not a by-name re-resolve - skips the in-flight
    /// handler (so a re-entrant graph such as Microsoft.Identity.Web's OpenIdConnectOptions chain is not
    /// mistaken for a cycle) and carries AdditionalArguments and burden ownership through.
    /// Returns <c>null</c> for an empty result the caller must re-run via <c>kernel.ResolveAll</c> so
    /// Castle's <c>EmptyCollectionResolving</c> event fires - but only when no keyed handler exists (else
    /// ResolveAll leaks keyed into the collection) and no handler ran (else a constraint-mismatched
    /// handler would run twice); otherwise an empty array is returned.
    /// </summary>
    public static Array? ResolveNonKeyedCollectionInContext(IKernel kernel, KeyedServiceRegistry registry, Type itemType, CreationContext context)
    {
        var converter = (IConversionManager)kernel.GetSubSystem(SubSystemConstants.ConversionManagerKey);
        var handlers = kernel.GetHandlers(itemType);
        var instances = new List<object>(handlers.Length);
        var hasKeyedHandler = false;
        var sawGenericMismatch = false;
        foreach (var handler in handlers)
        {
            if (registry.IsKeyedService(handler.ComponentModel.Name))
            {
                hasKeyedHandler = true;
                continue;
            }

            if (handler.IsBeingResolvedInContext(context))
            {
                continue; // in flight: skip like ResolveAll instead of re-entering it
            }

            var itemContext = new CreationContext(handler, kernel.ReleasePolicy, itemType, context.AdditionalArguments, converter, context);
            try
            {
                instances.Add(handler.Resolve(itemContext));
            }
            catch (GenericHandlerTypeMismatchException)
            {
                sawGenericMismatch = true; // open generic can't close over itemType
            }
        }

        // Empty, no handler invoked, nothing keyed to isolate: caller defers to ResolveAll (see summary).
        if (instances.Count == 0 && !hasKeyedHandler && !sawGenericMismatch)
        {
            return null;
        }

        instances.Reverse(); // GetHandlers returns .IsDefault() handlers reversed; restore registration order

        var array = Array.CreateInstance(itemType, instances.Count);
        ((ICollection)instances).CopyTo(array, 0);
        return array;
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
