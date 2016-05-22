using System;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceScope"/>.
    /// </summary> 
    public class WindsorServiceScope : IServiceScope
    {
        public IServiceProvider ServiceProvider { get; private set; }

        public MsLifetimeScope LifetimeScope { get; private set; }

        public WindsorServiceScope(IWindsorContainer container, MsLifetimeScope currentMsLifetimeScope)
        {
            LifetimeScope = new MsLifetimeScope();
            
            using (MsLifetimeScope.Using(LifetimeScope))
            {
                ServiceProvider = container.Resolve<IServiceProvider>();
            }
        }
         
        public void Dispose()
        {
            LifetimeScope.Dispose();
        }
    }
}