using System;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceScope"/>.
    /// </summary> 
    public class WindsorServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; }

        public MsLifetimeScope LifetimeScope { get; }

        private readonly IMsLifetimeScope _parentLifetimeScope;

        private readonly IDisposable _msLifetimeScopeDisposable;

        public WindsorServiceScope(IWindsorContainer container, IMsLifetimeScope currentMsLifetimeScope)
        {
            _parentLifetimeScope = currentMsLifetimeScope;

            LifetimeScope = new MsLifetimeScope(container);

            _parentLifetimeScope?.AddChild(LifetimeScope);

            _msLifetimeScopeDisposable = MsLifetimeScope.Using(LifetimeScope);
            ServiceProvider = container.Resolve<IServiceProvider>();
        }
         
        public void Dispose()
        {
            _parentLifetimeScope?.RemoveChild(LifetimeScope);
            _msLifetimeScopeDisposable?.Dispose();
            LifetimeScope.Dispose();
        }
    }
}