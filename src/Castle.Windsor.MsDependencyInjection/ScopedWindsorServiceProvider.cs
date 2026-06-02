using System;
using System.Collections.Generic;
using System.Threading;
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

        protected IMsLifetimeScope OwnMsLifetimeScope { get; }

        // Mimic MS container behavior where they cache these collections.
        // Did it primarily for the parity unit tests to pass
        private Dictionary<KeyedServiceId, object> _anyKeyCollectionCache = new();

        public ScopedWindsorServiceProvider(IWindsorContainer container, MsLifetimeScopeProvider msLifetimeScopeProvider)
        {
            _container = container;
            _registry = container.Resolve<KeyedServiceRegistry>();
            OwnMsLifetimeScope = msLifetimeScopeProvider.LifetimeScope;
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
            using (MsLifetimeScope.Using(OwnMsLifetimeScope))
            {
                _isInResolvingCounter.Value++;

                try
                {
                    return ResolveInstanceOrNull(serviceType, isOptional, track: _isInResolvingCounter.Value == 1);
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
            if (serviceType == null) throw new ArgumentNullException(nameof(serviceType));
            if (serviceKey == null)
            {
                return GetServiceInternal(serviceType, isOptional);
            }

            if (serviceKey == KeyedService.AnyKey && !ServiceResolveHelper.IsEnumerable(serviceType))
            {
                throw new InvalidOperationException($"The KeyedService.AnyKey can be used to resolve all services only");
            }

            using (MsLifetimeScope.Using(OwnMsLifetimeScope))
            {
                _isInResolvingCounter.Value++;

                try
                {
                    return ResolveKeyedInstanceOrNull(new KeyedServiceId(serviceType, serviceKey), _isInResolvingCounter.Value == 1);
                }
                finally
                {
                    _isInResolvingCounter.Value--;
                }
            }
        }

        private object ResolveKeyedInstanceOrNull(KeyedServiceId serviceId, bool track)
        {
            // IEnumerable<T> keyed collection
            if (ServiceResolveHelper.IsEnumerable(serviceId.ServiceType))
            {
                lock (_anyKeyCollectionCache)
                {
                    if (_anyKeyCollectionCache.TryGetValue(serviceId, out var cached))
                    {
                        return cached;
                    }
                }

                var itemType = serviceId.ServiceType.GenericTypeArguments[0];
                var itemServiceId = new KeyedServiceId(itemType, serviceId.Key);
                var keys = _registry.ResolveAllWindsorKeysForService(itemServiceId);

                var result = ServiceResolveHelper.ResolveAllByName(_container, itemType, keys, track ? OwnMsLifetimeScope : null);

                // Use locking to not corrupt the collection.
                // It's okay if we resolve it multiple times in case of concurrency.
                lock (_anyKeyCollectionCache)
                {
                    _anyKeyCollectionCache[serviceId] = result;
                }

                return result;
            }

            var windsorKey = _registry.TryResolveWindsorKeyForService(serviceId);
            if (windsorKey != null)
            {
                return ServiceResolveHelper.ResolveByName(_container, windsorKey, serviceId.ServiceType, track ? OwnMsLifetimeScope : null);
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

            return _registry.HasExplicitOrAnyKey(new KeyedServiceId(serviceType, serviceKey));
        }
    }
}
