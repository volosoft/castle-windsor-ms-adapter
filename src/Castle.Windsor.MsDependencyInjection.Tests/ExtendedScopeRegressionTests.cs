using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.Core;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class ExtendedScopeRegressionTests
{
    [Fact]
    public void A_Forwarded_Root_Does_Not_Bypass_ValidateScopes()
    {
        using var container = new WindsorContainer();
        var services = new ServiceCollection();
        services.AddScoped<ScopedMarker>();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services, validateScopes: true);
        IServiceProvider forwarded;
        using (MsLifetimeScope.Using(new ForwardScope(container.Resolve<GlobalMsLifetimeScope>())))
            forwarded = container.Resolve<IServiceProvider>();
        try
        {
            Assert.Throws<InvalidOperationException>(() => root.GetRequiredService<ScopedMarker>());
            Assert.Throws<InvalidOperationException>(() => forwarded.GetRequiredService<ScopedMarker>());
        }
        finally { ((IDisposable)root).Dispose(); }
    }

    [Fact]
    public void A_Derived_Scoped_Lifestyle_Cannot_Return_A_Closed_Cached_Collection()
    {
        using var container = new WindsorContainer();
        container.Kernel.ComponentModelCreated += model =>
        {
            if (model.Services.Contains(typeof(ScopedMarker))) model.CustomLifestyle = typeof(DerivedScoped);
        };
        var services = new ServiceCollection();
        services.AddKeyedScoped<ScopedMarker>("key");
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var scope = (WindsorServiceScope)root.CreateScope();
        var value = Assert.Single(scope.ServiceProvider.GetKeyedServices<ScopedMarker>("key"));
        scope.LifetimeScope.WindsorLifeTimeScope.Dispose();
        try
        {
            Assert.Equal(1, value.Disposals);
            Assert.Throws<ObjectDisposedException>(() => scope.ServiceProvider.GetRequiredKeyedService<ScopedMarker>("key"));
            Assert.Throws<ObjectDisposedException>(() => scope.ServiceProvider.GetKeyedServices<ScopedMarker>("key"));
        }
        finally { scope.Dispose(); ((IDisposable)root).Dispose(); }
    }

    public class DerivedScoped : MsScopedLifestyleManager { }
    public class ScopedMarker : IDisposable
    {
        public int Disposals;
        public void Dispose() => Disposals++;
    }
    private sealed class ForwardScope(IMsLifetimeScope inner) : IMsLifetimeScope
    {
        public Castle.MicroKernel.Lifestyle.Scoped.ILifetimeScope WindsorLifeTimeScope => inner.WindsorLifeTimeScope;
        public void AddInstance(object instance) => inner.AddInstance(instance);
        public void AddChild(MsLifetimeScope child) => inner.AddChild(child);
        public void RemoveChild(MsLifetimeScope child) => inner.RemoveChild(child);
        public void Dispose() => inner.Dispose();
    }
}
