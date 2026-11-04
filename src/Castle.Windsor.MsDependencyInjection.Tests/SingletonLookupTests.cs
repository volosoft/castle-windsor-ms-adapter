using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class SingletonLookupTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void A_Keyed_Container_Does_Not_Replace_The_Builder_Container(bool keyedFirst, bool keyedFactory)
    {
        using var expected = new WindsorContainer();
        using var keyed = new WindsorContainer();
        var services = new ServiceCollection();
        void RegisterKeyed()
        {
            if (keyedFactory) services.AddKeyedSingleton<IWindsorContainer>("key", (_, _) => keyed);
            else services.AddKeyedSingleton<IWindsorContainer>("key", keyed);
        }
        if (keyedFirst) RegisterKeyed();
        services.AddSingleton<IWindsorContainer>(expected);
        if (!keyedFirst) RegisterKeyed();
        Assert.Same(expected, services.GetSingletonServiceOrNull<IWindsorContainer>());
        var factory = new WindsorServiceProviderFactory();
        Assert.Same(expected, factory.CreateBuilder(services));
        var root = factory.CreateServiceProvider(expected);
        Assert.Same(expected, root.GetRequiredService<IWindsorContainer>());
        Assert.Same(keyed, root.GetRequiredKeyedService<IWindsorContainer>("key"));
        ((IDisposable)root).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Keyed_Only_Container_Does_Not_Count_As_An_Ordinary_Instance(bool keyedFactory)
    {
        using var keyed = new WindsorContainer();
        var services = new ServiceCollection();
        if (keyedFactory) services.AddKeyedSingleton<IWindsorContainer>("key", (_, _) => keyed);
        else services.AddKeyedSingleton<IWindsorContainer>("key", keyed);
        Assert.Null(services.GetSingletonServiceOrNull<IWindsorContainer>());
        var factory = new WindsorServiceProviderFactory();
        using var actual = factory.CreateBuilder(services);
        Assert.NotSame(keyed, actual);
        var root = factory.CreateServiceProvider(actual);
        Assert.Same(actual, root.GetRequiredService<IWindsorContainer>());
        ((IDisposable)root).Dispose();
    }

    [Fact]
    public void The_Public_Helper_Preserves_The_First_Ordinary_Instance()
    {
        var first = new Marker();
        var second = new Marker();
        var services = new ServiceCollection();
        services.AddKeyedSingleton("key", new Marker());
        services.AddSingleton(first);
        services.AddSingleton(second);
        Assert.Same(first, services.GetSingletonService<Marker>());
    }
    public sealed class Marker;
}
