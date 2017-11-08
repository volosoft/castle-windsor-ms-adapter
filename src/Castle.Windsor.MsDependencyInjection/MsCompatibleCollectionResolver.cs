using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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

        public override object Resolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model,
            DependencyModel dependency)
        {
            var items = base.Resolve(context, contextHandlerResolver, model, dependency);
            // Services in container are registered in backward order (using .IsDefault() - see WindsorRegistrationHelper.RegisterServiceDescriptor).
            // However we need to return them in original order when returning collection so let's reverse them here.
            // Following check is probably unnecessary but let's be defensive and don't expect that it's always array what we get.
            if (items is Array)
            {
                Array.Reverse((Array) items);
            }
            return items;
        }
    }
}
