#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

/// <summary>
/// Tracks the mapping between MS DI keyed service descriptors and the Castle Windsor
/// component names that fulfill them. AnyKey registrations are stored as <em>templates</em>
/// that are lazily expanded into real Windsor components on first resolution of a given actual key.
/// </summary>
internal sealed class KeyedServiceRegistry
{
    internal const string KeyedNamePrefix = "MsKeyed_";

    private readonly object _sync = new();

    // All entries in registration order. Templates have IsAnyKey = true.
    // Lazy expansions are appended as IsAnyKeyExpansion = true entries as they happen.
    private readonly Dictionary<Type, List<KeyedEntry>> _byType = new();

    private readonly Dictionary<AnyKeyTemplateExpansionKey, string> _anyKeyExpansions = new();

    // Reverse map: Windsor component name -> the service key it was registered/expanded for.
    private readonly Dictionary<string, object?> _windsorNameToKey = new(StringComparer.Ordinal);

    private int _nextAnyKeyTemplateId;

    private static string GenerateRandomWindsorName() => KeyedNamePrefix + Guid.NewGuid().ToString("N");

    public string RegisterExplicitKeyService(KeyedServiceId id)
    {
        lock (_sync)
        {
            var windsorName = GenerateRandomWindsorName();

            GetOrAddByTypeList(id.ServiceType).Add(KeyedEntry.ExplicitKey(windsorName, id.Key));
            _windsorNameToKey[windsorName] = id.Key;

            return windsorName;
        }
    }

    public void RegisterAnyKeyService(KeyedServiceId id, Action<string, object?> doRegisterExpansion)
    {
        lock (_sync)
        {
            var templateId = _nextAnyKeyTemplateId++;
            var template = new AnyKeyTemplate(templateId, id.ServiceType, doRegisterExpansion);
            GetOrAddByTypeList(id.ServiceType).Add(KeyedEntry.AnyKey(template));
        }
    }

    public string? TryResolveWindsorKeyForService(KeyedServiceId serviceId)
    {
        lock (_sync)
        {
            if (TryGetLastExplicitKeyName(serviceId, out var windsorName))
            {
                return windsorName;
            }

            if (TryGetLastAnyKeyTemplate(serviceId.ServiceType, out var template))
            {
                return ExpandTemplateOrGetExistingExpansion(template, serviceId.Key);
            }

            return null;
        }
    }

    public IReadOnlyCollection<string> ResolveAllWindsorKeysForService(KeyedServiceId serviceId)
    {
        lock (_sync)
        {
            // Could contain duplicates, as AnyKey expansion could be enumerated twice
            // So use HashSet to deduplicate
            var result = new HashSet<string>();

            CollectMatches(serviceId.ServiceType);
            if (serviceId.ServiceType.IsConstructedGenericType)
            {
                CollectMatches(serviceId.ServiceType.GetGenericTypeDefinition());
            }

            return result;

            void CollectMatches(Type lookupType)
            {
                if (!_byType.TryGetValue(lookupType, out var list))
                {
                    return;
                }

                // Need to make a snapshot of list, as expansion will mutate it.
                foreach (KeyedEntry entry in list.ToArray())
                {
                    if (serviceId.Key == KeyedService.AnyKey)
                    {
                        if (!entry.IsAnyKey && !entry.IsAnyKeyExpansion)
                        {
                            result.Add(entry.WindsorName!);
                        }
                    }
                    else
                    {
                        if (Equals(entry.Key, serviceId.Key))
                        {
                            result.Add(entry.WindsorName!);
                        }
                    }
                }
            }
        }
    }

    public bool HasExplicitOrAnyKey(KeyedServiceId id)
    {
        lock (_sync)
        {
            if (HasByType(id.ServiceType))
            {
                return true;
            }

            if (id.ServiceType.IsConstructedGenericType
                && HasByType(id.ServiceType.GetGenericTypeDefinition()))
            {
                return true;
            }
        }

        return false;

        bool HasByType(Type type)
        {
            if (!_byType.TryGetValue(type, out var list))
            {
                return false;
            }

            return list.Any(entry => entry.IsAnyKey || Equals(entry.Key, id.Key));
        }
    }

    public bool IsKeyedService(string? windsorName)
    {
        if (windsorName == null)
        {
            return false;
        }

        lock (_sync)
        {
            return _windsorNameToKey.ContainsKey(windsorName);
        }
    }


    public bool TryGetServiceKeyByWindsorName(string? windsorName, [NotNullWhen(true)] out object? key)
    {
        if (windsorName == null)
        {
            key = null;
            return false;
        }

        lock (_sync)
        {
            return _windsorNameToKey.TryGetValue(windsorName, out key);
        }
    }

    /// <summary>
    /// Check if we already have template expansion. If so - return cached, otherwise expand template for key.
    /// </summary>
    private string ExpandTemplateOrGetExistingExpansion(AnyKeyTemplate template, object? actualKey)
    {
        var expansionKey = new AnyKeyTemplateExpansionKey(template.Id, actualKey);
        if (_anyKeyExpansions.TryGetValue(expansionKey, out var existing))
        {
            return existing;
        }

        // The name -> key mapping must exist BEFORE the Windsor component is registered:
        // registration makes Windsor evaluate the constructor, and a [ServiceKey] parameter
        // is only deemed resolvable when the component name is already known to be keyed
        // (same ordering RegisterExplicitKeyService relies on).
        var name = GenerateRandomWindsorName();
        _anyKeyExpansions[expansionKey] = name;
        _windsorNameToKey[name] = actualKey;

        // Also publish the expansion as an entry so future TryGetLastExplicitKeyName
        // calls take the fast path.
        GetOrAddByTypeList(template.ServiceType).Add(KeyedEntry.AnyKeyExpansion(name, actualKey));

        template.DoRegisterExpansion.Invoke(name, actualKey);

        return name;
    }

    private bool TryGetLastExplicitKeyName(KeyedServiceId id, [NotNullWhen(true)] out string? windsorName)
    {
        if (TryGetByType(id.ServiceType, out windsorName))
        {
            return true;
        }

        if (id.ServiceType.IsConstructedGenericType
            && TryGetByType(id.ServiceType.GetGenericTypeDefinition(), out windsorName))
        {
            return true;
        }

        windsorName = null;
        return false;

        bool TryGetByType(Type type, [NotNullWhen(true)] out string? windsorName)
        {
            windsorName = _byType
                .GetValueOrDefault(type)?
                .LastOrDefault(x => Equals(x.Key, id.Key))?
                .WindsorName;

            return windsorName != null;
        }
    }

    private bool TryGetLastAnyKeyTemplate(Type serviceType, [NotNullWhen(true)] out AnyKeyTemplate? template)
    {
        if (TryGetByType(serviceType, out template))
        {
            return true;
        }

        if (serviceType.IsConstructedGenericType
            && TryGetByType(serviceType.GetGenericTypeDefinition(), out template))
        {
            return true;
        }

        template = null;
        return false;

        bool TryGetByType(Type type, [NotNullWhen(true)] out AnyKeyTemplate? template)
        {
            template = _byType
                .GetValueOrDefault(type)?
                .LastOrDefault(x => x.IsAnyKey)?
                .Template;

            return template != null;
        }
    }

    private List<KeyedEntry> GetOrAddByTypeList(Type serviceType)
    {
        if (!_byType.TryGetValue(serviceType, out var list))
        {
            list = new List<KeyedEntry>();
            _byType[serviceType] = list;
        }
        return list;
    }

    /// <summary>
    /// A template for an AnyKey registration. Holds everything needed to dynamically create
    /// a specific Windsor component once an actual key is observed at resolve time.
    /// </summary>
    private record AnyKeyTemplate(
        int Id,
        Type ServiceType,
        Action<string, object?> DoRegisterExpansion);


    private readonly record struct AnyKeyTemplateExpansionKey(int TemplateId, object? ServiceKey);

    /// <summary>
    /// A single registration in the registry. Either a specific-key entry (with a fixed
    /// Windsor component name) or an AnyKey template (which needs lazy expansion).
    /// </summary>
    private record KeyedEntry(
        bool IsAnyKey,
        bool IsAnyKeyExpansion,
        string? WindsorName,
        object? Key,
        AnyKeyTemplate Template)
    {
        public static KeyedEntry ExplicitKey(string windsorName, object? key)
            => new(IsAnyKey: false, IsAnyKeyExpansion: false, WindsorName: windsorName, Key: key, Template: default);

        public static KeyedEntry AnyKeyExpansion(string windsorName, object? key)
            => new(IsAnyKey: false, IsAnyKeyExpansion: true, WindsorName: windsorName, Key: key, Template: default);

        public static KeyedEntry AnyKey(AnyKeyTemplate template)
            => new(IsAnyKey: true, IsAnyKeyExpansion: false, WindsorName: null, Key: null, Template: template);
    }
}