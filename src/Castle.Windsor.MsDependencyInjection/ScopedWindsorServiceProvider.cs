using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
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
            if (HasNonKeyedComponent(serviceType))
            {
                return ContainerResolve(serviceType, track);
            }

            // Check if requested IEnumerable<TService>
            // MS uses GetService<IEnumerable<TService>>() to get a collection.
            // This must be resolved with IWindsorContainer.ResolveAll();
            if (IsEnumerable(serviceType))
            {
                var itemType = serviceType.GenericTypeArguments[0];
                var keys = _container.Kernel.GetAssignableHandlers(itemType)
                    .Select(x => x.ComponentModel.Name)
                    .Where(x => !_registry.IsKeyedService(x));

                return ContainerResolveAll(keys, itemType, track);
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

            if (serviceKey == KeyedService.AnyKey && !IsEnumerable(serviceType))
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
            if (IsEnumerable(serviceId.ServiceType))
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

                var result = ContainerResolveAll(keys, itemType, track);

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
                return ContainerResolve(windsorKey, serviceId.ServiceType, track);
            }

            return null;
        }

        private bool HasNonKeyedComponent(Type serviceType)
        {
            if (!_container.Kernel.HasComponent(serviceType))
            {
                return false;
            }

            var handlers = _container.Kernel.GetHandlers(serviceType);
            foreach (var h in handlers)
            {
                if (!_registry.IsKeyedService(h.ComponentModel.Name))
                {
                    return true;
                }
            }

            // Also check open-generic handlers if the type is constructed-generic.
            if (serviceType.IsConstructedGenericType)
            {
                var openHandlers = _container.Kernel.GetHandlers(serviceType.GetGenericTypeDefinition());
                foreach (var h in openHandlers)
                {
                    if (!_registry.IsKeyedService(h.ComponentModel.Name))
                    {
                        return true;
                    }
                }
            }

            return false;
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

            if (HasNonKeyedComponent(serviceType))
            {
                return true;
            }

            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() is { } genericDefinition)
            {
                // We special case IEnumerable since it isn't explicitly registered in the container
                // yet we can manifest instances of it when requested.
                if (genericDefinition == typeof(IEnumerable<>))
                {
                    return true;
                }

                if (HasNonKeyedComponent(genericDefinition))
                {
                    return true;
                }
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
            if (IsEnumerable(serviceType))
            {
                return true;
            }

            return _registry.HasExplicitOrAnyKey(new KeyedServiceId(serviceType, serviceKey));
        }

        private object ContainerResolve(Type serviceType, bool track)
        {
            // Let Castle Windsor throw ComponentNotFoundException when the service is not
            // registered. Callers that want a null-returning behavior (GetService) gate this
            // method with HasNonKeyedComponent first; callers that require throwing
            // (GetRequiredService / ISupportRequiredService) rely on the throw to honor the
            // MS DI contract.
            var instance = _container.Resolve(serviceType);
            if (track)
            {
                OwnMsLifetimeScope?.AddInstance(instance);
            }

            return instance;
        }

        private object ContainerResolve(string key, Type serviceType, bool track)
        {
            var instance = _container.Resolve(key, serviceType);
            if (track)
            {
                OwnMsLifetimeScope?.AddInstance(instance);
            }

            return instance;
        }

        private object ContainerResolveAll(IEnumerable<string> keys, Type itemType, bool track)
        {
            var instances = new List<object>();
            foreach (var key in keys)
            {
                try
                {
                    instances.Add(ContainerResolve(key, itemType, track));
                }
                catch (Castle.MicroKernel.Handlers.GenericHandlerTypeMismatchException)
                {
                    // Open-generic handler whose constraints can't satisfy this closed type.
                    // ResolveAll silently skips these; mirror that behavior.
                }
            }
            var array = Array.CreateInstance(itemType, instances.Count);
            ((ICollection)instances).CopyTo(array, 0);
            return array;
        }

        private static bool IsEnumerable(Type serviceType)
        {
            return serviceType.IsConstructedGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>);
        }
    }
}
