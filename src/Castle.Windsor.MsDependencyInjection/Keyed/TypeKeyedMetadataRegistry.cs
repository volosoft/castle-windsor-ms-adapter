#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

/// <summary>
/// Service to inspect type constructors and return information about keyed parameters.
/// </summary>
internal sealed class TypeKeyedMetadataRegistry
{
    private readonly ConcurrentDictionary<Type, TypeKeyedMetadata> _byType = new();

    /// <summary>
    /// Fast gate: true only if <paramref name="declaringType"/> was registered and has at
    /// least one keyed/service-key constructor parameter.
    /// </summary>
    public bool HasAnyKeyedParameter(Type declaringType)
    {
        return GetTypeMetadata(declaringType).HasKeyedParameters;
    }

    public bool TryGet(Type declaringType, ParameterInfo parameter, out KeyedParameterInfo? parameterInfo)
    {
        if (GetTypeMetadata(declaringType).TryGetParameter(parameter, out parameterInfo))
        {
            return true;
        }

        parameterInfo = null;
        return false;
    }

    private TypeKeyedMetadata GetTypeMetadata(Type type)
    {
        return _byType.GetOrAdd(type, BuildTypeMetadata);

        static TypeKeyedMetadata BuildTypeMetadata(Type type)
        {
            Dictionary<ParameterInfo, KeyedParameterInfo>? metadata = null;

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                foreach (var parameter in ctor.GetParameters())
                {
                    var fromKeyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
                    if (fromKeyed != null)
                    {
                        metadata ??= new();
                        metadata.Add(parameter, new KeyedParameterInfo(
                            KeyedParameterKind.FromKeyed,
                            fromKeyed.LookupMode,
                            fromKeyed.Key,
                            parameter.ParameterType));
                        continue;
                    }

                    if (parameter.IsDefined(typeof(ServiceKeyAttribute), inherit: true))
                    {
                        metadata ??= new();
                        metadata.Add(parameter, new KeyedParameterInfo(
                            KeyedParameterKind.ServiceKey,
                            ServiceKeyLookupMode.InheritKey,
                            null,
                            parameter.ParameterType));
                    }
                }
            }

            return new TypeKeyedMetadata(metadata?.ToFrozenDictionary());
        }
    }

    private sealed record TypeKeyedMetadata(FrozenDictionary<ParameterInfo, KeyedParameterInfo>? Metadata)
    {
        public bool HasKeyedParameters => Metadata != null;

        public bool TryGetParameter(ParameterInfo parameter, [NotNullWhen(true)] out KeyedParameterInfo? info)
        {
            info = null;
            return Metadata?.TryGetValue(parameter, out info) == true;
        }
    }
}