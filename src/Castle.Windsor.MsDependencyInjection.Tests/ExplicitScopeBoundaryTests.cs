using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class ExplicitScopeBoundaryTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Factory_Scopes_Own_Transient_Resources_After_Root_Close(bool keyed, bool factory)
    {
        var services = new ServiceCollection();
        Register<Resource>(services, keyed, factory, () => new Resource());
        var builder = new WindsorServiceProviderFactory();
        using var container = builder.CreateBuilder(services);
        var root = builder.CreateServiceProvider(container);
        using var scope = root.CreateScope();
        var resource = Resolve<Resource>(scope.ServiceProvider, keyed);
        ((IDisposable)root).Dispose();
        Assert.Equal(0, resource.Disposals);
        scope.Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public async Task Factory_Scopes_Own_Async_Resources_After_Root_Close(bool keyed, bool factory, bool asyncRoot)
    {
        var services = new ServiceCollection();
        Register<AsyncResource>(services, keyed, factory, () => new AsyncResource());
        var builder = new WindsorServiceProviderFactory();
        using var container = builder.CreateBuilder(services);
        var root = builder.CreateServiceProvider(container);
        await using var scope = root.CreateAsyncScope();
        var resource = Resolve<AsyncResource>(scope.ServiceProvider, keyed);
        if (asyncRoot) await ((IAsyncDisposable)root).DisposeAsync();
        else ((IDisposable)root).Dispose();
        Assert.Equal(0, resource.Disposals);
        await scope.DisposeAsync();
        Assert.Equal(1, resource.Disposals);
    }

    [Fact]
    public void Explicit_Children_Keep_Their_Parents_Independent_Release_Ownership()
    {
        var services = new ServiceCollection();
        services.AddTransient<Resource>();
        var builder = new WindsorServiceProviderFactory();
        using var container = builder.CreateBuilder(services);
        var root = builder.CreateServiceProvider(container);
        using var parent = (WindsorServiceScope)root.CreateScope();
        using var child = new WindsorServiceScope(container, parent.LifetimeScope);
        var resource = child.ServiceProvider.GetRequiredService<Resource>();
        ((IDisposable)root).Dispose();
        Assert.Equal(0, resource.Disposals);
        parent.Dispose();
        Assert.Equal(1, resource.Disposals);
        child.Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    [Fact]
    public void Caller_Owned_Scopes_Track_Shared_Factory_Activations_Independently()
    {
        var resource = new Resource();
        var services = new ServiceCollection();
        services.AddTransient(_ => resource);
        var builder = new WindsorServiceProviderFactory();
        using var container = builder.CreateBuilder(services);
        var root = builder.CreateServiceProvider(container);
        using var first = root.CreateScope();
        using var second = root.CreateScope();
        foreach (var scope in new[] { first, second })
        {
            Assert.Same(resource, scope.ServiceProvider.GetRequiredService<Resource>());
            Assert.Same(resource, scope.ServiceProvider.GetRequiredService<Resource>());
        }
        ((IDisposable)root).Dispose();
        Assert.Equal(0, resource.Disposals);
        first.Dispose();
        Assert.Equal(2, resource.Disposals);
        second.Dispose();
        Assert.Equal(4, resource.Disposals);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Native_Singleton_Work_Uses_Independent_Scopes(bool registerFirst, bool keyed)
    {
        using var container = new WindsorContainer();
        var resources = new List<Resource>();
        void RegisterOwner() => container.Register(Component.For<Owner>().UsingFactoryMethod(kernel =>
        {
            var scopes = kernel.Resolve<IServiceScopeFactory>();
            for (var i = 0; i < 2; i++)
            {
                using var scope = scopes.CreateScope();
                var resource = Resolve<Resource>(scope.ServiceProvider, keyed);
                Assert.Equal(0, resource.Disposals);
                resources.Add(resource);
            }
            return new Owner();
        }).LifestyleSingleton());
        if (registerFirst) RegisterOwner();
        var services = new ServiceCollection();
        if (keyed) services.AddKeyedScoped<Resource>("key");
        else services.AddScoped<Resource>();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        if (!registerFirst) RegisterOwner();
        root.GetRequiredService<Owner>();
        Assert.Equal(2, resources.Count);
        Assert.NotSame(resources[0], resources[1]);
        Assert.All(resources, resource => Assert.Equal(1, resource.Disposals));
        ((IDisposable)root).Dispose();
        Assert.All(resources, resource => Assert.Equal(1, resource.Disposals));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Pooled_Checkouts_Return_To_Windsor_When_Their_Scope_Closes(bool registerFirst, bool factoryEntry)
    {
        using var container = new WindsorContainer();
        void RegisterPool() => container.Register(Component.For<Resource>().LifestylePooled(0, 2));
        if (registerFirst) RegisterPool();
        var services = new ServiceCollection();
        services.AddSingleton<IWindsorContainer>(container);
        var builder = new WindsorServiceProviderFactory();
        var root = factoryEntry ? builder.CreateServiceProvider(builder.CreateBuilder(services))
            : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        if (!registerFirst) RegisterPool();
        Resource first;
        using (var scope = root.CreateScope()) first = scope.ServiceProvider.GetRequiredService<Resource>();
        using (var scope = root.CreateScope())
        {
            Assert.Same(first, scope.ServiceProvider.GetRequiredService<Resource>());
            Assert.Equal(0, first.Disposals);
        }
        ((IDisposable)root).Dispose();
        Assert.Equal(1, first.Disposals);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Native_Singleton_Constructor_Dependencies_Remain_Root_Owned(bool registerFirst, bool keyed)
    {
        using var container = new WindsorContainer();
        void RegisterOwner() => container.Register(keyed
            ? Component.For<DependencyOwner>().ImplementedBy<KeyedDependencyOwner>().LifestyleSingleton()
            : Component.For<DependencyOwner>().LifestyleSingleton());
        if (registerFirst) RegisterOwner();
        var services = new ServiceCollection();
        services.AddSingleton<IWindsorContainer>(container);
        if (keyed) services.AddKeyedTransient<Resource>("key");
        else services.AddTransient<Resource>();
        var builder = new WindsorServiceProviderFactory();
        var root = builder.CreateServiceProvider(builder.CreateBuilder(services));
        if (!registerFirst) RegisterOwner();
        DependencyOwner owner;
        using (var scope = root.CreateScope()) owner = scope.ServiceProvider.GetRequiredService<DependencyOwner>();
        Assert.Equal(0, owner.Resource.Disposals);
        ((IDisposable)root).Dispose();
        Assert.False(owner.DependencyWasDisposed);
        Assert.Equal(1, owner.Resource.Disposals);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Native_Transient_Registrations_Keep_Explicit_Container_Release(bool registerFirst)
    {
        using var container = new WindsorContainer();
        void RegisterResource() => container.Register(Component.For<Resource>().LifestyleTransient());
        if (registerFirst) RegisterResource();
        var services = new ServiceCollection();
        services.AddSingleton<IWindsorContainer>(container);
        var builder = new WindsorServiceProviderFactory();
        var root = builder.CreateServiceProvider(builder.CreateBuilder(services));
        if (!registerFirst) RegisterResource();
        using (var scope = root.CreateScope())
        {
            var resource = scope.ServiceProvider.GetRequiredService<Resource>();
            container.Release(resource);
            Assert.Equal(1, resource.Disposals);
        }
        ((IDisposable)root).Dispose();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Validation_Allows_Work_In_A_New_Scope_Inside_A_Singleton_Factory(bool factoryEntry, bool keyed)
    {
        var services = new ServiceCollection();
        if (keyed) services.AddKeyedScoped<Resource>("key");
        else services.AddScoped<Resource>();
        Resource resource = null;
        services.AddSingleton(provider =>
        {
            using var scope = provider.CreateScope();
            resource = Resolve<Resource>(scope.ServiceProvider, keyed);
            return new Owner();
        });
        using var container = new WindsorContainer();
        services.AddSingleton<IWindsorContainer>(container);
        var builder = new WindsorServiceProviderFactory(validateScopes: true);
        var root = factoryEntry ? builder.CreateServiceProvider(builder.CreateBuilder(services))
            : WindsorRegistrationHelper.CreateServiceProvider(container, services, validateScopes: true);
        Assert.NotNull(root.GetRequiredService<Owner>());
        Assert.Equal(1, resource.Disposals);
        ((IDisposable)root).Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Custom_Transient_Activations_Are_Not_Cached_By_Keyed_Collections(bool anyKey, bool scopedBase)
    {
        using var container = new WindsorContainer();
        container.Kernel.ComponentModelCreated += model =>
        {
            if (!model.Services.Contains(typeof(Resource))) return;
            model.LifestyleType = LifestyleType.Custom;
            model.CustomLifestyle = scopedBase ? typeof(CustomScopedTransient) : typeof(CustomTransient);
        };
        var services = new ServiceCollection();
        services.AddKeyedTransient<Resource>("key");
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        using (var scope = root.CreateScope())
        {
            var provider = scope.ServiceProvider;
            Assert.NotSame(provider.GetRequiredKeyedService<Resource>("key"), provider.GetRequiredKeyedService<Resource>("key"));
            var key = anyKey ? KeyedService.AnyKey : "key";
            var first = provider.GetKeyedServices<Resource>(key).Single();
            var second = provider.GetKeyedServices<Resource>(key).Single();
            Assert.NotSame(first, second);
        }
        ((IDisposable)root).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Custom_Scoped_Collections_Keep_Windsors_Instance_Lifetime(bool anyKey)
    {
        using var container = new WindsorContainer();
        container.Kernel.ComponentModelCreated += model =>
        {
            if (!model.Services.Contains(typeof(Resource))) return;
            model.LifestyleType = LifestyleType.Custom;
            model.CustomLifestyle = typeof(CustomScoped);
        };
        var services = new ServiceCollection();
        services.AddKeyedScoped<Resource>("key");
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var key = anyKey ? KeyedService.AnyKey : "key";
        Resource first;
        using (var scope = root.CreateScope())
        {
            first = scope.ServiceProvider.GetKeyedServices<Resource>(key).Single();
            Assert.Same(first, scope.ServiceProvider.GetKeyedServices<Resource>(key).Single());
            Assert.Equal(0, first.Disposals);
        }
        Assert.Equal(1, first.Disposals);
        using (var scope = root.CreateScope())
            Assert.NotSame(first, scope.ServiceProvider.GetKeyedServices<Resource>(key).Single());
        ((IDisposable)root).Dispose();
        Assert.Equal(1, first.Disposals);
    }

    private static T Resolve<T>(IServiceProvider provider, bool keyed) where T : class =>
        keyed ? provider.GetRequiredKeyedService<T>("key") : provider.GetRequiredService<T>();

    private static void Register<T>(IServiceCollection services, bool keyed, bool factory, Func<T> create) where T : class
    {
        if (keyed)
        {
            if (factory) services.AddKeyedTransient<T>("key", (_, _) => create());
            else services.AddKeyedTransient<T>("key");
        }
        else if (factory) services.AddTransient(_ => create());
        else services.AddTransient<T>();
    }

    public sealed class Owner;
    public class Resource : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }
    public sealed class AsyncResource : IAsyncDisposable
    {
        public int Disposals;
        public ValueTask DisposeAsync() { Disposals++; return default; }
    }
    public sealed class CustomTransient : TransientLifestyleManager;
    public sealed class CustomScoped : MsScopedLifestyleManager;
    public sealed class CustomScopedTransient : MsScopedLifestyleManager
    {
        public override object Resolve(CreationContext context, IReleasePolicy releasePolicy)
        {
            var burden = CreateInstance(context, trackedExternally: false);
            Track(burden, releasePolicy);
            return burden.Instance;
        }
    }
    public class DependencyOwner(Resource resource) : IDisposable
    {
        public Resource Resource { get; } = resource;
        public bool DependencyWasDisposed;
        public void Dispose() => DependencyWasDisposed = Resource.Disposals != 0;
    }
    public sealed class KeyedDependencyOwner([FromKeyedServices("key")] Resource resource) : DependencyOwner(resource);
}
