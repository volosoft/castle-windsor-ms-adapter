using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class FactoryScopeOwnershipTests
    {
        [Theory]
        [InlineData(false, "direct")]
        [InlineData(false, "constructor")]
        [InlineData(false, "keyed")]
        [InlineData(true, "direct")]
        [InlineData(true, "constructor")]
        [InlineData(true, "keyed")]
        public void Root_Scope_Ownership_Preserves_The_Creation_Entry(bool factoryEntry, string entry)
        {
            var services = new ServiceCollection();
            services.AddScoped<Resource>();
            services.AddTransient<Owner>();
            services.AddKeyedTransient<Owner>("owner");
            var factory = new WindsorServiceProviderFactory();
            using var container = factoryEntry ? factory.CreateBuilder(services) : new WindsorContainer();
            var root = factoryEntry ? factory.CreateServiceProvider(container)
                : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = entry switch
            {
                "constructor" => root.GetRequiredService<Owner>().Factory.CreateScope(),
                "keyed" => root.GetRequiredKeyedService<Owner>("owner").Factory.CreateScope(),
                _ => root.CreateScope()
            };
            var resource = scope.ServiceProvider.GetRequiredService<Resource>();
            ((IDisposable)root).Dispose();
            Assert.Equal(factoryEntry ? 0 : 1, resource.Disposals);
            scope.Dispose();
            Assert.Equal(1, resource.Disposals);
        }

        public sealed class Owner
        {
            public IServiceScopeFactory Factory { get; }

            public Owner(IServiceScopeFactory factory)
            {
                Factory = factory;
            }
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Disposals++;
            }
        }
    }
}
