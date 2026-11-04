using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class SharedFactoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Cached_Parent_Disposes_Before_Its_Duplicate_Factory_Dependencies(bool native)
    {
        var instance = new Resource();
        var services = new ServiceCollection();
        services.AddTransient(_ => instance);
        if (!native) services.AddSingleton<Owner>();
        using var container = new WindsorContainer();
        if (native) container.Register(Castle.MicroKernel.Registration.Component.For<Owner>().LifestyleSingleton());
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        Owner owner;
        using (var scope = root.CreateScope()) owner = scope.ServiceProvider.GetRequiredService<Owner>();
        Assert.Equal(0, instance.SyncCalls);
        ((IDisposable)root).Dispose();
        Assert.False(owner.DependencyWasDisposed);
        Assert.Equal(2, instance.SyncCalls);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Every_Transient_Factory_Activation_Owns_A_Disposal(bool keyed, bool asynchronous)
    {
        foreach (var microsoft in new[] { true, false })
        {
            var instance = new Resource();
            var services = new ServiceCollection();
            if (keyed) services.AddKeyedTransient<Resource>("k", (_, _) => instance);
            else services.AddTransient(_ => instance);
            using var container = new WindsorContainer();
            var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = root.CreateScope();
            Resource Resolve() => keyed ? scope.ServiceProvider.GetKeyedServices<Resource>("k").Single()
                : scope.ServiceProvider.GetRequiredService<Resource>();
            Assert.Same(instance, Resolve());
            Assert.Same(instance, Resolve());
            if (asynchronous) await ((IAsyncDisposable)scope).DisposeAsync();
            else scope.Dispose();
            Assert.Equal(asynchronous ? 2 : 0, instance.AsyncCalls);
            Assert.Equal(asynchronous ? 0 : 2, instance.SyncCalls);
            await ((IAsyncDisposable)root).DisposeAsync();
            Assert.Equal(2, instance.SyncCalls + instance.AsyncCalls);
        }
    }

    [Fact]
    public async Task Concurrent_Factories_Can_Return_The_Same_Disposable()
    {
        var instance = new Resource();
        var services = new ServiceCollection();
        services.AddTransient(_ => instance);
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var scope = root.CreateScope();
        var values = await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() => scope.ServiceProvider.GetRequiredService<Resource>())));
        Assert.All(values, value => Assert.Same(instance, value));
        scope.Dispose();
        Assert.Equal(32, instance.SyncCalls);
        ((IDisposable)root).Dispose();
        Assert.Equal(32, instance.SyncCalls);
    }

    [Fact]
    public void Explicit_Release_And_Parent_Ownership_Do_Not_Drop_Duplicate_Activations()
    {
        var instance = new Resource();
        var services = new ServiceCollection();
        services.AddTransient(_ => instance);
        services.AddTransient<Owner>();
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        using (var scope = root.CreateScope())
        {
            var owner = scope.ServiceProvider.GetRequiredService<Owner>();
            Assert.Same(owner.First, owner.Second);
        }
        Assert.Equal(2, instance.SyncCalls);
        using (var scope = root.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<Resource>();
            scope.ServiceProvider.GetRequiredService<Resource>();
            container.Release(instance);
        }
        Assert.Equal(4, instance.SyncCalls);
        ((IDisposable)root).Dispose();
        Assert.Equal(4, instance.SyncCalls);
    }

    public sealed class Resource : IDisposable, IAsyncDisposable
    {
        public int SyncCalls;
        public int AsyncCalls;
        public void Dispose() => Interlocked.Increment(ref SyncCalls);
        public ValueTask DisposeAsync() { Interlocked.Increment(ref AsyncCalls); return default; }
    }
    public sealed class Owner(Resource first, Resource second) : IDisposable
    {
        public Resource First { get; } = first;
        public Resource Second { get; } = second;
        public bool DependencyWasDisposed;
        public void Dispose() => DependencyWasDisposed = First.SyncCalls + First.AsyncCalls != 0;
    }
}
