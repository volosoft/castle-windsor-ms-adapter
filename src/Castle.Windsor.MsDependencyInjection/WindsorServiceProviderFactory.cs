using System;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    public class WindsorServiceProviderFactory : IServiceProviderFactory<IWindsorContainer>
    {
        private readonly bool _validateScopes;
        public WindsorServiceProviderFactory() : this(false) { }
        public WindsorServiceProviderFactory(bool validateScopes) => _validateScopes = validateScopes;
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
            using var origin = WindsorServiceScopeFactory.UsingFactoryRoot(containerBuilder);
            var root = containerBuilder.Resolve<GlobalMsLifetimeScope>();
            root.ValidateScopes = _validateScopes;
            using (MsLifetimeScope.Using(root))
            {
                return containerBuilder.Kernel.GetHandler(typeof(IServiceProvider)).ComponentModel.Implementation == typeof(ScopedWindsorServiceProvider)
                    ? containerBuilder.Resolve<GlobalScopedWindsorServiceProvider>()
                    : containerBuilder.Resolve<IServiceProvider>();
            }
        }
    }
}
