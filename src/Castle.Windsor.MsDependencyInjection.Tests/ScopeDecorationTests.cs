using System;
using System.Threading.Tasks;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ScopeDecorationTests
    {
        [Fact]
        public void A_Transparent_Scope_Owns_Repeated_Factory_Activations()
        {
            using var container = new WindsorContainer();
            var instance = new Resource();
            var services = new ServiceCollection();
            services.AddTransient(_ => instance);
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var child = (WindsorServiceScope)root.CreateScope();
            using (MsLifetimeScope.Using(new ForwardScope(child.LifetimeScope)))
            {
                var provider = container.Resolve<IServiceProvider>();
                Assert.Same(instance, provider.GetRequiredService<Resource>());
                Assert.Same(instance, provider.GetRequiredService<Resource>());
            }
            child.Dispose();
            Assert.Equal(2, instance.Disposals);
            ((IDisposable)root).Dispose();
            Assert.Equal(2, instance.Disposals);
        }

        [Fact]
        public void A_Transparent_Scope_Owns_Transient_Resources()
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddTransient<Resource>();
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var child = (WindsorServiceScope)root.CreateScope();
            Resource resource;
            using (MsLifetimeScope.Using(new ForwardScope(child.LifetimeScope)))
            {
                resource = container.Resolve<IServiceProvider>().GetRequiredService<Resource>();
            }

            child.Dispose();
            Assert.Equal(1, resource.Disposals);
            ((IDisposable)root).Dispose();
            Assert.Equal(1, resource.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Transparent_Scopes_Share_The_Original_Cache(bool rootScope, bool asynchronous)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddScoped<Resource>();
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var child = (WindsorServiceScope)provider.CreateScope();
            var actual = rootScope ? container.Resolve<GlobalMsLifetimeScope>() : child.LifetimeScope;
            var scopedProvider = rootScope ? provider : child.ServiceProvider;
            var wrapped = new ForwardScope(actual);
            Resource first;
            using (MsLifetimeScope.Using(wrapped))
            {
                first = container.Resolve<Resource>();
            }

            Assert.Same(first, scopedProvider.GetRequiredService<Resource>());
            var model = container.Kernel.GetHandler(typeof(Resource)).ComponentModel;
            Assert.Same(first, wrapped.WindsorLifeTimeScope.GetCachedInstance(model,
                _ => throw new InvalidOperationException("The shared entry was absent.")).Instance);
            if (asynchronous)
            {
                await child.DisposeAsync();
                await ((IAsyncDisposable)provider).DisposeAsync();
            }
            else
            {
                child.Dispose();
                ((IDisposable)provider).Dispose();
            }
            Assert.Equal(1, first.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Failed_Track_Does_Not_Publish_Or_Dispose_Twice(bool releaseBeforeThrow, bool asynchronous)
        {
            var state = new HookState { ReleaseBeforeThrow = releaseBeforeThrow };
            using var container = new WindsorContainer();
            container.Register(Component.For<HookState>().Instance(state),
                Component.For<Resource>().LifestyleCustom<ThrowingScoped>());
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            var actual = container.Resolve<GlobalMsLifetimeScope>();
            var wrapped = new ForwardScope(actual);
            Resource success;
            using (MsLifetimeScope.Using(wrapped))
            {
                Assert.Throws<HookFailure>(() => container.Resolve<Resource>());
                success = container.Resolve<Resource>();
                Assert.Same(success, container.Resolve<Resource>());
            }
            Assert.Equal(1, state.Failed.Disposals);
            if (asynchronous)
            {
                await ((IAsyncDisposable)provider).DisposeAsync();
            }
            else
            {
                ((IDisposable)provider).Dispose();
            }

            Assert.Equal(1, state.Failed.Disposals);
            Assert.Equal(1, success.Disposals);
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals { get; private set; }

            public void Dispose()
            {
                Disposals++;
            }
        }

        public sealed class HookState
        {
            public bool ReleaseBeforeThrow;
            public Resource Failed;
        }

        public sealed class HookFailure : Exception;

        public sealed class ThrowingScoped : MsScopedLifestyleManager
        {
            protected override void Track(Burden burden, IReleasePolicy policy)
            {
                base.Track(burden, policy);
                var state = Kernel.Resolve<HookState>();
                if (state.Failed != null)
                {
                    return;
                }

                state.Failed = (Resource)burden.Instance;
                if (state.ReleaseBeforeThrow)
                {
                    burden.Release();
                }

                throw new HookFailure();
            }
        }

        private sealed class ForwardScope : IMsLifetimeScope
        {
            private readonly IMsLifetimeScope _inner;

            public ILifetimeScope WindsorLifeTimeScope { get; }

            public ForwardScope(IMsLifetimeScope inner)
            {
                _inner = inner;
                WindsorLifeTimeScope = new ForwardCache(inner.WindsorLifeTimeScope);
            }

            public void AddInstance(object instance)
            {
                _inner.AddInstance(instance);
            }

            public void AddChild(MsLifetimeScope child)
            {
                _inner.AddChild(child);
            }

            public void RemoveChild(MsLifetimeScope child)
            {
                _inner.RemoveChild(child);
            }

            public void Dispose()
            {
                _inner.Dispose();
            }
        }

        private sealed class ForwardCache : ILifetimeScope
        {
            private readonly ILifetimeScope _inner;

            public ForwardCache(ILifetimeScope inner)
            {
                _inner = inner;
            }

            public Burden GetCachedInstance(ComponentModel model, ScopedInstanceActivationCallback create)
            {
                return _inner.GetCachedInstance(model, create);
            }

            public void Dispose()
            {
                _inner.Dispose();
            }
        }
    }
}
