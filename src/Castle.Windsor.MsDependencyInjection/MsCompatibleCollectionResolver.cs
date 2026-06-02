using System;
using System.Collections;
using System.Collections.Generic;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Resolvers.SpecializedResolvers;
using Castle.Windsor.MsDependencyInjection.Keyed;

namespace Castle.Windsor.MsDependencyInjection
{
    public class MsCompatibleCollectionResolver : CollectionResolver
    {
        private readonly KeyedServiceRegistry _keyedServicesRegistry;

        public MsCompatibleCollectionResolver(IKernel kernel)
            : base(kernel, allowEmptyCollections: true)
        {
            if (!kernel.HasComponent(typeof(KeyedServiceRegistry)))
            {
                throw new InvalidOperationException($"Kernel must have {typeof(KeyedServiceRegistry).FullName} registered.");
            }

            _keyedServicesRegistry = kernel.Resolve<KeyedServiceRegistry>();
        }

        public override bool CanResolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model,
            DependencyModel dependency)
        {
            if (kernel.HasComponent(dependency.TargetItemType))
            {
                return false;
            }

            return base.CanResolve(context, contextHandlerResolver, model, dependency);
        }

        public override object Resolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model,
            DependencyModel dependency)
        {
            // Filter keyed components out of the non-keyed collection: build the array
            // from the non-keyed handlers ourselves so we never resolve a keyed component
            // as part of an IEnumerable<T> non-keyed request.
            // That is a part of spec for keyed services isolation.
            var itemType = base.GetItemType(dependency.TargetItemType);
            var handlers = kernel.GetAssignableHandlers(itemType);

            var instances = new List<object>(handlers.Length);
            foreach (var handler in handlers)
            {
                if (_keyedServicesRegistry.IsKeyedService(handler.ComponentModel.Name))
                {
                    continue;
                }

                instances.Add(kernel.Resolve(handler.ComponentModel.Name, itemType));
            }

            var array = Array.CreateInstance(itemType, instances.Count);
            ((ICollection)instances).CopyTo(array, 0);
            return array;
        }
    }
}
