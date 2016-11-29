using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Microsoft.Extensions.Options;

namespace Castle.Windsor.MsDependencyInjection
{
    public class WindsorOptionsSubResolver : ISubDependencyResolver
    {
        private readonly IKernel _kernel;

        public WindsorOptionsSubResolver(IKernel kernel)
        {
            _kernel = kernel;
        }

        public bool CanResolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
        {
            return dependency.TargetType.IsGenericType && dependency.TargetType.GetGenericTypeDefinition() == typeof(IOptions<>);
        }

        public object Resolve(CreationContext context, ISubDependencyResolver contextHandlerResolver, ComponentModel model, DependencyModel dependency)
        {
            return _kernel.Resolve(dependency.TargetType);
        }
    }
}