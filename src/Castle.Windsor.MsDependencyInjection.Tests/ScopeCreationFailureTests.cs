using System;
using System.Threading.Tasks;
using Castle.MicroKernel.ComponentActivator;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ScopeCreationFailureTests
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
        public async Task Provider_Creation_Failure_Releases_The_Unreturned_Scope(bool factoryEntry, bool asyncOnly, bool cleanupFails)
        {
            var state = new State { CleanupFails = cleanupFails };
            var services = new ServiceCollection();
            services.AddSingleton(state);
            if (asyncOnly)
            {
                services.AddScoped<IResource, AsyncResource>();
            }
            else
            {
                services.AddScoped<IResource, SyncResource>();
            }

            using var container = new WindsorContainer();
            services.AddSingleton<IWindsorContainer>(container);
            var factory = new WindsorServiceProviderFactory();
            var root = factoryEntry ? factory.CreateServiceProvider(factory.CreateBuilder(services))
                : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            container.AddServices(new ServiceCollection().AddTransient<IServiceProvider, FailingProvider>());
            try
            {
                var failure = Assert.Throws<ComponentActivatorException>(() => root.CreateScope());
                Assert.Same(state.Error, failure.InnerException);
                Assert.NotNull(state.Created);
                Assert.Equal(1, state.Created.Disposals);
                var failedResource = state.Created;
                state.FailCreation = false;
                var retry = root.CreateScope();
                var retryResource = state.Created;
                Assert.NotSame(failedResource, retryResource);
                Assert.Equal(0, retryResource.Disposals);
                var cleanup = await Record.ExceptionAsync(() => ((IAsyncDisposable)retry).DisposeAsync().AsTask());
                if (cleanupFails)
                {
                    Assert.IsType<ApplicationException>(cleanup);
                }
                else
                {
                    Assert.Null(cleanup);
                }

                Assert.Equal(1, failedResource.Disposals);
                Assert.Equal(1, retryResource.Disposals);
            }
            finally
            {
                await Record.ExceptionAsync(() => ((IAsyncDisposable)root).DisposeAsync().AsTask());
            }
            Assert.Equal(1, state.Created.Disposals);
        }

        public sealed class State
        {
            public readonly Exception Error = new ApplicationException("Provider creation failed.");
            public bool CleanupFails;
            public bool FailCreation = true;
            public IResource Created;
        }

        public interface IResource
        {
            int Disposals { get; }
        }

        public sealed class SyncResource : IResource, IDisposable
        {
            private readonly State _state;

            public int Disposals { get; private set; }

            public SyncResource(State state)
            {
                _state = state;
                state.Created = this;
            }

            public void Dispose()
            {
                Disposals++;
                if (_state.CleanupFails)
                {
                    throw new ApplicationException("Cleanup failed.");
                }
            }
        }

        public sealed class AsyncResource : IResource, IAsyncDisposable
        {
            private readonly State _state;

            public int Disposals { get; private set; }

            public AsyncResource(State state)
            {
                _state = state;
                state.Created = this;
            }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Disposals++;
                if (_state.CleanupFails)
                {
                    throw new ApplicationException("Cleanup failed.");
                }
            }
        }

        public sealed class FailingProvider : IServiceProvider
        {
            private readonly IServiceProvider _inner;

            public FailingProvider(State state, IResource resource, IServiceProvider inner)
            {
                if (state.FailCreation)
                {
                    throw state.Error;
                }

                _inner = inner;
            }

            public object GetService(Type type)
            {
                return _inner.GetService(type);
            }
        }
    }
}
