using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Castle.Core;
using Castle.DynamicProxy;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class MixedProviderEntryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public void Parent_Singletons_Keep_Factory_Ownership_When_First_Resolved_From_Descendants(bool warmParent, bool providerDependency, bool grandchild)
    {
        foreach (var keyed in new[] { false, true })
        {
            using var parent = new WindsorContainer();
            using var child = new WindsorContainer();
            using var leaf = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddScoped<Resource>();
            services.AddKeyedScoped<Resource>("k");
            services.AddSingleton<ParentOwner>();
            services.AddSingleton<ParentProviderOwner>();
            var parentRoot = FactoryRoot(parent, services);
            var childRoot = FactoryRoot(child, new ServiceCollection());
            var leafRoot = grandchild ? FactoryRoot(leaf, new ServiceCollection()) : childRoot;
            if (warmParent)
            {
                if (providerDependency) parentRoot.GetRequiredService<ParentProviderOwner>();
                else parentRoot.GetRequiredService<ParentOwner>();
            }
            parent.AddChildContainer(child);
            if (grandchild) child.AddChildContainer(leaf);
            var scopes = providerDependency ? leafRoot.GetRequiredService<ParentProviderOwner>().Factory
                : leafRoot.GetRequiredService<ParentOwner>().Factory;
            using var scope = scopes.CreateScope();
            var resource = ResolveResource(scope.ServiceProvider, keyed);
            parent.Dispose();
            Assert.Equal(0, resource.SyncCalls + resource.AsyncCalls);
            scope.Dispose();
            Assert.Equal(1, resource.SyncCalls);
            ((IDisposable)leafRoot).Dispose();
            ((IDisposable)childRoot).Dispose();
            ((IDisposable)parentRoot).Dispose();
            Assert.Equal(1, resource.SyncCalls);
        }
    }

    [Fact]
    public void A_Factory_Entry_Does_Not_Change_An_Unrelated_Containers_Helper_Factory()
    {
        using var helperContainer = new WindsorContainer();
        var helperRoot = WindsorRegistrationHelper.CreateServiceProvider(helperContainer, new ServiceCollection().AddScoped<Resource>());
        helperRoot.GetRequiredService<IServiceScopeFactory>();
        using var factoryContainer = new WindsorContainer();
        var services = new ServiceCollection();
        services.AddTransient(_ => new ScopeHolder(helperContainer.Resolve<IServiceScopeFactory>().CreateScope()));
        var factoryRoot = FactoryRoot(factoryContainer, services);
        using var holder = factoryRoot.GetRequiredService<ScopeHolder>();
        var resource = holder.Scope.ServiceProvider.GetRequiredService<Resource>();
        ((IDisposable)helperRoot).Dispose();
        Assert.Equal(1, resource.SyncCalls);
        ((IDisposable)factoryRoot).Dispose();
        Assert.Equal(1, resource.SyncCalls);
    }

    [Theory]
    [InlineData(false, "direct", false)]
    [InlineData(false, "direct", true)]
    [InlineData(false, "saved", false)]
    [InlineData(false, "saved", true)]
    [InlineData(false, "constructor", false)]
    [InlineData(false, "constructor", true)]
    [InlineData(false, "child", false)]
    [InlineData(false, "child", true)]
    [InlineData(true, "direct", false)]
    [InlineData(true, "direct", true)]
    [InlineData(true, "saved", false)]
    [InlineData(true, "saved", true)]
    [InlineData(true, "constructor", false)]
    [InlineData(true, "constructor", true)]
    [InlineData(true, "child", false)]
    [InlineData(true, "child", true)]
    public async Task Each_Entry_Keeps_Its_Scope_Ownership_After_The_Other_Is_Created(bool factoryFirst, string entry, bool asynchronous)
    {
        foreach (var keyed in new[] { false, true })
        {
            using var container = new WindsorContainer();
            var services = Services(container);
            var factory = new WindsorServiceProviderFactory();
            var first = factoryFirst ? factory.CreateServiceProvider(factory.CreateBuilder(services))
                : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var firstScopes = ResolveFactory(first, entry, keyed);
            var second = factoryFirst ? WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection())
                : factory.CreateServiceProvider(container);
            var secondScopes = ResolveFactory(second, entry, keyed);
            Assert.Same(firstScopes, ResolveFactory(first, entry, keyed));
            Assert.Same(secondScopes, ResolveFactory(second, entry, keyed));
            var helperRoot = factoryFirst ? second : first;
            var factoryRoot = factoryFirst ? first : second;
            var helperScopes = factoryFirst ? secondScopes : firstScopes;
            var factoryScopes = factoryFirst ? firstScopes : secondScopes;
            var parents = new List<IServiceScope>();
            var helperScope = CreateScope(helperRoot, helperScopes, entry, parents);
            var factoryScope = CreateScope(factoryRoot, factoryScopes, entry, parents);
            var helperResource = ResolveResource(helperScope.ServiceProvider, keyed);
            var factoryResource = ResolveResource(factoryScope.ServiceProvider, keyed);
            try
            {
                if (asynchronous) await ((IAsyncDisposable)helperRoot).DisposeAsync();
                else ((IDisposable)helperRoot).Dispose();
                Assert.Equal(asynchronous ? 0 : 1, helperResource.SyncCalls);
                Assert.Equal(asynchronous ? 1 : 0, helperResource.AsyncCalls);
                Assert.Equal(0, factoryResource.SyncCalls + factoryResource.AsyncCalls);
                await ((IAsyncDisposable)factoryScope).DisposeAsync();
                Assert.Equal(0, factoryResource.SyncCalls);
                Assert.Equal(1, factoryResource.AsyncCalls);
                helperScope.Dispose();
                Assert.Equal(1, helperResource.SyncCalls + helperResource.AsyncCalls);
            }
            finally
            {
                helperScope.Dispose();
                await ((IAsyncDisposable)factoryScope).DisposeAsync();
                foreach (var parent in parents) parent.Dispose();
                ((IDisposable)helperRoot).Dispose();
                ((IDisposable)factoryRoot).Dispose();
            }
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Entry_Binding_Preserves_Windsors_Scope_Factory_Interceptors(bool factoryFirst)
    {
        using var container = new WindsorContainer();
        var interceptor = new ScopeInterceptor();
        container.Register(Component.For<ScopeInterceptor>().Instance(interceptor));
        container.Kernel.ComponentModelCreated += model =>
        {
            if (model.Implementation == typeof(WindsorServiceScopeFactory))
                model.Interceptors.Add(InterceptorReference.ForType<ScopeInterceptor>());
        };
        var services = Services(container);
        var factory = new WindsorServiceProviderFactory();
        var first = factoryFirst ? factory.CreateServiceProvider(factory.CreateBuilder(services))
            : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        first.GetRequiredService<IServiceScopeFactory>();
        var second = factoryFirst ? WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection())
            : factory.CreateServiceProvider(container);
        var helperRoot = factoryFirst ? second : first;
        var factoryRoot = factoryFirst ? first : second;
        using var helperScope = helperRoot.CreateScope();
        using var factoryScope = factoryRoot.CreateScope();
        var helperResource = helperScope.ServiceProvider.GetRequiredService<Resource>();
        var factoryResource = factoryScope.ServiceProvider.GetRequiredService<Resource>();
        Assert.Equal(2, interceptor.Calls);
        ((IDisposable)helperRoot).Dispose();
        Assert.Equal(1, helperResource.SyncCalls);
        Assert.Equal(0, factoryResource.SyncCalls);
        factoryScope.Dispose();
        Assert.Equal(1, factoryResource.SyncCalls);
        ((IDisposable)factoryRoot).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Explicit_Scope_Factory_Overrides_Are_Not_Replaced(bool factoryFirst)
    {
        using var container = new WindsorContainer();
        var expected = new CustomFactory();
        var services = Services(container);
        services.AddSingleton<IServiceScopeFactory>(expected);
        var factory = new WindsorServiceProviderFactory();
        var first = factoryFirst ? factory.CreateServiceProvider(factory.CreateBuilder(services))
            : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var second = factoryFirst ? WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection())
            : factory.CreateServiceProvider(container);
        Assert.Same(expected, first.GetRequiredService<IServiceScopeFactory>());
        Assert.Same(expected, second.GetRequiredService<IServiceScopeFactory>());
        Assert.Same(expected, first.GetRequiredService<FactoryOwner>().Factory);
        Assert.Same(expected, second.GetRequiredService<FactoryOwner>().Factory);
        ((IDisposable)first).Dispose();
        ((IDisposable)second).Dispose();
    }

    private static IServiceCollection Services(IWindsorContainer container)
    {
        var services = new ServiceCollection();
        services.AddSingleton(container);
        services.AddScoped<Resource>();
        services.AddKeyedScoped<Resource>("k");
        services.AddTransient<FactoryOwner>();
        services.AddKeyedTransient<FactoryOwner>("owner");
        return services;
    }

    private static IServiceProvider FactoryRoot(IWindsorContainer container, IServiceCollection services)
    {
        services.AddSingleton(container);
        var factory = new WindsorServiceProviderFactory();
        return factory.CreateServiceProvider(factory.CreateBuilder(services));
    }

    private static IServiceScopeFactory ResolveFactory(IServiceProvider provider, string entry, bool keyed)
        => entry == "constructor" ? (keyed ? provider.GetRequiredKeyedService<FactoryOwner>("owner")
            : provider.GetRequiredService<FactoryOwner>()).Factory : provider.GetRequiredService<IServiceScopeFactory>();

    private static IServiceScope CreateScope(IServiceProvider provider, IServiceScopeFactory factory, string entry, List<IServiceScope> parents)
    {
        if (entry == "direct") return provider.CreateScope();
        if (entry != "child") return factory.CreateScope();
        var parent = provider.CreateScope();
        parents.Add(parent);
        Assert.Same(factory, parent.ServiceProvider.GetRequiredService<IServiceScopeFactory>());
        return parent.ServiceProvider.CreateScope();
    }

    private static Resource ResolveResource(IServiceProvider provider, bool keyed)
        => keyed ? provider.GetRequiredKeyedService<Resource>("k") : provider.GetRequiredService<Resource>();

    public sealed class FactoryOwner(IServiceScopeFactory factory) { public IServiceScopeFactory Factory { get; } = factory; }
    public sealed class ParentOwner(IServiceScopeFactory factory) { public IServiceScopeFactory Factory { get; } = factory; }
    public sealed class ParentProviderOwner(IServiceProvider provider) { public IServiceScopeFactory Factory => provider.GetRequiredService<IServiceScopeFactory>(); }
    public sealed class ScopeHolder(IServiceScope scope) : IDisposable
    {
        public IServiceScope Scope { get; } = scope;
        public void Dispose() => Scope.Dispose();
    }
    public sealed class Resource : IDisposable, IAsyncDisposable
    {
        public int SyncCalls;
        public int AsyncCalls;
        public void Dispose() => SyncCalls++;
        public ValueTask DisposeAsync() { AsyncCalls++; return default; }
    }
    public sealed class ScopeInterceptor : IInterceptor
    {
        public int Calls;
        public void Intercept(IInvocation invocation)
        {
            if (invocation.Method.Name == nameof(IServiceScopeFactory.CreateScope)) Calls++;
            invocation.Proceed();
        }
    }
    public sealed class CustomFactory : IServiceScopeFactory
    {
        public IServiceScope CreateScope() => throw new NotSupportedException("The override should only be resolved in this test.");
    }
}
