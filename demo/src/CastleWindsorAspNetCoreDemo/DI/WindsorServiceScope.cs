using System;
using Castle.Windsor;
using Microsoft.Extensions.DependencyInjection;

namespace CastleWindsorAspNetCoreDemo.DI
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

            currentMsLifetimeScope.Children.Add(LifetimeScope);

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