#nullable enable

using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Resolvers;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

/// <summary>
/// Resolves constructor parameters annotated with <c>[FromKeyedServices]</c> or <c>[ServiceKey]</c>
/// </summary>
internal sealed class KeyedServicesSubResolver : ISubDependencyResolver
{
    private readonly TypeKeyedMetadataRegistry _typeMetadataRegistry;
    private readonly KeyedServiceRegistry _keyedRegistry;
    private readonly ScopedWindsorServiceProvider _serviceProvider;

    public KeyedServicesSubResolver(
        TypeKeyedMetadataRegistry typeMetadataRegistry,
        KeyedServiceRegistry keyedRegistry,
        ScopedWindsorServiceProvider serviceProvider)
    {
        _typeMetadataRegistry = typeMetadataRegistry;
        _keyedRegistry = keyedRegistry;
        _serviceProvider = serviceProvider;
    }

    public bool CanResolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
    {
        if (!TryGetKeyedParameter(dependency, out var parameterInfo))
        {
            return false;
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

        return true;
    }

    public object Resolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
    {
        if (!TryGetKeyedParameter(dependency, out var parameterInfo))
        {
            throw new DependencyResolverException($"Parameter '{dependency.DependencyKey}' of '{model.Implementation}' is not a keyed-services parameter.");
        }

        return parameterInfo.Kind switch
        {
            KeyedParameterKind.ServiceKey => ResolveServiceKey(model, dependency, parameterInfo),
            KeyedParameterKind.FromKeyed => ResolveFromKeyed(model, dependency, parameterInfo),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private object ResolveFromKeyed(ComponentModel model, DependencyModel dependency, KeyedParameterInfo parameterInfo)
    {
        object? serviceProviderValue;
        var parameterType = parameterInfo.ParameterType;

        switch (parameterInfo.LookupMode)
        {
            // [FromKeyedServices(null)] behaves like a plain non-keyed injection.
            case ServiceKeyLookupMode.NullKey:
                serviceProviderValue = _serviceProvider.GetService(parameterType);
                break;

            // Parameterless [FromKeyedServices]: resolve with the key the enclosing
            // component was itself resolved with.
            case ServiceKeyLookupMode.InheritKey:
                serviceProviderValue = _keyedRegistry.TryGetServiceKeyByWindsorName(model.Name, out var inheritedKey)
                    ? _serviceProvider.GetKeyedService(parameterType, serviceKey: inheritedKey)
                    // Enclosing component is not keyed -> behaves as a non-keyed injection.
                    : _serviceProvider.GetService(parameterType);
                break;

            case ServiceKeyLookupMode.ExplicitKey:
                serviceProviderValue = _serviceProvider.GetKeyedService(parameterType, serviceKey: parameterInfo.Key);
                break;

            default:
                throw new ArgumentOutOfRangeException();
        }

        if (serviceProviderValue is not null)
        {
            return serviceProviderValue;
        }

        if (dependency.HasDefaultValue)
        {
            return dependency.DefaultValue;
        }

        throw new InvalidOperationException($"No service for type '{parameterInfo.ParameterType}' has been registered with key '{parameterInfo.Key}'.");
    }

    private object ResolveServiceKey(ComponentModel model, DependencyModel dependency, KeyedParameterInfo parameter)
    {
        if (!_keyedRegistry.TryGetServiceKeyByWindsorName(model.Name, out var key))
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

        static bool IsAssignableKey(object? key, Type parameterType)
        {
            if (key == null)
            {
                return !parameterType.IsValueType || Nullable.GetUnderlyingType(parameterType) != null;
            }

            return parameterType.IsInstanceOfType(key);
        }
    }

    private bool TryGetKeyedParameter(DependencyModel dependency, [NotNullWhen(true)] out KeyedParameterInfo? parameterInfo)
    {
        parameterInfo = null;

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

        return _typeMetadataRegistry.TryGet(declaringType, matchedParameter, out parameterInfo);
    }
}