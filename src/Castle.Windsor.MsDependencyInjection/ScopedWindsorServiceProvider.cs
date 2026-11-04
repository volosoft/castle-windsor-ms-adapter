using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Castle.Core;
using Castle.Windsor.MsDependencyInjection.Keyed;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceProvider"/>.
    /// </summary>
    public class ScopedWindsorServiceProvider :
        IServiceProvider,
        ISupportRequiredService,
        IServiceProviderIsService,
        IKeyedServiceProvider,
        IServiceProviderIsKeyedService
    {
        private static readonly AsyncLocal<int> _isInResolvingCounter = new AsyncLocal<int>();

        private readonly IWindsorContainer _container;

        private readonly KeyedServiceRegistry _registry;
        private readonly MsLifetimeScope _scopeOwner;
        private readonly bool _fromFactoryRoot;

        protected IMsLifetimeScope OwnMsLifetimeScope { get; }

        // MS DI caches collections only when all elements have a cached lifetime.
        private readonly Dictionary<KeyedServiceId, (object Value, string[] Names)> _anyKeyCollectionCache = new();

        public ScopedWindsorServiceProvider(IWindsorContainer container, MsLifetimeScopeProvider msLifetimeScopeProvider)
        {
            _container = container;
            _registry = container.Resolve<KeyedServiceRegistry>();
            OwnMsLifetimeScope = msLifetimeScopeProvider.LifetimeScope;
            _scopeOwner = MsScopeState.Resolve(container.Kernel, OwnMsLifetimeScope);
            _fromFactoryRoot = WindsorServiceScopeFactory.IsFactoryEntry(container);
        }

        private IDisposable UseScope()
        {
            var lifetime = MsLifetimeScope.Using(OwnMsLifetimeScope);
            var origin = WindsorServiceScopeFactory.UsingFactoryRoot(_fromFactoryRoot ? _container : null);
            return new DisposeAction(() =>
            {
                origin.Dispose();
                lifetime.Dispose();
            });
        }

        internal void ClearCache()
        {
            lock (_anyKeyCollectionCache)
            {
                _anyKeyCollectionCache.Clear();
            }
        }

        private void ThrowIfDisposed()
        {
            if (_scopeOwner?.IsDisposed != true)
            {
                return;
            }

            ClearCache();
            throw new ObjectDisposedException(nameof(ScopedWindsorServiceProvider));
        }

        public object GetService(Type serviceType)
        {
            return GetServiceInternal(serviceType, true);
        }

        public object GetRequiredService(Type serviceType)
        {
            return GetServiceInternal(serviceType, false);
        }

        private object GetServiceInternal(Type serviceType, bool isOptional)
        {
            ArgumentNullException.ThrowIfNull(serviceType);
            ThrowIfDisposed();
            var boundary = OwnMsLifetimeScope is not null and not GlobalMsLifetimeScope &&
                !ReferenceEquals(OwnMsLifetimeScope, MsLifetimeScope.Current);
            using (UseScope())
            {
                _isInResolvingCounter.Value++;

                try
                {
                    object Resolve() => ResolveInstanceOrNull(serviceType, isOptional, track: boundary || _isInResolvingCounter.Value == 1);
                    var result = NullFactoryResult.Unwrap(boundary ? ScopedResolution.Run(_container, Resolve) : Resolve());
                    if (!isOptional && result == null)
                    {
                        throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
                    }

                    return result;
                }
                finally
                {
                    _isInResolvingCounter.Value--;
                }
            }
        }

        private object ResolveInstanceOrNull(Type serviceType, bool isOptional, bool track)
        {
            // Check if given service is directly registered as a non-keyed component
            if (WindsorRegistrationHelper.RequiresBoxing(serviceType) && _registry.FindUnkeyedValue(serviceType) is { } valueName)
            {
                return ServiceResolveHelper.ResolveByName(_container, valueName, serviceType, track ? OwnMsLifetimeScope : null);
            }

            if (ServiceResolveHelper.HasNonKeyedComponent(_container, _registry, serviceType))
            {
                return ServiceResolveHelper.Resolve(_container, serviceType, track ? OwnMsLifetimeScope : null);
            }

            // Check if requested IEnumerable<TService>
            // MS uses GetService<IEnumerable<TService>>() to get a collection.
            // This must be resolved with IWindsorContainer.ResolveAll();
            if (ServiceResolveHelper.IsEnumerable(serviceType))
            {
                var itemType = serviceType.GenericTypeArguments[0];
                var keys = ServiceResolveHelper.GetNonKeyedHandlerNames(_container, _registry, itemType);

                return ServiceResolveHelper.ResolveAllByName(_container, itemType, keys, track ? OwnMsLifetimeScope : null);
            }

            if (isOptional)
            {
                // Not found
                return null;
            }

            // Match MS DI contract: GetRequiredService / ISupportRequiredService must throw
            // InvalidOperationException (not Castle's ComponentNotFoundException) when the
            // service is not registered.
            throw new InvalidOperationException($"No service for type '{serviceType}' has been registered.");
        }

        public object GetKeyedService(Type serviceType, object serviceKey)
        {
            return GetKeyedServiceInternal(serviceType, serviceKey, isOptional: true);
        }

        public object GetRequiredKeyedService(Type serviceType, object serviceKey)
        {
            var instance = GetKeyedServiceInternal(serviceType, serviceKey, isOptional: false);
            if (instance == null)
            {
                throw new InvalidOperationException($"No service for type '{serviceType}' has been registered with key '{serviceKey}'.");
            }
            return instance;
        }

        private object GetKeyedServiceInternal(Type serviceType, object serviceKey, bool isOptional)
        {
            if (serviceType == null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            ThrowIfDisposed();
            if (serviceKey == null)
            {
                return GetServiceInternal(serviceType, isOptional);
            }

            if (serviceKey == KeyedService.AnyKey && !ServiceResolveHelper.IsEnumerable(serviceType))
            {
                throw new InvalidOperationException($"The KeyedService.AnyKey can be used to resolve all services only");
            }

            var boundary = OwnMsLifetimeScope is not null and not GlobalMsLifetimeScope &&
                !ReferenceEquals(OwnMsLifetimeScope, MsLifetimeScope.Current);
            using (UseScope())
            {
                _isInResolvingCounter.Value++;

                try
                {
                    object Resolve() => ResolveKeyedInstanceOrNull(new KeyedServiceId(serviceType, serviceKey), boundary || _isInResolvingCounter.Value == 1);
                    return NullFactoryResult.Unwrap(boundary ? ScopedResolution.Run(_container, Resolve) : Resolve());
                }
                finally
                {
                    _isInResolvingCounter.Value--;
                }
            }
        }

        private object ResolveKeyedInstanceOrNull(KeyedServiceId serviceId, bool track)
        {
            var selectedName = _registry.TryResolveWindsorKeyForService(serviceId);
            if (selectedName != null)
            {
                return ServiceResolveHelper.ResolveKeyedByName(_container, selectedName, serviceId, track ? OwnMsLifetimeScope : null);
            }
            // IEnumerable<T> keyed collection
            if (ServiceResolveHelper.IsEnumerable(serviceId.ServiceType))
            {
                var itemType = serviceId.ServiceType.GenericTypeArguments[0];
                var itemServiceId = new KeyedServiceId(itemType, serviceId.Key);
                var keys = _registry.ResolveAllWindsorKeysForService(itemServiceId);
                var closedScopedCache = _scopeOwner?.WindsorLifeTimeScope is MsScopeCache { IsDisposed: true } &&
                    keys.Any(name => typeof(MsScopedLifestyleManager).IsAssignableFrom(_container.Kernel.GetHandler(name).ComponentModel.CustomLifestyle));
                lock (_anyKeyCollectionCache)
                {
                    if (closedScopedCache)
                    {
                        _anyKeyCollectionCache.Remove(serviceId);
                    }
                    else if (_anyKeyCollectionCache.TryGetValue(serviceId, out var cached))
                    {
                        if (cached.Names.SequenceEqual(keys))
                        {
                            return cached.Value;
                        }

                        _anyKeyCollectionCache.Remove(serviceId);
                    }
                }

                // Use the existing Windsor lifetime metadata, including open-generic registrations.
                // Caching a collection containing transients would turn them into scoped instances.
                var cacheCollection = keys.All(key =>
                {
                    var model = _container.Kernel.GetHandler(key).ComponentModel;
                    return (model.LifestyleType == LifestyleType.Singleton || model.CustomLifestyle == typeof(MsScopedLifestyleManager)
                        || model.CustomLifestyle == typeof(MsSingletonLifestyleManager))
                        && model.ExtendedProperties["instance"] == null;
                });

                var result = ServiceResolveHelper.ResolveAllByName(_container, itemType, keys, track ? OwnMsLifetimeScope : null,
                    registry: _registry, key: serviceId.Key);

                if (cacheCollection)
                {
                    // Use locking to not corrupt the collection.
                    // It's okay if we resolve it multiple times in case of concurrency.
                    lock (_anyKeyCollectionCache)
                    {
                        if (_anyKeyCollectionCache.TryGetValue(serviceId, out var published) && published.Names.SequenceEqual(keys))
                        {
                            return published.Value;
                        }

                        _anyKeyCollectionCache[serviceId] = (result, keys.ToArray());
                    }
                }

                return result;
            }

            return null;
        }

        public bool IsService(Type serviceType)
        {
            if (serviceType is null)
            {
                throw new ArgumentNullException(nameof(serviceType));
            }

            if (serviceType.IsGenericTypeDefinition)
            {
                return false;
            }

            if (ServiceResolveHelper.HasNonKeyedComponent(_container, _registry, serviceType))
            {
                return true;
            }

            // We special case IEnumerable since it isn't explicitly registered in the container
            // yet we can manifest instances of it when requested.
            if (ServiceResolveHelper.IsEnumerable(serviceType))
            {
                return true;
            }

            return serviceType == typeof(IServiceProvider) ||
                   serviceType == typeof(IServiceScopeFactory) ||
                   serviceType == typeof(IServiceProviderIsService) ||
                   serviceType == typeof(IKeyedServiceProvider) ||
                   serviceType == typeof(IServiceProviderIsKeyedService);
        }

        public bool IsKeyedService(Type serviceType, object serviceKey)
        {
            ArgumentNullException.ThrowIfNull(serviceType);

            if (serviceKey == null)
            {
                return IsService(serviceType);
            }

            if (serviceType.IsGenericTypeDefinition)
            {
                return false;
            }

            // MS DI: closed-generic IEnumerable<T> always reports as a keyed service.
            if (ServiceResolveHelper.IsEnumerable(serviceType))
            {
                return true;
            }

            return serviceType == typeof(IServiceProvider) || serviceType == typeof(IServiceScopeFactory) ||
                serviceType == typeof(IServiceProviderIsService) || serviceType == typeof(IServiceProviderIsKeyedService) ||
                _registry.HasExplicitOrAnyKey(new KeyedServiceId(serviceType, serviceKey));
        }
    }
}
