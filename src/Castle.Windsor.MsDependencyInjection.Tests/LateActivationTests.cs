using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class LateActivationTests
    {
        [Theory]
        [InlineData(ServiceLifetime.Transient, false)]
        [InlineData(ServiceLifetime.Transient, true)]
        [InlineData(ServiceLifetime.Singleton, false)]
        [InlineData(ServiceLifetime.Singleton, true)]
        public async Task Closing_During_Factory_Activation_Releases_And_Rejects_The_Late_Result(ServiceLifetime lifetime, bool asyncOnly)
        {
            foreach (var microsoft in new[] { true, false })
            {
                using var entered = new ManualResetEventSlim();
                using var resume = new ManualResetEventSlim();
                IResource resource = asyncOnly ? new AsyncResource() : new DualResource();
                IServiceCollection services = new ServiceCollection();
                services.Add(new ServiceDescriptor(typeof(IResource), _ =>
                {
                    entered.Set();
                    if (!resume.Wait(TimeSpan.FromSeconds(10)))
                    {
                        throw new TimeoutException("Factory was not resumed.");
                    }

                    return resource;
                }, lifetime));
                using var container = new WindsorContainer();
                var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
                var scope = lifetime == ServiceLifetime.Singleton ? null : root.CreateScope();
                var provider = scope?.ServiceProvider ?? root;
                var pending = Task.Run(() => provider.GetRequiredService<IResource>());
                try
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                    await Task.Run(async () => await ((IAsyncDisposable)((object)scope ?? root)).DisposeAsync())
                        .WaitAsync(TimeSpan.FromSeconds(5));
                }
                finally
                {
                    resume.Set();
                }
                var failure = await Record.ExceptionAsync(async () => await pending.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsType<ObjectDisposedException>(failure);
                Assert.Equal(1, resource.Disposals);
                if (resource is DualResource dual)
                {
                    Assert.Equal(1, dual.SyncCalls);
                }

                await ((IAsyncDisposable)root).DisposeAsync();
                Assert.Equal(1, resource.Disposals);
            }
        }

        [Theory]
        [InlineData(ServiceLifetime.Transient, false)]
        [InlineData(ServiceLifetime.Transient, true)]
        [InlineData(ServiceLifetime.Scoped, false)]
        [InlineData(ServiceLifetime.Scoped, true)]
        [InlineData(ServiceLifetime.Singleton, false)]
        [InlineData(ServiceLifetime.Singleton, true)]
        public async Task A_Factory_That_Closes_Its_Owner_Cannot_Publish_The_Result(ServiceLifetime lifetime, bool asyncOnly)
        {
            foreach (var microsoft in new[] { true, false })
            {
                IResource resource = asyncOnly ? new AsyncResource() : new DualResource();
                object owner = null;
                IServiceCollection services = new ServiceCollection();
                services.Add(new ServiceDescriptor(typeof(IResource), _ =>
                {
                    ((IDisposable)owner).Dispose();
                    return resource;
                }, lifetime));
                using var container = new WindsorContainer();
                var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
                var scope = lifetime == ServiceLifetime.Singleton ? null : root.CreateScope();
                owner = (object)scope ?? root;
                var failure = await Record.ExceptionAsync(async () => await Task.Run(() =>
                    (scope?.ServiceProvider ?? root).GetRequiredService<IResource>()).WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.IsType<ObjectDisposedException>(failure);
                Assert.Equal(1, resource.Disposals);
                if (resource is DualResource dual)
                {
                    Assert.Equal(1, dual.SyncCalls);
                }

                await ((IAsyncDisposable)root).DisposeAsync();
                Assert.Equal(1, resource.Disposals);
            }
        }

        public interface IResource
        {
            int Disposals { get; }
        }

        public sealed class DualResource : IResource, IDisposable, IAsyncDisposable
        {
            public int SyncCalls;

            public int Disposals { get; private set; }

            public void Dispose()
            {
                SyncCalls++;
                Disposals++;
            }

            public ValueTask DisposeAsync()
            {
                Disposals++;
                return default;
            }
        }

        public sealed class AsyncResource : IResource, IAsyncDisposable
        {
            public int Disposals { get; private set; }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Disposals++;
            }
        }
    }
}
