using System.Reflection;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.Windsor.MsDependencyInjection.Keyed;
using Microsoft.Extensions.Options;

namespace Castle.Windsor.MsDependencyInjection
{
    public class MsOptionsSubResolver : ISubDependencyResolver
    {
        private readonly IKernel _kernel;

        public MsOptionsSubResolver(IKernel kernel)
        {
            _kernel = kernel;
        }

        public bool CanResolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
        {
            return dependency.Parameter?.Value != KeyedConstructorDependencyContributor.Reference &&
                dependency.TargetType != null && dependency.TargetType.GetTypeInfo().IsGenericType && dependency.TargetType.GetGenericTypeDefinition() == typeof(IOptions<>);
        }

        public object Resolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
        {
            return _kernel.Resolve(dependency.TargetType);
        }
    }
}
