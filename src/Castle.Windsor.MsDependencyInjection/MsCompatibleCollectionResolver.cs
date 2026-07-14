using System;
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
            var itemType = base.GetItemType(dependency.TargetItemType);

            // Shared with the [FromKeyedServices(null)] path; null means defer so EmptyCollectionResolving fires.
            var array = ServiceResolveHelper.ResolveNonKeyedCollectionInContext(kernel, _keyedServicesRegistry, itemType, context);
            return array ?? base.Resolve(context, contextHandlerResolver, model, dependency);
        }
    }
}
