using System;
using System.Collections;
using System.Reflection;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class DisposalBoundaryTests
    {
        [Theory]
        [InlineData(false, "scope", false)]
        [InlineData(false, "scope", true)]
        [InlineData(false, "lifetime", false)]
        [InlineData(false, "lifetime", true)]
        [InlineData(false, "provider", false)]
        [InlineData(false, "provider", true)]
        [InlineData(false, "cache", false)]
        [InlineData(false, "cache", true)]
        [InlineData(true, "scope", false)]
        [InlineData(true, "scope", true)]
        [InlineData(true, "lifetime", false)]
        [InlineData(true, "lifetime", true)]
        [InlineData(true, "provider", false)]
        [InlineData(true, "provider", true)]
        [InlineData(true, "cache", false)]
        [InlineData(true, "cache", true)]
        public async Task Nested_Public_Close_Keeps_Its_Error_At_The_Call_Site(bool factoryEntry, string entry, bool asynchronous)
        {
            using var innerContainer = new WindsorContainer();
            var innerServices = new ServiceCollection();
            innerServices.AddScoped<ThrowingResource>();
            var innerRoot = Build(innerContainer, innerServices, factoryEntry);
            using var innerScope = (WindsorServiceScope)innerRoot.CreateScope();
            var innerProvider = entry == "provider" ? innerRoot : innerScope.ServiceProvider;
            var resource = innerProvider.GetRequiredService<ThrowingResource>();
            var closer = new NestedCloser(Close(entry, innerRoot, innerScope));
            using var outerContainer = new WindsorContainer();
            var outerServices = new ServiceCollection();
            outerServices.AddScoped<DualResource>();
            outerServices.AddScoped(_ => closer);
            var outerRoot = Build(outerContainer, outerServices, factoryEntry);
            using var outerScope = outerRoot.CreateScope();
            try
            {
                var survivor = outerScope.ServiceProvider.GetRequiredService<DualResource>();
                outerScope.ServiceProvider.GetRequiredService<NestedCloser>();
                var error = asynchronous
                    ? await Record.ExceptionAsync(() => ((IAsyncDisposable)outerScope).DisposeAsync().AsTask())
                    : Record.Exception(outerScope.Dispose);
                Assert.Null(error);
                Assert.Same(resource.Error, closer.Error);
                Assert.Equal(1, resource.Calls);
                Assert.Equal(asynchronous ? 0 : 1, survivor.SyncCalls);
                Assert.Equal(asynchronous ? 1 : 0, survivor.AsyncCalls);
                outerScope.Dispose();
                Assert.Equal(1, resource.Calls);
            }
            finally
            {
                ((IDisposable)outerRoot).Dispose();
                ((IDisposable)innerRoot).Dispose();
            }
        }

        [Theory]
        [InlineData(false, "scope", false)]
        [InlineData(false, "scope", true)]
        [InlineData(false, "lifetime", false)]
        [InlineData(false, "lifetime", true)]
        [InlineData(false, "provider", false)]
        [InlineData(false, "provider", true)]
        [InlineData(false, "cache", false)]
        [InlineData(false, "cache", true)]
        [InlineData(true, "scope", false)]
        [InlineData(true, "scope", true)]
        [InlineData(true, "lifetime", false)]
        [InlineData(true, "lifetime", true)]
        [InlineData(true, "provider", false)]
        [InlineData(true, "provider", true)]
        [InlineData(true, "cache", false)]
        [InlineData(true, "cache", true)]
        public async Task Nested_Public_Synchronous_Close_Does_Not_Inherit_Async_Mode(bool factoryEntry, string entry, bool asyncOnly)
        {
            using var innerContainer = new WindsorContainer();
            var innerServices = new ServiceCollection();
            innerServices.AddScoped<DualResource>();
            innerServices.AddScoped<AsyncResource>();
            var innerRoot = Build(innerContainer, innerServices, factoryEntry);
            using var innerScope = (WindsorServiceScope)innerRoot.CreateScope();
            var innerProvider = entry == "provider" ? innerRoot : innerScope.ServiceProvider;
            var dual = asyncOnly ? null : innerProvider.GetRequiredService<DualResource>();
            var asynchronous = asyncOnly ? innerProvider.GetRequiredService<AsyncResource>() : null;
            var closer = new NestedCloser(Close(entry, innerRoot, innerScope));
            using var outerContainer = new WindsorContainer();
            var outerServices = new ServiceCollection();
            outerServices.AddScoped<DualResource>();
            outerServices.AddScoped(_ => closer);
            var outerRoot = Build(outerContainer, outerServices, factoryEntry);
            using var outerScope = outerRoot.CreateScope();
            try
            {
                var survivor = outerScope.ServiceProvider.GetRequiredService<DualResource>();
                outerScope.ServiceProvider.GetRequiredService<NestedCloser>();
                await ((IAsyncDisposable)outerScope).DisposeAsync();
                if (asyncOnly)
                {
                    Assert.IsType<InvalidOperationException>(closer.Error);
                    Assert.Equal(0, asynchronous.Calls);
                }
                else
                {
                    Assert.Null(closer.Error);
                    Assert.Equal(1, dual.SyncCalls);
                    Assert.Equal(0, dual.AsyncCalls);
                }
                Assert.Equal(0, survivor.SyncCalls);
                Assert.Equal(1, survivor.AsyncCalls);
            }
            finally
            {
                ((IDisposable)outerRoot).Dispose();
                ((IDisposable)innerRoot).Dispose();
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Internal_Child_Closure_Still_Uses_The_Parents_Async_Mode(bool factoryEntry)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddScoped<AsyncResource>();
            var root = Build(container, services, factoryEntry);
            var parent = (WindsorServiceScope)root.CreateScope();
            var child = new WindsorServiceScope(container, parent.LifetimeScope);
            var resource = child.ServiceProvider.GetRequiredService<AsyncResource>();
            await parent.DisposeAsync();
            Assert.Equal(1, resource.Calls);
            Assert.Throws<ObjectDisposedException>(() => child.ServiceProvider.GetRequiredService<AsyncResource>());
            await child.DisposeAsync();
            await ((IAsyncDisposable)root).DisposeAsync();
            Assert.Equal(1, resource.Calls);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Empty_Scopes_Do_Not_Accumulate_Root_Infrastructure_Records(bool factoryEntry)
        {
            using var container = new WindsorContainer();
            var root = Build(container, new ServiceCollection(), factoryEntry);
            var lifetime = container.Resolve<GlobalMsLifetimeScope>();
            var field = typeof(MsLifetimeScope).GetField("_resolvedInstances", BindingFlags.Instance | BindingFlags.NonPublic);
            var records = (ICollection)field.GetValue(lifetime);
            for (var batch = 0; batch < 3; batch++)
            {
                for (var i = 0; i < 1000; i++)
                {
                    using var scope = root.CreateScope();
                }
                Assert.Empty(records);
            }
            container.Register(Component.For<NativeTransient>().LifestyleTransient(),
                Component.For<NativeSingleton>().LifestyleSingleton());
            NativeTransient transient;
            NativeSingleton singleton;
            using (var scope = root.CreateScope())
            {
                transient = scope.ServiceProvider.GetRequiredService<NativeTransient>();
                singleton = scope.ServiceProvider.GetRequiredService<NativeSingleton>();
                Assert.Equal(0, transient.Calls);
                Assert.Equal(0, singleton.Calls);
            }
            Assert.Equal(1, transient.Calls);
            Assert.Equal(0, singleton.Calls);
            ((IDisposable)root).Dispose();
            Assert.Equal(1, transient.Calls);
            Assert.Equal(1, singleton.Calls);
        }

        private static Action Close(string entry, IServiceProvider root, WindsorServiceScope scope)
        {
            return entry switch
            {
                "lifetime" => scope.LifetimeScope.Dispose,
                "provider" => ((IDisposable)root).Dispose,
                "cache" => scope.LifetimeScope.WindsorLifeTimeScope.Dispose,
                _ => scope.Dispose
            };
        }

        private static IServiceProvider Build(IWindsorContainer container, IServiceCollection services, bool factoryEntry)
        {
            if (!factoryEntry)
            {
                return WindsorRegistrationHelper.CreateServiceProvider(container, services);
            }

            services.AddSingleton(container);
            var factory = new WindsorServiceProviderFactory();
            return factory.CreateServiceProvider(factory.CreateBuilder(services));
        }

        public sealed class NestedCloser : IDisposable, IAsyncDisposable
        {
            private readonly Action _close;
            public Exception Error;

            public NestedCloser(Action close)
            {
                _close = close;
            }

            public void Dispose()
            {
                try
                {
                    _close();
                }
                catch (Exception error)
                {
                    Error = error;
                }
            }

            public ValueTask DisposeAsync()
            {
                Dispose();
                return default;
            }
        }

        public sealed class ThrowingResource : IDisposable
        {
            public readonly Exception Error = new ApplicationException("Inner cleanup failed.");
            public int Calls;

            public void Dispose()
            {
                Calls++;
                throw Error;
            }
        }

        public sealed class DualResource : IDisposable, IAsyncDisposable
        {
            public int SyncCalls;
            public int AsyncCalls;

            public void Dispose()
            {
                SyncCalls++;
            }

            public ValueTask DisposeAsync()
            {
                AsyncCalls++;
                return default;
            }
        }

        public sealed class AsyncResource : IAsyncDisposable
        {
            public int Calls;

            public ValueTask DisposeAsync()
            {
                Calls++;
                return default;
            }
        }

        public sealed class NativeTransient : IDisposable
        {
            public int Calls;

            public void Dispose()
            {
                Calls++;
            }
        }

        public sealed class NativeSingleton : IDisposable
        {
            public int Calls;

            public void Dispose()
            {
                Calls++;
            }
        }
    }
}
