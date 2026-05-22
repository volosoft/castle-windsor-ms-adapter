#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

/// <summary>
/// Tracks the mapping between MS DI keyed service descriptors and the Castle Windsor
/// component names that fulfill them. AnyKey registrations are stored as <em>templates</em>
/// that are lazily expanded into real Windsor components on first resolution of a given actual key.
/// </summary>
internal sealed class KeyedServiceRegistry
{
    private const string KeyedNamePrefix = "MsKeyed_";

    private readonly object _sync = new();

    // All entries in registration order. Templates have IsAnyKey = true.
    // Lazy expansions are appended as IsAnyKeyExpansion = true entries as they happen.
    private readonly Dictionary<Type, List<KeyedEntry>> _byType = new();

    // Per-expansion Lazy guarantees the container.Register callback runs at most once per
    // (template, key) pair. The Lazy.Value call happens OUTSIDE _sync to avoid a lock-ordering
    // deadlock with Windsor (Windsor's own locks would otherwise be acquired while holding _sync,
    // while other threads can already hold Windsor's lock and need _sync via the sub-resolver).
    private readonly Dictionary<AnyKeyTemplateExpansionKey, Lazy<string>> _anyKeyExpansions = new();

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
        Lazy<string>? anyKeyExpansion = null;
        lock (_sync)
        {
            if (TryGetLastExplicitKeyName(serviceId, out var windsorName))
            {
                return windsorName;
            }

            if (TryGetLastAnyKeyTemplate(serviceId.ServiceType, out var template))
            {
                anyKeyExpansion = GetOrCreateAnyKeyExpansionLazy(template, serviceId.Key);
            }
        }

        // Force the registration outside _sync: the factory inside the Lazy reacquires _sync
        // briefly to publish the name->key mapping and the _byType entry, then invokes
        // container.Register without holding _sync. This breaks the _sync -> Windsor-lock
        // ordering that would otherwise risk a cross-lock deadlock with concurrent resolves.
        if (anyKeyExpansion != null)
        {
            return anyKeyExpansion.Value;
        }

        return null;
    }

    public IReadOnlyCollection<string> ResolveAllWindsorKeysForService(KeyedServiceId serviceId)
    {
        lock (_sync)
        {
            // Registration order matters - parity tests assert ordering. Use a list to
            // preserve order and a HashSet only as a seen-set (closed-generic + open-generic
            // lookups can otherwise enumerate the same expansion twice).
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

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
                        if (!entry.IsAnyKey && !entry.IsAnyKeyExpansion && seen.Add(entry.WindsorName!))
                        {
                            result.Add(entry.WindsorName!);
                        }
                    }
                    else
                    {
                        // AnyKey expansion entries surface in _byType with entry.Key == actualKey,
                        // but MS DI 10 keeps them out of GetKeyedServices(specific_key) - only
                        // explicit specific-key registrations count there.
                        if (!entry.IsAnyKeyExpansion
                            && Equals(entry.Key, serviceId.Key)
                            && seen.Add(entry.WindsorName!))
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
    /// Returns the Lazy that produces the Windsor name for this (template, actualKey) pair, creating
    /// it if needed. Caller must hold <see cref="_sync"/>. The Lazy.Value call must happen OUTSIDE
    /// the lock — see <see cref="TryResolveWindsorKeyForService"/>.
    /// </summary>
    private Lazy<string> GetOrCreateAnyKeyExpansionLazy(AnyKeyTemplate template, object? actualKey)
    {
        var expansionKey = new AnyKeyTemplateExpansionKey(template.Id, actualKey);
        if (_anyKeyExpansions.TryGetValue(expansionKey, out var existing))
        {
            return existing;
        }

        var lazy = new Lazy<string>(RegisterExpansion, LazyThreadSafetyMode.ExecutionAndPublication);
        _anyKeyExpansions[expansionKey] = lazy;
        return lazy;

        // <summary>
        // Runs OUTSIDE <see cref="_sync"/>. Order:
        // (1) Publish <c>_windsorNameToKey[name] = actualKey</c> under a short _sync section, so
        //     <c>[ServiceKey]</c> resolution during the registration call has the mapping ready.
        // (2) Invoke <c>container.Register</c> without holding _sync (avoids the
        //     _sync -> Windsor-lock ordering that would deadlock against another thread holding
        //     the Windsor lock and waiting on _sync via the sub-resolver).
        // (3) Publish the <c>_byType</c> entry AFTER the Register call returns, so concurrent
        //     <c>TryResolveWindsorKeyForService</c> calls for the same (type, key) cannot pick up
        //     the name from <c>_byType</c> and reach <c>container.Resolve(name)</c> before Windsor
        //     finishes registering. Other concurrent callers for the same (template, key) come in
        //     through the shared <see cref="Lazy{T}"/> and block until this method returns.
        // </summary>
        string RegisterExpansion()
        {
            var name = GenerateRandomWindsorName();

            // (1) Publish the name->key mapping BEFORE the Windsor component is registered. Windsor
            // evaluates constructor-bound sub-resolvers (including [ServiceKey] via
            // KeyedServicesSubResolver) during Register, and ServiceKeyInjectionParityTests breaks
            // if the mapping isn't visible at that point. Verified empirically: moving this to
            // post-register fails ServiceKey_Via_AnyKey_Gets_Actual_Key,
            // FromKeyedServices_Parameterless_Inherits_Parent_Key and three more.
            lock (_sync)
            {
                _windsorNameToKey[name] = actualKey;
            }

            // (2) Run container.Register OUTSIDE _sync to avoid a _sync -> Windsor-lock ordering
            //     that would deadlock against threads holding the Windsor lock and waiting on
            //     _sync via the sub-resolver.
            template.DoRegisterExpansion.Invoke(name, actualKey);

            // (3) Publish the _byType entry AFTER Register completes. Other threads asking
            //     TryResolveWindsorKeyForService for the same (type, key) would otherwise pick up
            //     the name from _byType and reach _container.Resolve(name) before Windsor finishes
            //     registering. Concurrent callers for the same (template, key) go through the shared
            //     Lazy and block until this method returns.
            lock (_sync)
            {
                GetOrAddByTypeList(template.ServiceType).Add(KeyedEntry.AnyKeyExpansion(name, actualKey));
            }

            return name;
        }
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
        AnyKeyTemplate? Template)
    {
        public static KeyedEntry ExplicitKey(string windsorName, object? key)
            => new(IsAnyKey: false, IsAnyKeyExpansion: false, WindsorName: windsorName, Key: key, Template: null);

        public static KeyedEntry AnyKeyExpansion(string windsorName, object? key)
            => new(IsAnyKey: false, IsAnyKeyExpansion: true, WindsorName: windsorName, Key: key, Template: null);

        public static KeyedEntry AnyKey(AnyKeyTemplate template)
            => new(IsAnyKey: true, IsAnyKeyExpansion: false, WindsorName: null, Key: null, Template: template);
    }
}