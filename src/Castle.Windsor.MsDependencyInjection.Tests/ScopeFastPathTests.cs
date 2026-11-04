#nullable enable

using System;
using System.Threading;
using System.Threading.Tasks;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Castle.MicroKernel.Lifestyle.Scoped;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public sealed class ScopeFastPathTests
{
    [Fact]
    public async Task Empty_Scope_Can_Complete_Inline()
    {
        var factory = new WindsorServiceProviderFactory();
        var root = factory.CreateServiceProvider(factory.CreateBuilder(new ServiceCollection()));
        var scope = root.CreateAsyncScope();
        var pending = scope.DisposeAsync();
        Assert.True(pending.IsCompletedSuccessfully);
        await pending;
        await ((IAsyncDisposable)root).DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Synchronous_Class_And_Unknown_Factory_Keep_Correct_Thread_Boundaries(bool factoryRegistration)
    {
        var services = new ServiceCollection();
        if (factoryRegistration) services.AddScoped(_ => new SyncResource());
        else services.AddScoped<SyncResource>();
        var factory = new WindsorServiceProviderFactory();
        var root = factory.CreateServiceProvider(factory.CreateBuilder(services));
        var scope = root.CreateAsyncScope();
        var resource = scope.ServiceProvider.GetRequiredService<SyncResource>();
        var caller = Environment.CurrentManagedThreadId;
        var pending = scope.DisposeAsync();
        if (!factoryRegistration) Assert.True(pending.IsCompletedSuccessfully);
        await pending;
        Assert.Equal(1, resource.Calls);
        Assert.Equal(!factoryRegistration, resource.Thread == caller);
        await scope.DisposeAsync();
        Assert.Equal(1, resource.Calls);
        await ((IAsyncDisposable)root).DisposeAsync();
    }

    [Fact]
    public async Task Completed_Async_Resource_Uses_The_Async_Bridge()
    {
        var services = new ServiceCollection(); services.AddScoped<CompletedResource>();
        var factory = new WindsorServiceProviderFactory();
        var root = factory.CreateServiceProvider(factory.CreateBuilder(services));
        var scope = root.CreateAsyncScope();
        var resource = scope.ServiceProvider.GetRequiredService<CompletedResource>();
        var caller = Environment.CurrentManagedThreadId;
        await scope.DisposeAsync();
        Assert.NotEqual(caller, resource.Thread);
        Assert.Equal(1, resource.Calls);
        await ((IAsyncDisposable)root).DisposeAsync();
    }

    [Fact]
    public async Task Linked_Child_Prevents_An_Unsafe_Inline_Parent_Shutdown()
    {
        var services = new ServiceCollection(); services.AddScoped<SyncResource>();
        using var container = new WindsorContainer(); services.AddSingleton<IWindsorContainer>(container);
        var factory = new WindsorServiceProviderFactory();
        var root = factory.CreateServiceProvider(factory.CreateBuilder(services));
        var parent = (WindsorServiceScope)root.CreateScope();
        var child = new WindsorServiceScope(container, parent.LifetimeScope);
        var resource = child.ServiceProvider.GetRequiredService<SyncResource>();
        var caller = Environment.CurrentManagedThreadId;
        await parent.DisposeAsync();
        Assert.NotEqual(caller, resource.Thread);
        Assert.Equal(1, resource.Calls);
        await child.DisposeAsync();
        Assert.Equal(1, resource.Calls);
        await ((IAsyncDisposable)root).DisposeAsync();
    }

    [Fact]
    public async Task Late_Async_Factory_Result_Is_Cleaned_After_Concurrent_Closure()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        CompletedResource? created = null;
        var services = new ServiceCollection();
        services.AddScoped(_ =>
        {
            entered.TrySetResult();
            if (!resume.Wait(TimeSpan.FromSeconds(10))) throw new TimeoutException();
            return created = new CompletedResource();
        });
        var factory = new WindsorServiceProviderFactory();
        var root = factory.CreateServiceProvider(factory.CreateBuilder(services));
        var scope = root.CreateAsyncScope();
        var pending = Task.Run(() => Record.Exception(() => scope.ServiceProvider.GetRequiredService<CompletedResource>()));
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
            await scope.DisposeAsync();
        }
        finally { resume.Set(); }
        Assert.IsType<ObjectDisposedException>(await pending.WaitAsync(TimeSpan.FromSeconds(10)));
        Assert.NotNull(created);
        Assert.Equal(1, created.Calls);
        await ((IAsyncDisposable)root).DisposeAsync();
    }

    [Fact]
    public async Task Inline_Disposal_Preserves_The_Original_Error_And_Does_Not_Retry()
    {
        var services = new ServiceCollection(); services.AddScoped<ThrowingResource>();
        var factory = new WindsorServiceProviderFactory();
        var root = factory.CreateServiceProvider(factory.CreateBuilder(services));
        var scope = root.CreateAsyncScope();
        var resource = scope.ServiceProvider.GetRequiredService<ThrowingResource>();
        var caller = Environment.CurrentManagedThreadId;
        var error = await Record.ExceptionAsync(() => scope.DisposeAsync().AsTask());
        Assert.Same(resource.Error, error);
        Assert.Equal(caller, resource.Thread);
        await scope.DisposeAsync();
        Assert.Equal(1, resource.Calls);
        await ((IAsyncDisposable)root).DisposeAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Parent_Hook_Failure_Still_Closes_The_Scope_Once(bool asynchronous)
    {
        var services = new ServiceCollection(); services.AddScoped<SyncResource>();
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var parent = new ThrowingParent(container.Resolve<GlobalMsLifetimeScope>());
        var scope = new WindsorServiceScope(container, parent);
        var resource = scope.ServiceProvider.GetRequiredService<SyncResource>();
        var error = asynchronous ? await Record.ExceptionAsync(() => scope.DisposeAsync().AsTask()) : Record.Exception(scope.Dispose);
        Assert.Same(parent.Error, error);
        Assert.Equal(1, resource.Calls);
        Assert.Throws<ObjectDisposedException>(() => scope.ServiceProvider.GetRequiredService<SyncResource>());
        scope.Dispose();
        await ((IAsyncDisposable)root).DisposeAsync();
        Assert.Equal(1, resource.Calls);
    }

    private sealed class ThrowingParent(IMsLifetimeScope inner) : IMsLifetimeScope
    {
        public readonly Exception Error = new ApplicationException("Parent hook failed.");
        public ILifetimeScope WindsorLifeTimeScope => inner.WindsorLifeTimeScope;
        public void AddInstance(object instance) => inner.AddInstance(instance);
        public void AddChild(MsLifetimeScope child) => inner.AddChild(child);
        public void RemoveChild(MsLifetimeScope child) => throw Error;
        public void Dispose() => inner.Dispose();
    }

    public class SyncResource : IDisposable
    {
        public int Thread; public int Calls;
        public virtual void Dispose() { Thread = Environment.CurrentManagedThreadId; Calls++; }
    }
    public sealed class CompletedResource : IAsyncDisposable
    {
        public int Thread; public int Calls;
        public ValueTask DisposeAsync() { Thread = Environment.CurrentManagedThreadId; Calls++; return default; }
    }
    public sealed class ThrowingResource : SyncResource
    {
        public readonly Exception Error = new ApplicationException("Cleanup failed.");
        public override void Dispose() { base.Dispose(); throw Error; }
    }
}
