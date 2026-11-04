using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;
using Castle.MicroKernel.Registration;
using Castle.Core;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class ScopeOwnerRegressionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_Closed_Transparent_Scope_Rejects_Cached_Singletons(bool keyed)
    {
        using var container = new WindsorContainer();
        var services = new ServiceCollection();
        if (keyed) services.AddKeyedSingleton<Marker>("key");
        else services.AddSingleton<Marker>();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var child = (WindsorServiceScope)root.CreateScope();
        IServiceProvider wrapped;
        using (MsLifetimeScope.Using(new ForwardScope(child.LifetimeScope))) wrapped = container.Resolve<IServiceProvider>();
        object Resolve(IServiceProvider provider) => keyed ? provider.GetRequiredKeyedService<Marker>("key") : provider.GetRequiredService<Marker>();
        Assert.NotNull(Resolve(wrapped));
        child.Dispose();
        try
        {
            Assert.Throws<ObjectDisposedException>(() => Resolve(child.ServiceProvider));
            Assert.Throws<ObjectDisposedException>(() => Resolve(wrapped));
        }
        finally { ((IDisposable)root).Dispose(); }
    }

    [Fact]
    public void A_Closed_Forwarded_Parent_Rejects_Child_Creation()
    {
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
        var parent = (WindsorServiceScope)root.CreateScope();
        var forwarded = new ForwardScope(parent.LifetimeScope);
        parent.Dispose();
        WindsorServiceScope created = null;
        try
        {
            Assert.Throws<ObjectDisposedException>(() => new WindsorServiceScope(container, parent.LifetimeScope));
            Assert.Throws<ObjectDisposedException>(() => created = new WindsorServiceScope(container, forwarded));
        }
        finally { created?.Dispose(); ((IDisposable)root).Dispose(); }
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Native_Async_Resources_Are_Released_Regardless_Of_Registration_Order(bool before, bool factory)
    {
        var state = new State();
        using var container = new WindsorContainer();
        void Register()
        {
            var registration = Component.For<IResource>();
            if (factory) registration.UsingFactoryMethod(_ => new AsyncResource(state));
            else registration.ImplementedBy<AsyncResource>();
            container.Register(registration.LifestyleTransient());
        }
        if (before) Register();
        var services = new ServiceCollection();
        services.AddSingleton(state);
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        if (!before) Register();
        var scope = root.CreateAsyncScope();
        var resource = (AsyncResource)scope.ServiceProvider.GetRequiredService<IResource>();
        try
        {
            await scope.DisposeAsync();
            Assert.Equal(1, resource.Disposals);
        }
        finally
        {
            await ((IAsyncDisposable)root).DisposeAsync();
            if (resource.Disposals == 0) await resource.DisposeAsync();
        }
    }

    public class Marker { }
    public interface IResource { }
    public class State { public int Disposals; }
    public class AsyncResource(State state) : IResource, IAsyncDisposable
    {
        public int Disposals;
        public async ValueTask DisposeAsync() { await Task.Yield(); Disposals++; state.Disposals++; }
    }
    private sealed class ForwardScope(IMsLifetimeScope inner) : IMsLifetimeScope
    {
        public ILifetimeScope WindsorLifeTimeScope => inner.WindsorLifeTimeScope;
        public void AddInstance(object instance) => inner.AddInstance(instance);
        public void AddChild(MsLifetimeScope child) => inner.AddChild(child);
        public void RemoveChild(MsLifetimeScope child) => inner.RemoveChild(child);
        public void Dispose() => inner.Dispose();
    }
}
