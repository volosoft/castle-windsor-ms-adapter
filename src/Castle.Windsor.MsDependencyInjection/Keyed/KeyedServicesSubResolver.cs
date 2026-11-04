#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Handlers;
using Castle.MicroKernel.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

/// <summary>
/// Resolves constructor parameters annotated with <c>[FromKeyedServices]</c> or <c>[ServiceKey]</c>.
/// <para>
/// Uses the container directly (instead of going through <see cref="ScopedWindsorServiceProvider"/>)
/// so the ambient <see cref="MsLifetimeScope.Current"/> of the in-flight resolution is preserved.
/// Routing through a service-provider instance would push that provider's own
/// <c>OwnMsLifetimeScope</c> onto the AsyncLocal, and the captured-at-registration provider
/// holds a null scope — which silently downgrades scoped keyed services to transient.
/// </para>
/// </summary>
internal sealed class KeyedServicesSubResolver : ISubDependencyResolver
{
    [ThreadStatic]
    private static (CreationContext? Context, DependencyModel? Dependency) _probing;

    private readonly TypeKeyedMetadataRegistry _typeMetadataRegistry;
    private readonly KeyedServiceRegistry _keyedRegistry;
    private readonly IWindsorContainer _container;

    public KeyedServicesSubResolver(
        TypeKeyedMetadataRegistry typeMetadataRegistry,
        KeyedServiceRegistry keyedRegistry,
        IWindsorContainer container)
    {
        _typeMetadataRegistry = typeMetadataRegistry;
        _keyedRegistry = keyedRegistry;
        _container = container;
    }

    public bool CanResolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
    {
        if (!TryGetKeyedParameter(model, dependency, out var parameterInfo))
        {
            if (ReferenceEquals(_probing.Context, context) && ReferenceEquals(_probing.Dependency, dependency)) return false;
            var registration = FindRegistration(context, dependency);
            if (registration == null) return false;
            if (!WindsorRegistrationHelper.RequiresBoxing(dependency.TargetItemType))
            {
                // A registered factory may supply null through Windsor's normal fallback.
                if (registration.ComponentModel.ExtendedProperties[NullFactoryResult.FactoryMarker] is true)
                    dependency.IsOptional = true;
                return false;
            }
            var previous = _probing;
            _probing = (context, dependency);
            try { return !_container.Kernel.Resolver.CanResolve(context, contextHandlerResolver, model, dependency); }
            finally { _probing = previous; }
        }

        // A [ServiceKey] parameter can only be filled when the enclosing component was
        // resolved with a key. For a non-keyed resolution it is unresolvable - report that
        // so Windsor picks a different constructor instead of selecting this one and
        // failing, matching MS DI (which falls back to a keyless constructor).
        if (parameterInfo.Kind == KeyedParameterKind.ServiceKey
            && !_keyedRegistry.IsKeyedService(model.Name))
        {
            return false;
        }

        if (parameterInfo.Kind == KeyedParameterKind.ServiceKey) return true;
        if (dependency.HasDefaultValue || ServiceResolveHelper.IsEnumerable(parameterInfo.ParameterType)) return true;

        var available = parameterInfo.LookupMode == ServiceKeyLookupMode.NullKey
            ? _container.Kernel.HasComponent(parameterInfo.ParameterType)
            : parameterInfo.LookupMode == ServiceKeyLookupMode.InheritKey
                ? _keyedRegistry.TryGetLookupKey(context, model.Name, out var key)
                    ? _keyedRegistry.HasExplicitOrAnyKey(new KeyedServiceId(parameterInfo.ParameterType, key))
                    : _container.Kernel.HasComponent(parameterInfo.ParameterType)
                : _keyedRegistry.HasExplicitOrAnyKey(new KeyedServiceId(parameterInfo.ParameterType, parameterInfo.Key));
        return available || model.Constructors.Count == 1;
    }

    public object Resolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
    {
        if (!TryGetKeyedParameter(model, dependency, out var parameterInfo))
        {
            var factory = FindRegistration(context, dependency);
            if (factory != null)
                return NullFactoryResult.Unwrap(ServiceResolveHelper.ResolveInContext(_container.Kernel, factory, dependency.TargetItemType, context))!;
            throw new DependencyResolverException($"Parameter '{dependency.DependencyKey}' of '{model.Implementation}' is not a keyed-services parameter.");
        }

        return parameterInfo.Kind switch
        {
            KeyedParameterKind.ServiceKey => ResolveServiceKey(context, model, dependency, parameterInfo),
            KeyedParameterKind.FromKeyed => ResolveFromKeyed(context, model, dependency, parameterInfo),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private object ResolveFromKeyed(CreationContext context, ComponentModel model, DependencyModel dependency, KeyedParameterInfo parameterInfo)
    {
        var parameterType = parameterInfo.ParameterType;
        object? resolved;

        switch (parameterInfo.LookupMode)
        {
            // [FromKeyedServices(null)] behaves like a plain non-keyed injection.
            case ServiceKeyLookupMode.NullKey:
                resolved = TryResolveNonKeyed(context, parameterType);
                break;

            // Parameterless [FromKeyedServices]: resolve with the key the enclosing
            // component was itself resolved with; non-keyed enclosing -> non-keyed injection.
            case ServiceKeyLookupMode.InheritKey:
                resolved = _keyedRegistry.TryGetLookupKey(context, model.Name, out var inheritedKey)
                    ? TryResolveKeyed(context, parameterType, inheritedKey)
                    : TryResolveNonKeyed(context, parameterType);
                break;

            case ServiceKeyLookupMode.ExplicitKey:
                resolved = TryResolveKeyed(context, parameterType, parameterInfo.Key);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        if (resolved is NullFactoryResult) return null!;
        if (resolved is not null)
        {
            return resolved;
        }

        if (dependency.HasDefaultValue)
        {
            return dependency.DefaultValue;
        }

        throw new InvalidOperationException($"No service for type '{parameterInfo.ParameterType}' has been registered with key '{parameterInfo.Key}'.");
    }

    private object? TryResolveKeyed(CreationContext context, Type type, object? key)
    {
        var name = _keyedRegistry.TryResolveWindsorKeyForService(new KeyedServiceId(type, key));
        if (name != null)
            return ServiceResolveHelper.ResolveKeyedByName(_container, name, new KeyedServiceId(type, key), trackingScope: null, context);

        if (ServiceResolveHelper.IsEnumerable(type))
        {
            var itemType = type.GenericTypeArguments[0];
            var names = _keyedRegistry.ResolveAllWindsorKeysForService(new KeyedServiceId(itemType, key));
            return ServiceResolveHelper.ResolveAllByName(_container, itemType, names, trackingScope: null, context, _keyedRegistry, key);
        }
        return null;
    }

    private IHandler? FindRegistration(CreationContext context, DependencyModel dependency)
    {
        if (dependency.Parameter != null || dependency.TargetItemType == null) return null;
        var valueName = _keyedRegistry.FindUnkeyedValue(dependency.TargetItemType);
        if (valueName != null) return _container.Kernel.GetHandler(valueName);
        var handler = _container.Kernel.GetHandler(dependency.TargetItemType);
        return handler?.ComponentModel.ExtendedProperties[WindsorRegistrationHelper.ServiceTypeProperty] is Type && !handler.IsBeingResolvedInContext(context) ? handler : null;
    }

    private object? TryResolveNonKeyed(CreationContext context, Type type)
    {
        var valueName = _keyedRegistry.FindUnkeyedValue(type);
        if (valueName != null)
            return ServiceResolveHelper.ResolveInContext(_container.Kernel, _container.Kernel.GetHandler(valueName), type, context);
        if (_container.Kernel.HasComponent(type))
            return ServiceResolveHelper.ResolveInContext(_container.Kernel, _container.Kernel.GetHandler(type), type, context);

        if (ServiceResolveHelper.IsEnumerable(type))
        {
            var itemType = type.GenericTypeArguments[0];
            var array = ServiceResolveHelper.ResolveNonKeyedCollectionInContext(_container.Kernel, itemType, context);
            return array ?? _container.Kernel.ResolveAll(itemType, context.AdditionalArguments);
        }
        return null;
    }

    private object ResolveServiceKey(CreationContext context, ComponentModel model, DependencyModel dependency, KeyedParameterInfo parameter)
    {
        if (!_keyedRegistry.TryGetLookupKey(context, model.Name, out var key))
        {
            throw new InvalidOperationException(
                $"[ServiceKey] parameter '{dependency.DependencyKey}' of '{model.Implementation}' " +
                "can only be injected into a keyed service.");
        }

        if (!IsAssignableKey(key, parameter.ParameterType))
        {
            throw new InvalidOperationException(
                $"The service key of type '{key?.GetType()}' is not assignable to parameter " +
                $"'{dependency.DependencyKey}' of type '{parameter.ParameterType}' on '{model.Implementation}'.");
        }

        return key;

        static bool IsAssignableKey(object? key, Type parameterType) =>
            parameterType == typeof(object) || key != null && parameterType == key.GetType();
    }

    private bool TryGetKeyedParameter(ComponentModel model, DependencyModel dependency, [NotNullWhen(true)] out KeyedParameterInfo? parameterInfo)
    {
        parameterInfo = null;
        if (dependency.Parameter != null && dependency.Parameter.Value != KeyedConstructorDependencyContributor.Reference)
            return false;

        if (dependency is not ConstructorDependencyModel ctorDependency)
        {
            return false;
        }

        var ctor = ctorDependency.Constructor?.Constructor;
        var declaringType = ctor?.DeclaringType;
        if (ctor == null || declaringType == null)
        {
            return false;
        }

        if (!_typeMetadataRegistry.HasAnyKeyedParameter(declaringType))
        {
            return false;
        }

        var matchedParameter = ctor.GetParameters().FirstOrDefault(x => x.Name == dependency.DependencyKey);
        if (matchedParameter == null)
        {
            return false;
        }

        if (!_typeMetadataRegistry.TryGet(declaringType, matchedParameter, out var info)) return false;
        parameterInfo = info?.ForComponent(_keyedRegistry.IsKeyedService(model.Name));
        return parameterInfo != null;
    }
}
