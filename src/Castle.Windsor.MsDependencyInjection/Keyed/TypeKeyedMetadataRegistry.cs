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
/// <para>
/// Entries are keyed by constructor metadata token and parameter position, not by
/// <see cref="ParameterInfo"/> reference: the runtime materializes ParameterInfo arrays lazily
/// and without synchronization, so threads racing the first <see cref="MethodBase.GetParameters"/>
/// call can observe different instances for the same parameter. Token and position are stable
/// across those generations.
/// </para>
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
            Dictionary<int, KeyedParameterInfo?[]>? metadata = null;

            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var parameters = ctor.GetParameters();
                KeyedParameterInfo?[]? slots = null;

                foreach (var parameter in parameters)
                {
                    var info = InspectParameter(parameter);
                    if (info != null)
                    {
                        slots ??= new KeyedParameterInfo?[parameters.Length];
                        slots[parameter.Position] = info;
                    }
                }

                if (slots != null)
                {
                    metadata ??= new();
                    metadata.Add(ctor.MetadataToken, slots);
                }
            }

            return new TypeKeyedMetadata(metadata?.ToFrozenDictionary());
        }

        static KeyedParameterInfo? InspectParameter(ParameterInfo parameter)
        {
            var fromKeyed = parameter.GetCustomAttribute<FromKeyedServicesAttribute>();
            if (fromKeyed != null)
            {
                return new KeyedParameterInfo(
                    KeyedParameterKind.FromKeyed,
                    fromKeyed.LookupMode,
                    fromKeyed.Key,
                    parameter.ParameterType);
            }

            if (parameter.IsDefined(typeof(ServiceKeyAttribute), inherit: true))
            {
                return new KeyedParameterInfo(
                    KeyedParameterKind.ServiceKey,
                    ServiceKeyLookupMode.InheritKey,
                    null,
                    parameter.ParameterType);
            }

            return null;
        }
    }

    private sealed record TypeKeyedMetadata(FrozenDictionary<int, KeyedParameterInfo?[]>? Metadata)
    {
        public bool HasKeyedParameters => Metadata != null;

        public bool TryGetParameter(ParameterInfo parameter, [NotNullWhen(true)] out KeyedParameterInfo? info)
        {
            info = null;

            if (Metadata == null || parameter.Member is not ConstructorInfo ctor)
            {
                return false;
            }

            if (!Metadata.TryGetValue(ctor.MetadataToken, out var slots)
                || (uint)parameter.Position >= (uint)slots.Length)
            {
                return false;
            }

            info = slots[parameter.Position];
            return info != null;
        }
    }
}
