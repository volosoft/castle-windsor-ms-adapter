using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IScopedWindsorServiceProvider"/> (and <see cref="IServiceProvider"/>) to get service
    /// from <see cref="IWindsorContainer"/> in a scoped manner.
    /// </summary>
    public class ScopedWindsorServiceProvider : IScopedWindsorServiceProvider
    {
        /// <summary>
        /// Reference to the current service provider that can be usable
        /// in a service resolution.
        /// </summary>
        [ThreadStatic]
        public static IScopedWindsorServiceProvider Current;

        public IWindsorServiceScope Scope { get { return _scope; } }

        private readonly IWindsorContainer _container;
        private readonly IWindsorServiceScope _scope;
        
        public ScopedWindsorServiceProvider(IWindsorContainer container, IWindsorServiceScope scope)
        {
            _container = container;
            _scope = scope;
        }

        public object GetService(Type serviceType)
        {
            if (serviceType == typeof(IServiceProvider) ||
                serviceType == typeof(IScopedWindsorServiceProvider))
            {
                return this;
            }

            if (serviceType == typeof(IServiceScope) ||
                serviceType == typeof(IWindsorServiceScope))
            {
                return _scope;
            }


            var previous = Current;
            Current = this;

            try
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
            }
            finally
            {
                Current = previous;
            }

            //Not found
            return null;
        }
    }
}