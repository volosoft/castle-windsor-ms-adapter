using Castle.Windsor;
using Microsoft.Extensions.DependencyInjection;

namespace CastleWindsorAspNetCoreDemo.DI
{
    /// <summary>
    /// Implements <see cref="IServiceScopeFactory"/>.
    /// </summary>
    public class WindsorServiceScopeFactory : IServiceScopeFactory
    {
        private readonly IWindsorContainer _container; 
        private readonly MsLifetimeScope _msLifetimeScope;

        public WindsorServiceScopeFactory(IWindsorContainer container, MsLifetimeScopeProvider msLifetimeScopeProvider)
        { 
            _container = container;
            _msLifetimeScope = msLifetimeScopeProvider.LifetimeScope;
        }

        public IServiceScope CreateScope()
        {
            return new WindsorServiceScope(_container, _msLifetimeScope);
        }
    }
}