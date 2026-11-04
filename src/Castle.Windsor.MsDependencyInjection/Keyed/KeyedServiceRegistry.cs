#nullable enable

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Keyed;

internal sealed class KeyedServiceRegistry
{
    private readonly IKernel _kernel;

    public KeyedServiceRegistry(IKernel kernel) => _kernel = kernel;

    private KeyedServiceRegistry? Parent => _kernel.Parent?.HasComponent(typeof(KeyedServiceRegistry)) == true
        ? _kernel.Parent.Resolve<KeyedServiceRegistry>() : null;

    [ThreadStatic]
    private static Lookup? _lookup;

    internal sealed record Lookup(string Name, object? Key);

    private readonly object _sync = new();
    private readonly Dictionary<Type, List<Registration>> _registrations = new();
    private readonly Dictionary<KeyedServiceId, Registration> _explicit = new();
    private readonly Dictionary<Type, Registration> _templates = new();
    private readonly Dictionary<(Registration Template, object? Key), Lazy<string>> _expansions = new();
    private readonly Dictionary<string, object?> _keysByName = new(StringComparer.Ordinal);
    private int _nextOrder;

    public string RegisterExplicitKeyService(KeyedServiceId id)
    {
        lock (_sync)
        {
            var name = CreateName();
            var registration = new Registration(id.ServiceType, id.Key, name, _nextOrder++, null);
            Add(registration);
            _explicit[id] = registration;
            _keysByName.Add(name, id.Key);
            return name;
        }
    }

    public void RegisterAnyKeyService(KeyedServiceId id, Action<string, object?> registerExpansion)
    {
        lock (_sync)
        {
            var registration = new Registration(id.ServiceType, id.Key, null, _nextOrder++, registerExpansion);
            Add(registration);
            _templates[id.ServiceType] = registration;
        }
    }

    public string? TryResolveWindsorKeyForService(KeyedServiceId id)
    {
        Lazy<string>? expansion = null;
        lock (_sync)
        {
            var registration = Find(id);
            if (registration?.Name != null)
            {
                return registration.Name;
            }

            if (registration != null)
            {
                var expansionId = (registration, id.Key);
                if (!_expansions.TryGetValue(expansionId, out expansion!))
                {
                    expansion = new Lazy<string>(() => Expand(registration, id.Key), LazyThreadSafetyMode.ExecutionAndPublication);
                    _expansions.Add(expansionId, expansion);
                }
            }
        }
        // Registration enters Windsor and must not run under the registry lock.
        return expansion != null ? expansion.Value : Parent?.TryResolveWindsorKeyForService(id);
    }

    public IReadOnlyCollection<string> ResolveAllWindsorKeysForService(KeyedServiceId id)
    {
        string[] names;
        lock (_sync)
        {
            var registrations = ForType(id.ServiceType);
            if (id.ServiceType.IsConstructedGenericType && id.Key != KeyedService.AnyKey)
            {
                registrations = registrations.Concat(ForType(id.ServiceType.GetGenericTypeDefinition()));
            }

            // Expansions are derived lookup state, never collection registrations.
            names = registrations.Where(entry => entry.Name != null && entry.Key != null &&
                    (id.Key == KeyedService.AnyKey || Equals(entry.Key, id.Key)))
                .OrderBy(entry => entry.Order)
                .Select(entry => entry.Name!)
                .ToArray();
        }
        return (Parent?.ResolveAllWindsorKeysForService(id) ?? Array.Empty<string>()).Concat(names).ToArray();
    }

    public bool HasExplicitOrAnyKey(KeyedServiceId id)
    {
        lock (_sync)
        {
            if (Find(id) != null)
            {
                return true;
            }
        }

        return Parent?.HasExplicitOrAnyKey(id) == true;
    }

    public bool IsKeyedService(string? name)
    {
        if (name == null)
        {
            return false;
        }

        lock (_sync)
        {
            if (_keysByName.TryGetValue(name, out var key))
            {
                return key != null;
            }
        }

        return Parent?.IsKeyedService(name) == true;
    }

    public bool TryGetServiceKeyByWindsorName(string? name, [NotNullWhen(true)] out object? key)
    {
        lock (_sync)
        {
            if (name != null && _keysByName.TryGetValue(name, out key))
            {
                return key != null;
            }
        }
        if (Parent is { } parent)
        {
            return parent.TryGetServiceKeyByWindsorName(name, out key);
        }

        key = null;
        return false;
    }

    internal string? FindUnkeyedValue(Type serviceType)
    {
        lock (_sync)
        {
            if (_explicit.GetValueOrDefault(new KeyedServiceId(serviceType, null))?.Name is { } name)
            {
                return name;
            }
        }

        return Parent?.FindUnkeyedValue(serviceType);
    }

    internal string[] FindUnkeyedValueNames(Type serviceType)
    {
        lock (_sync)
        {
            return ForType(serviceType).Where(entry => entry.Key == null && entry.Name != null)
            .OrderByDescending(entry => entry.Order).Select(entry => entry.Name!).ToArray();
        }
    }

    public bool TryGetLookupKey(CreationContext context, string name, [NotNullWhen(true)] out object? key)
    {
        if (_lookup is Lookup lookup && lookup.Name == name)
        {
            key = lookup.Key;
            return key != null;
        }
        return TryGetServiceKeyByWindsorName(name, out key);
    }

    internal static IDisposable UseLookup(string name, object? key)
    {
        var previous = _lookup;
        _lookup = new Lookup(name, key);
        return new DisposeAction(() => _lookup = previous);
    }

    private Registration? Find(KeyedServiceId id)
    {
        var registration = Find(id.ServiceType, id.Key);
        return registration ?? (id.ServiceType.IsConstructedGenericType
            ? Find(id.ServiceType.GetGenericTypeDefinition(), id.Key)
            : null);
    }

    private Registration? Find(Type serviceType, object? key)
    {
        return _explicit.TryGetValue(new KeyedServiceId(serviceType, key), out var registration)
            ? registration
            : _templates.GetValueOrDefault(serviceType);
    }

    private IEnumerable<Registration> ForType(Type serviceType) =>
        _registrations.TryGetValue(serviceType, out var entries) ? entries : Enumerable.Empty<Registration>();

    private void Add(Registration registration)
    {
        if (!_registrations.TryGetValue(registration.ServiceType, out var entries))
        {
            _registrations.Add(registration.ServiceType, entries = new List<Registration>());
        }

        entries.Add(registration);
    }

    private string Expand(Registration registration, object? key)
    {
        var name = CreateName();
        // Constructor metadata is consulted during registration, before it completes.
        lock (_sync)
        {
            _keysByName.Add(name, key);
        }

        registration.RegisterExpansion!(name, key);
        return name;
    }

    private static string CreateName() => "MsKeyed_" + Guid.NewGuid().ToString("N");

    private sealed record Registration(Type ServiceType, object? Key, string? Name, int Order,
        Action<string, object?>? RegisterExpansion);
}
