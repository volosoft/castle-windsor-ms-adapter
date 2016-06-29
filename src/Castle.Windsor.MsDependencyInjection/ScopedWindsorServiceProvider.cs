using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
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
                // MS uses GetService<IEnumerable<TDesiredType>>() to get a collection.
                // This must be resolved with IWindsorContainer.ResolveAll();

                if (serviceType.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    var allObjects = _container.ResolveAll(serviceType.GenericTypeArguments[0]);
                    Array.Reverse(allObjects);
                    return allObjects;
                }

                if (!isOptional)
                {
                    return _container.Resolve(serviceType);
                }

                // A single service was requested.

                // Microsoft.Extensions.DependencyInjection is built to handle optional registrations.
                // However Castle Windsor throws a ComponentNotFoundException when a type wasn't registered.
                // For this reason we have to manually check if the type exists in Windsor.

                if (_container.Kernel.HasComponent(serviceType))
                {
                    return _container.Resolve(serviceType);
                }

                //Not found
                return null;
            }
        }
    }
}