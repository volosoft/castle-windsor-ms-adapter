using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class WindsorSpecificationTests : DependencyInjectionSpecificationTests, IDisposable
    {
        private readonly IWindsorContainer _container; 

        public WindsorSpecificationTests()
        {
            _container = new WindsorContainer();
        }

        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            return WindsorRegistrationHelper.CreateServiceProvider(
                _container,
                serviceCollection
                );
        }
        
        public void Dispose()
        {
            Assert.Null(MsLifetimeScope.Current);
        }
    }
}