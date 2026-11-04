using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Castle.Core;
using Castle.DynamicProxy;
using Castle.MicroKernel;
using Castle.MicroKernel.LifecycleConcerns;
using Castle.MicroKernel.ModelBuilder;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class AsyncDisposalTests
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
    public async Task Async_Only_Resources_Are_Released_Once(bool keyed, bool factory, bool child)
    {
        foreach (var microsoft in new[] { true, false })
        {
            var log = new Log();
            var services = new ServiceCollection();
            services.AddSingleton(log);
            if (keyed)
            {
                if (factory) services.AddKeyedScoped<AsyncOnly>("k", (_, _) => new AsyncOnly(log));
                else services.AddKeyedScoped<AsyncOnly>("k");
            }
            else
            {
                if (factory) services.AddScoped(_ => new AsyncOnly(log));
                else services.AddScoped<AsyncOnly>();
            }
            using var container = new WindsorContainer();
            var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = root.CreateAsyncScope();
            var provider = child ? scope.ServiceProvider : root;
            var resource = keyed ? provider.GetRequiredKeyedService<AsyncOnly>("k") : provider.GetRequiredService<AsyncOnly>();
            await scope.DisposeAsync();
            Assert.Equal(child ? 1 : 0, resource.Disposals);
            await Assert.IsAssignableFrom<IAsyncDisposable>(root).DisposeAsync();
            Assert.Equal(1, resource.Disposals);
            Assert.Equal(new[] { "async" }, log.Events);
        }
    }

    [Fact]
    public async Task Async_Parent_Remains_Ahead_Of_Native_Dependency()
    {
        var log = new Log();
        using var container = new WindsorContainer();
        container.Register(Component.For<NativeResource>().LifestyleSingleton());
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<AsyncOwner>();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var owner = root.GetRequiredService<AsyncOwner>();
        await Assert.IsAssignableFrom<IAsyncDisposable>(root).DisposeAsync();
        Assert.False(owner.DependencyWasDisposed);
        Assert.True(owner.Resource.Disposed);
    }

    [Fact]
    public async Task Native_Parent_Remains_Ahead_Of_Async_Dependency()
    {
        var log = new Log();
        using var container = new WindsorContainer();
        container.Register(Component.For<NativeOwner>().LifestyleSingleton());
        var services = new ServiceCollection();
        services.AddSingleton(log);
        services.AddSingleton<AsyncOnly>();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var owner = root.GetRequiredService<NativeOwner>();
        await Assert.IsAssignableFrom<IAsyncDisposable>(root).DisposeAsync();
        Assert.False(owner.DependencyWasDisposed);
        Assert.Equal(1, owner.Resource.Disposals);
    }

    [Fact]
    public void Synchronous_Disposal_Rejects_Async_Only_Resources()
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Log());
        services.AddScoped<AsyncOnly>();
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var scope = root.CreateScope();
        scope.ServiceProvider.GetRequiredService<AsyncOnly>();
        Assert.Throws<InvalidOperationException>(() => scope.Dispose());
        ((IDisposable)root).Dispose();
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task Custom_Late_Bound_Concerns_Are_Preserved(bool asynchronous, bool separate, bool afterInstallation)
    {
        var log = new Log();
        using var container = new WindsorContainer();
        if (!afterInstallation) container.Kernel.ComponentModelBuilder.AddContributor(new CustomConcernContributor(log, separate));
        var services = new ServiceCollection();
        services.AddSingleton<IConcernResource>(_ => new ConcernResource(log));
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        if (afterInstallation)
            container.Kernel.GetHandler(typeof(IConcernResource)).ComponentModel.Lifecycle.DecommissionConcerns
                .OfType<LateBoundDecommissionConcerns>().Single().AddConcern<ConcernResource>(new CallbackConcern(log));
        root.GetRequiredService<IConcernResource>();
        if (asynchronous) await ((IAsyncDisposable)root).DisposeAsync();
        else ((IDisposable)root).Dispose();
        Assert.Equal(new[] { asynchronous ? "async" : "sync", "callback" }, log.Events);
    }

    private sealed class CustomConcernContributor(Log log, bool separate) : IContributeComponentModelConstruction
    {
        public void ProcessModel(IKernel kernel, ComponentModel model)
        {
            if (!model.Services.Contains(typeof(IConcernResource))) return;
            var concern = separate ? new LateBoundDecommissionConcerns()
                : model.Lifecycle.DecommissionConcerns.OfType<LateBoundDecommissionConcerns>().Single();
            concern.AddConcern<ConcernResource>(new CallbackConcern(log));
            if (separate) model.Lifecycle.Add(concern);
        }
    }
    private sealed class CallbackConcern(Log log) : IDecommissionConcern
    {
        public void Apply(ComponentModel model, object component) => log.Events.Add("callback");
    }
    public interface IConcernResource { }
    public sealed class ConcernResource(Log log) : IConcernResource, IDisposable, IAsyncDisposable
    {
        public void Dispose() => log.Events.Add("sync");
        public async ValueTask DisposeAsync() { await Task.Yield(); log.Events.Add("async"); }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_Intercepted_Factory_Disposes_Its_Async_Only_Target(bool keyed)
    {
        var log = new Log();
        using var container = new WindsorContainer();
        container.Register(Component.For<PassThrough>());
        container.Kernel.ComponentModelCreated += model =>
        {
            if (model.Services.Contains(typeof(IAsyncResource)))
                model.Interceptors.Add(new InterceptorReference(typeof(PassThrough)));
        };
        var resource = new HiddenAsyncResource(log);
        var services = new ServiceCollection();
        if (keyed) services.AddKeyedTransient<IAsyncResource>("k", (_, _) => resource);
        else services.AddTransient<IAsyncResource>(_ => resource);
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var scope = provider.CreateAsyncScope();
        Assert.NotNull(keyed ? scope.ServiceProvider.GetKeyedService<IAsyncResource>("k")
            : scope.ServiceProvider.GetService<IAsyncResource>());
        await scope.DisposeAsync();
        Assert.Equal(new[] { "async" }, log.Events);
        await ((IAsyncDisposable)provider).DisposeAsync();
        Assert.Equal(new[] { "async" }, log.Events);
    }
    public interface IAsyncResource { }
    public sealed class HiddenAsyncResource(Log log) : IAsyncResource, IAsyncDisposable
    {
        public async ValueTask DisposeAsync() { await Task.Yield(); log.Events.Add("async"); }
    }
    public sealed class PassThrough : IInterceptor
    {
        public void Intercept(IInvocation invocation) => invocation.Proceed();
    }

    public sealed class Log { public List<string> Events { get; } = new(); }
    public sealed class AsyncOnly(Log log) : IAsyncDisposable
    {
        public int Disposals { get; private set; }
        public async ValueTask DisposeAsync() { await Task.Yield(); Disposals++; log.Events.Add("async"); }
    }
    public sealed class NativeResource : IDisposable
    {
        public bool Disposed { get; private set; }
        public void Dispose() => Disposed = true;
    }
    public sealed class AsyncOwner(NativeResource resource) : IAsyncDisposable
    {
        public NativeResource Resource { get; } = resource;
        public bool DependencyWasDisposed { get; private set; }
        public async ValueTask DisposeAsync() { await Task.Yield(); DependencyWasDisposed = Resource.Disposed; }
    }
    public sealed class NativeOwner(AsyncOnly resource) : IDisposable
    {
        public AsyncOnly Resource { get; } = resource;
        public bool DependencyWasDisposed { get; private set; }
        public void Dispose() => DependencyWasDisposed = Resource.Disposals != 0;
    }
}
