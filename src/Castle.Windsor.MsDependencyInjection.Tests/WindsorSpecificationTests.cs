using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class WindsorSpecificationTests : DependencyInjectionSpecificationTests
    {
        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            return WindsorRegistrationHelper.CreateServiceProvider(
                new WindsorContainer(),
                serviceCollection
                );
        }
    }
}