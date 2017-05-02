using System;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    public class WindsorServiceProviderFactory : IServiceProviderFactory<IWindsorContainer>
    {
        public IWindsorContainer CreateBuilder(IServiceCollection services)
        {
            var container = services.GetSingletonServiceOrNull<IWindsorContainer>();

            if (container == null)
            {
                container = new WindsorContainer();
                services.AddSingleton(container);
            }

            container.AddServices(services);

            return container;
        }

        public IServiceProvider CreateServiceProvider(IWindsorContainer containerBuilder)
        {
            return containerBuilder.Resolve<IServiceProvider>();
        }
    }
}
