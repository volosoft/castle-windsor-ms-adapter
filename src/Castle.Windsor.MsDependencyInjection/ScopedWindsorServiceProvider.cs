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
    public class ScopedWindsorServiceProvider : IServiceProvider, ISupportRequiredService
    {
        private readonly IWindsorContainer _container;
        private readonly IMsLifetimeScope _ownMsLifetimeScope;


#if NET452
        public static bool IsInResolving
        {
            get { return _isInResolving; }
            set { _isInResolving = value; }
        }

        [ThreadStatic]
        private static bool _isInResolving;
#else
        public static bool IsInResolving
        {
            get { return _current.Value; }
            set { _current.Value = value; }
        }

        private static readonly AsyncLocal<bool> _current = new AsyncLocal<bool>();
#endif

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
                            _ownMsLifetimeScope?.AddInstance(instance);
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
    }
}