using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Resolvers.SpecializedResolvers;

namespace Castle.Windsor.MsDependencyInjection
{
    public class MsCompatibleCollectionResolver : CollectionResolver
    {
        public MsCompatibleCollectionResolver(IKernel kernel) 
            : base(kernel, allowEmptyCollections: true)
        {
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
    }
}
