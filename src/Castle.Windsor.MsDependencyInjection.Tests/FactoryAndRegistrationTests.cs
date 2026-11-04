using System;
using Castle.DynamicProxy;
using Castle.Core;
using Castle.MicroKernel.Handlers;
using Component = Castle.MicroKernel.Registration.Component;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class FactoryAndRegistrationTests
{
    [Fact]
    public void Invalidated_Collection_Arrays_Are_Not_Retained_By_A_Live_Provider()
    {
        using var container = new WindsorContainer();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Resource>("k", (_, _) => new Resource());
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var reference = InvalidateCollection(container, provider);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Assert.False(reference.IsAlive);
        GC.KeepAlive(provider);
        ((IDisposable)provider).Dispose();
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference InvalidateCollection(IWindsorContainer container, IServiceProvider provider)
    {
        var reference = new WeakReference(provider.GetKeyedServices<Resource>("k"));
        container.AddServices(new ServiceCollection().AddKeyedTransient<Resource>("k"));
        Assert.Equal(2, provider.GetKeyedServices<Resource>("k").Count());
        return reference;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Intercepted_Null_Factory_Results_Remain_Null(bool keyed)
    {
        using var container = new WindsorContainer();
        container.Register(Component.For<PassThrough>());
        container.Kernel.ComponentModelCreated += model =>
        {
            if (model.Services.Contains(typeof(INullResource)))
                model.Interceptors.Add(new InterceptorReference(typeof(PassThrough)));
        };
        var services = new ServiceCollection();
        if (keyed) services.AddKeyedTransient<INullResource>("k", (_, _) => null);
        else services.AddTransient<INullResource>(_ => null);
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        for (var i = 0; i < 100; i++)
            Assert.Null(keyed ? provider.GetKeyedService<INullResource>("k") : provider.GetService<INullResource>());
        Assert.Null((keyed ? provider.GetKeyedServices<INullResource>("k") : provider.GetServices<INullResource>()).Single());
        ((IDisposable)provider).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_User_Factory_Mismatch_Exception_Is_Not_An_Empty_Collection(bool keyed)
    {
        var failure = new GenericHandlerTypeMismatchException("User factory failed.", new ComponentName("user", true));
        using var container = new WindsorContainer();
        var services = new ServiceCollection();
        if (keyed) services.AddKeyedTransient<Resource>("k", (_, _) => throw failure);
        else services.AddTransient<Resource>(_ => throw failure);
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        for (var i = 0; i < 2; i++)
            Assert.Same(failure, Record.Exception(() =>
            {
                if (keyed) provider.GetKeyedServices<Resource>("k");
                else provider.GetServices<Resource>();
            }));
        ((IDisposable)provider).Dispose();
    }

    public interface INullResource : IDisposable, IInitializable { }
    public sealed class PassThrough : IInterceptor
    {
        public void Intercept(IInvocation invocation) => invocation.Proceed();
    }
    [Theory]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void Ordinary_Null_Factories_Follow_Lifetime_And_Constructor_Semantics(ServiceLifetime lifetime)
    {
        foreach (var microsoft in new[] { true, false })
        {
            var calls = 0;
            IServiceCollection services = new ServiceCollection();
            services.Add(new ServiceDescriptor(typeof(Resource), _ => { calls++; return null; }, lifetime));
            services.AddTransient<Owner>();
            using var container = new WindsorContainer();
            var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            using (var scope = root.CreateScope())
            {
                var provider = scope.ServiceProvider;
                Assert.Null(provider.GetService<Resource>());
                Assert.Null(provider.GetService<Resource>());
                Assert.Equal(lifetime == ServiceLifetime.Transient ? 2 : 1, calls);
                Assert.Null(provider.GetRequiredService<Owner>().Resource);
                Assert.Null(Assert.Single(provider.GetServices<Resource>()));
                Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<Resource>());
            }
            ((IDisposable)root).Dispose();
        }
    }

    [Fact]
    public void Adding_A_Descriptor_Invalidates_The_Old_Collection_Selection()
    {
        var first = new Resource();
        var second = new Resource();
        var services = new ServiceCollection();
        services.AddKeyedSingleton<Resource>("k", (_, _) => first);
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        Assert.Same(first, Assert.Single(root.GetKeyedServices<Resource>("k")));
        container.AddServices(new ServiceCollection().AddKeyedSingleton<Resource>("k", (_, _) => second));
        Assert.Equal(new[] { first, second }, root.GetKeyedServices<Resource>("k"));
        ((IDisposable)root).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void External_Constants_Do_Not_Cache_The_Enumerable_Result(bool microsoft)
    {
        var instance = new Resource();
        var services = new ServiceCollection();
        services.AddKeyedSingleton("k", instance);
        using var container = new WindsorContainer();
        var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var first = root.GetKeyedServices<Resource>("k");
        var second = root.GetKeyedServices<Resource>("k");
        Assert.NotSame(first, second);
        Assert.Same(instance, Assert.Single(first));
        Assert.Same(instance, Assert.Single(second));
        ((IDisposable)root).Dispose();
    }

    public sealed class Resource;
    public sealed class Owner(Resource resource) { public Resource Resource { get; } = resource; }
}
