using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class ScopeValidationTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Validation_Is_Optional_And_Applies_To_Both_Root_Entries(bool factory, bool validate)
    {
        var services = new ServiceCollection();
        services.AddScoped<Resource>();
        services.AddKeyedScoped<Resource>("k");
        using var container = new WindsorContainer();
        IServiceProvider provider;
        if (factory)
        {
            services.AddSingleton<IWindsorContainer>(container);
            var builder = new WindsorServiceProviderFactory(validate);
            provider = builder.CreateServiceProvider(builder.CreateBuilder(services));
        }
        else provider = WindsorRegistrationHelper.CreateServiceProvider(container, services, validate);

        if (validate)
        {
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<Resource>());
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<Resource>("k"));
        }
        else
        {
            Assert.NotNull(provider.GetRequiredService<Resource>());
            Assert.NotNull(provider.GetRequiredKeyedService<Resource>("k"));
        }
        using (var scope = provider.CreateScope())
        {
            Assert.NotNull(scope.ServiceProvider.GetRequiredService<Resource>());
            Assert.NotNull(scope.ServiceProvider.GetRequiredKeyedService<Resource>("k"));
        }
        ((IDisposable)provider).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Singleton_Cannot_Capture_Scoped_Service_From_A_Child(bool factory)
    {
        var services = new ServiceCollection();
        services.AddScoped<Resource>();
        if (factory) services.AddSingleton(provider => new Owner(provider.GetRequiredService<Resource>()));
        else services.AddSingleton<Owner>();
        using var container = new WindsorContainer();
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services, validateScopes: true);
        using (var scope = provider.CreateScope())
            Assert.Throws<InvalidOperationException>(() => scope.ServiceProvider.GetRequiredService<Owner>());
        ((IDisposable)provider).Dispose();
    }

    public sealed class Resource;
    public sealed class Owner(Resource resource) { public Resource Resource { get; } = resource; }
}
