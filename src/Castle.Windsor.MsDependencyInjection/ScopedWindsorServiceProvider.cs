using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceProvider"/>.
    /// </summary>
    public class ScopedWindsorServiceProvider : IServiceProvider, ISupportRequiredService
    {
        private readonly IWindsorContainer _container;
        private readonly MsLifetimeScope _ownMsLifetimeScope;

        public ScopedWindsorServiceProvider(IWindsorContainer container, MsLifetimeScopeProvider msLifetimeScopeProvider)
        {
            _container = container;
            _ownMsLifetimeScope = msLifetimeScopeProvider.LifetimeScope;
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
            using (MsLifetimeScope.Using(_ownMsLifetimeScope))
            {
                //Check if given service is directly registered
                if (_container.Kernel.HasComponent(serviceType))
                {
                    return _container.Resolve(serviceType);
                }

                // Check if requested IEnumerable<TService>
                // MS uses GetService<IEnumerable<TService>>() to get a collection.
                // This must be resolved with IWindsorContainer.ResolveAll();

                if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
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
        }
    }
}