using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceProvider"/>.
    /// </summary>
    public class ScopedWindsorServiceProvider : IServiceProvider, ISupportRequiredService, IServiceProviderIsService
    {
        private readonly IWindsorContainer _container;
        protected IMsLifetimeScope OwnMsLifetimeScope { get; }

        public static bool IsInResolving
        {
            get => _isInResolving.Value;
            set => _isInResolving.Value = value;
        }

        private static readonly AsyncLocal<bool> _isInResolving = new AsyncLocal<bool>();

        public ScopedWindsorServiceProvider(IWindsorContainer container, MsLifetimeScopeProvider msLifetimeScopeProvider)
        {
            _container = container;
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
                var isAlreadyInResolving = IsInResolving;

                if (!isAlreadyInResolving)
                {
                    IsInResolving = true;
                }

                object instance = null;
                try
                {
                    return instance = ResolveInstanceOrNull(serviceType, isOptional);
                }
                finally
                {
                    if (!isAlreadyInResolving)
                    {
                        if (instance != null)
                        {
                            OwnMsLifetimeScope?.AddInstance(instance);
                        }

                        IsInResolving = false;
                    }
                }
            }
        }

        private object ResolveInstanceOrNull(Type serviceType, bool isOptional)
        {
            //Check if given service is directly registered
            if (_container.Kernel.HasComponent(serviceType))
            {
                return _container.Resolve(serviceType);
            }

            // Check if requested IEnumerable<TService>
            // MS uses GetService<IEnumerable<TService>>() to get a collection.
            // This must be resolved with IWindsorContainer.ResolveAll();

            if (serviceType.GetTypeInfo().IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
            {
                var allObjects = _container.ResolveAll(serviceType.GenericTypeArguments[0]);
                Array.Reverse(allObjects);
                return allObjects;
            }

            if (isOptional)
            {
                //Not found
                return null;
            }

            //Let Castle Windsor throws exception since the service is not registered!
            return _container.Resolve(serviceType);
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

            if (_container.Kernel.HasComponent(serviceType))
            {
                return true;
            }

            if (serviceType.IsConstructedGenericType &&
                serviceType.GetGenericTypeDefinition() is Type genericDefinition)
            {
                // We special case IEnumerable since it isn't explicitly registered in the container
                // yet we can manifest instances of it when requested.
                return genericDefinition == typeof(IEnumerable<>) || _container.Kernel.HasComponent(genericDefinition);
            }

            return serviceType == typeof(IServiceProvider) ||
                   serviceType == typeof(IServiceScopeFactory) ||
                   serviceType == typeof(IServiceProviderIsService);
        }
    }
}