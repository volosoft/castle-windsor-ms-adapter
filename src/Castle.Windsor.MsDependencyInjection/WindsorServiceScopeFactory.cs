using Castle.MicroKernel;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceScopeFactory"/> 
    /// to create <see cref="IWindsorServiceScope"/> objects as <see cref="IServiceScope"/>.
    /// </summary>
    public class WindsorServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IKernel _kernel;

        public WindsorServiceScopeFactory(IKernel kernel)
        {
            _kernel = kernel;
        }

        public IServiceScope CreateScope()
        {
            return _kernel.Resolve<IWindsorServiceScope>();
        }
    }
}