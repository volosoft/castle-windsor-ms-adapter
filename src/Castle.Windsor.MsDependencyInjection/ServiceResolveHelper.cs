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
    internal static bool IsScopeBoundary(IHandler handler) => handler.ComponentModel.Implementation == typeof(ScopedResolution);

    public static bool HasNonKeyedComponent(IWindsorContainer container, KeyedServiceRegistry registry, Type serviceType)
        => container.Kernel.HasComponent(serviceType);

    public static IEnumerable<string> GetNonKeyedHandlerNames(IWindsorContainer container, KeyedServiceRegistry registry, Type itemType)
        => GetNonKeyedHandlerNames(container, itemType);

    public static Array? ResolveNonKeyedCollectionInContext(IKernel kernel, KeyedServiceRegistry registry, Type itemType, CreationContext context)
        => ResolveNonKeyedCollectionInContext(kernel, itemType, context);

    public static bool IsEnumerable(Type type)
    {
        return type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(IEnumerable<>);
    }

    public static IEnumerable<string> GetNonKeyedHandlerNames(IWindsorContainer container, Type itemType)
    {
        // Exact-service-type handler set (like MS DI's IEnumerable<T>), not the wider
        // GetAssignableHandlers. Reverse restores registration order for the adapter's .IsDefault()
        // registrations (Castle head-inserts them); keep it a non-mutating LINQ Reverse - GetHandlers
        // returns Castle's cached array, so an in-place Array.Reverse would corrupt it.
        return container.Kernel.GetHandlers(itemType)
            .Reverse()
            .Select(handler => handler.ComponentModel.Name);
    }

    public static object Resolve(IWindsorContainer container, Type serviceType, IMsLifetimeScope? trackingScope)
    {
        var instance = container.Resolve(serviceType);
        Track(container.Kernel, instance, trackingScope, container.Kernel.GetHandler(serviceType)?.ComponentModel);
        return instance;
    }

    public static object ResolveByName(IWindsorContainer container, string windsorName, Type serviceType, IMsLifetimeScope? trackingScope)
    {
        var instance = container.Resolve(windsorName, WindsorRegistrationHelper.RequiresBoxing(serviceType) ? typeof(object) : serviceType);
        Track(container.Kernel, instance, trackingScope, container.Kernel.GetHandler(windsorName)?.ComponentModel);
        return instance;
    }

    public static object ResolveKeyedByName(IWindsorContainer container, string name, KeyedServiceId id,
        IMsLifetimeScope? trackingScope, CreationContext? context = null)
    {
        using var lookup = KeyedServiceRegistry.UseLookup(name, id.Key);
        if (context != null)
        {
            return ResolveInContext(container.Kernel, container.Kernel.GetHandler(name), id.ServiceType, context);
        }

        return ResolveByName(container, name, id.ServiceType, trackingScope);
    }

    private static void Track(IKernel kernel, object instance, IMsLifetimeScope? trackingScope, Castle.Core.ComponentModel? model)
    {
        if (model?.ExtendedProperties[WindsorRegistrationHelper.ServiceTypeProperty] is Type)
        {
            if (MsScopeState.Resolve(kernel, trackingScope) == null)
            {
                trackingScope?.AddInstance(instance);
            }

            return;
        }
        if (model != null && MsOwnedScopeResolution.CurrentContext is { } context &&
            MsLifetimeScope.Current is GlobalMsLifetimeScope rootOwner)
        {
            rootOwner.RecordNativeDependency(context, kernel.GetHandler(model.Name)?.ComponentModel ?? model);
        }

        var target = Castle.DynamicProxy.ProxyUtil.GetUnproxiedInstance(instance);
        var scopeOwner = MsScopeState.Resolve(kernel, trackingScope);
        var transient = model?.LifestyleType == Castle.Core.LifestyleType.Transient ||
            model?.CustomLifestyle != null && typeof(Castle.MicroKernel.Lifestyle.TransientLifestyleManager).IsAssignableFrom(model.CustomLifestyle);
        if (transient && target is IAsyncDisposable asynchronous && target is not IDisposable &&
            model?.ExtendedProperties["factory.managedExternally"] is not true &&
            scopeOwner is { } owner)
        {
            if (!owner.ReleasePolicy.HasTrack(instance) && !kernel.ReleasePolicy.HasTrack(instance))
            {
                owner.AddNativeAsync(instance, asynchronous);
                return;
            }
            owner.RequireAsyncDisposal();
        }
        var tracked = scopeOwner?.ReleasePolicy.HasTrack(instance) == true || kernel.ReleasePolicy.HasTrack(instance);
        if (model != null && scopeOwner is GlobalMsLifetimeScope root && tracked)
        {
            root.TrackReleaseDependency(kernel.GetHandler(model.Name).ComponentModel);
        }

        if (tracked)
        {
            trackingScope?.AddInstance(instance);
        }
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
    public static Array? ResolveNonKeyedCollectionInContext(IKernel kernel, Type itemType, CreationContext context)
    {
        var handlers = kernel.GetHandlers(itemType);
        var instances = new List<object>(handlers.Length);
        var hasKeyedHandler = kernel.GetSubSystem(SubSystemConstants.NamingKey) is KeyedServicesNamingSubSystem naming && naming.HasKeyedHandlers(itemType);
        var sawGenericMismatch = false;
        var preserveNativeOrder = handlers.Any(handler => handler.ComponentModel.ExtendedProperties[WindsorRegistrationHelper.ServiceTypeProperty] is not Type);
        foreach (var handler in preserveNativeOrder ? handlers : handlers.Reverse())
        {
            if (handler.IsBeingResolvedInContext(context))
            {
                continue; // in flight: skip like ResolveAll instead of re-entering it
            }

            try
            {
                instances.Add(ResolveInContext(kernel, handler, itemType, context));
            }
            catch (GenericHandlerTypeMismatchException) when (handler.ComponentModel.Implementation.IsGenericTypeDefinition)
            {
                sawGenericMismatch = true; // open generic can't close over itemType
            }
        }

        // Empty, no handler invoked, nothing keyed to isolate: caller defers to ResolveAll (see summary).
        if (instances.Count == 0 && !hasKeyedHandler && !sawGenericMismatch)
        {
            return null;
        }

        if (preserveNativeOrder)
        {
            instances.Reverse();
        }

        var array = Array.CreateInstance(itemType, instances.Count);
        ((ICollection)instances.Select(NullFactoryResult.Unwrap).ToArray()).CopyTo(array, 0);
        return array;
    }

    internal static object ResolveInContext(IKernel kernel, IHandler handler, Type serviceType, CreationContext context, Arguments? arguments = null)
    {
        var converter = (IConversionManager)kernel.GetSubSystem(SubSystemConstants.ConversionManagerKey);
        var child = new CreationContext(handler, context.ReleasePolicy, WindsorRegistrationHelper.RequiresBoxing(serviceType) ? typeof(object) : serviceType,
            arguments ?? context.AdditionalArguments, converter, context);
        var instance = handler.Resolve(child);
        var owner = context.SelectScopeRoot(handlers => handlers.LastOrDefault())?.Handler.ComponentModel ?? context.Handler?.ComponentModel;
        if (owner != null && owner.Implementation != typeof(ScopedResolution))
        {
            var registeredOwner = kernel.GetHandler(owner.Name)?.ComponentModel ?? owner;
            if (!ReferenceEquals(registeredOwner, handler.ComponentModel) && !registeredOwner.Dependents.Contains(handler.ComponentModel))
            {
                registeredOwner.AddDependent(handler.ComponentModel);
            }
        }
        return instance;
    }

    public static Array ResolveAllByName(IWindsorContainer container, Type itemType, IEnumerable<string> names, IMsLifetimeScope? trackingScope,
        CreationContext? context = null, KeyedServiceRegistry? registry = null, object? key = null)
    {
        var instances = new List<object>();
        foreach (var name in names)
        {
            var handler = container.Kernel.GetHandler(name);
            try
            {
                if (registry != null)
                {
                    var itemKey = key;
                    if (key == Microsoft.Extensions.DependencyInjection.KeyedService.AnyKey)
                    {
                        registry.TryGetServiceKeyByWindsorName(name, out itemKey);
                    }

                    instances.Add(ResolveKeyedByName(container, name, new KeyedServiceId(itemType, itemKey), trackingScope, context));
                }
                else
                {
                    instances.Add(context == null ? ResolveByName(container, name, itemType, trackingScope)
                    : ResolveInContext(container.Kernel, container.Kernel.GetHandler(name), itemType, context));
                }
            }
            catch (GenericHandlerTypeMismatchException) when (handler.ComponentModel.Implementation.IsGenericTypeDefinition)
            {
                // Open-generic handler whose constraints can't satisfy this closed type - mirror ResolveAll.
            }
        }

        var array = Array.CreateInstance(itemType, instances.Count);
        ((ICollection)instances.Select(NullFactoryResult.Unwrap).ToArray()).CopyTo(array, 0);
        return array;
    }
}
