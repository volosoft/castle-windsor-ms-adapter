using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class GenericAsyncDisposalTests
{
    public static IEnumerable<object[]> Cases()
    {
        foreach (var lifetime in new[] { ServiceLifetime.Singleton, ServiceLifetime.Scoped, ServiceLifetime.Transient })
        foreach (var keyMode in new[] { 0, 1, 2 })
        foreach (var factory in new[] { false, true })
        foreach (var fromRoot in new[] { false, true })
        foreach (var disposalMode in new[] { 0, 1, 2 })
            yield return new object[] { lifetime, keyMode, factory, fromRoot, disposalMode };
    }

    [Theory]
    [MemberData(nameof(Cases))]
    public async Task Open_Generic_Resources_Use_The_Requested_Disposal_Mode(
        ServiceLifetime lifetime, int keyMode, bool factory, bool fromRoot, int disposalMode)
    {
        foreach (var microsoft in new[] { true, false })
        {
            using var container = new WindsorContainer();
            IServiceCollection services = new ServiceCollection();
            services.AddSingleton<IWindsorContainer>(container);
            var implementation = disposalMode == 0 ? typeof(AsyncOnly<>) : typeof(Dual<>);
            services.Add(keyMode == 0
                ? new ServiceDescriptor(typeof(IResource<>), implementation, lifetime)
                : new ServiceDescriptor(typeof(IResource<>), keyMode == 1 ? "key" : KeyedService.AnyKey, implementation, lifetime));
            var providerFactory = new WindsorServiceProviderFactory();
            var root = microsoft ? services.BuildServiceProvider()
                : factory ? providerFactory.CreateServiceProvider(providerFactory.CreateBuilder(services))
                : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = fromRoot ? null : root.CreateScope();
            var provider = scope?.ServiceProvider ?? root;
            var first = Resolve<int>(provider, keyMode);
            var second = Resolve<string>(provider, keyMode);

            if (scope != null) await Close(scope, disposalMode);
            var released = !fromRoot && lifetime != ServiceLifetime.Singleton;
            AssertCalls(first, released, disposalMode);
            AssertCalls(second, released, disposalMode);
            await Close(root, disposalMode);
            AssertCalls(first, true, disposalMode);
            AssertCalls(second, true, disposalMode);
        }
    }

    private static IResource<T> Resolve<T>(IServiceProvider provider, int keyMode)
        => keyMode == 0 ? provider.GetRequiredService<IResource<T>>() : provider.GetRequiredKeyedService<IResource<T>>("key");

    private static ValueTask Close(object owner, int disposalMode)
    {
        if (disposalMode != 2) return ((IAsyncDisposable)owner).DisposeAsync();
        ((IDisposable)owner).Dispose();
        return default;
    }

    private static void AssertCalls<T>(IResource<T> resource, bool released, int disposalMode)
    {
        Assert.Equal(released && disposalMode == 2 ? 1 : 0, resource.SyncCalls);
        Assert.Equal(released && disposalMode != 2 ? 1 : 0, resource.AsyncCalls);
    }

    public interface IResource<T>
    {
        int SyncCalls { get; }
        int AsyncCalls { get; }
    }

    public class AsyncOnly<T> : IResource<T>, IAsyncDisposable
    {
        public int SyncCalls { get; protected set; }
        public int AsyncCalls { get; private set; }
        public async ValueTask DisposeAsync()
        {
            await Task.Yield();
            AsyncCalls++;
        }
    }

    public sealed class Dual<T> : AsyncOnly<T>, IDisposable
    {
        public void Dispose() => SyncCalls++;
    }
}
