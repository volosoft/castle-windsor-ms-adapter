using System;
using System.Collections.Generic;
using System.Reflection;
using Castle.Windsor;

namespace CastleWindsorAspNetCoreDemo.DI
{
    /// <summary>
    /// Implements <see cref="IServiceProvider"/>.
    /// </summary>
    public class ScopedWindsorServiceProvider : IServiceProvider
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
            using (MsLifetimeScope.Using(_ownMsLifetimeScope))
            {
                // MS uses GetService<IEnumerable<TDesiredType>>() to get a collection.
                // This must be resolved with IWindsorContainer.ResolveAll();
                var typeInfo = serviceType.GetTypeInfo();
                if (typeInfo.IsGenericType && serviceType.GetGenericTypeDefinition() == typeof(IEnumerable<>))
                {
                    return _container.ResolveAll(typeInfo.GenericTypeArguments[0]);
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