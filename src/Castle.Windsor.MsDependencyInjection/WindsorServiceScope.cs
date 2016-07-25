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

        private readonly MsLifetimeScope _parentLifetimeScope;

        public WindsorServiceScope(IWindsorContainer container, MsLifetimeScope currentMsLifetimeScope)
        {
            _parentLifetimeScope = currentMsLifetimeScope;

            LifetimeScope = new MsLifetimeScope();

            _parentLifetimeScope?.AddChild(LifetimeScope);

            using (MsLifetimeScope.Using(LifetimeScope))
            {
                ServiceProvider = container.Resolve<IServiceProvider>();
            }
        }
         
        public void Dispose()
        {
            _parentLifetimeScope?.RemoveChild(LifetimeScope);
            LifetimeScope.Dispose();
        }
    }
}