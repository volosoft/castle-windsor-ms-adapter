using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class NativeAndScopeContractTests
    {
        [Theory]
        [InlineData("automatic", false, false)]
        [InlineData("automatic", false, true)]
        [InlineData("automatic", true, false)]
        [InlineData("automatic", true, true)]
        [InlineData("external", false, false)]
        [InlineData("external", false, true)]
        [InlineData("external", true, false)]
        [InlineData("external", true, true)]
        [InlineData("destroy", false, false)]
        [InlineData("destroy", false, true)]
        [InlineData("destroy", true, false)]
        [InlineData("destroy", true, true)]
        [InlineData("external-destroy", false, false)]
        [InlineData("external-destroy", false, true)]
        [InlineData("external-destroy", true, false)]
        [InlineData("external-destroy", true, true)]
        public async Task Native_Async_Resources_Keep_Their_Original_Ownership(string mode, bool factoryEntry, bool child)
        {
            foreach (var before in new[] { true, false })
            {
                using var container = new WindsorContainer();
                var resource = new NativeAsyncResource();
                var callbacks = 0;
                var callbackThread = 0;
                void Register()
                {
                    var registration = Component.For<NativeAsyncResource>()
                        .UsingFactoryMethod(() => resource, managedExternally: mode.StartsWith("external", StringComparison.Ordinal))
                        .LifestyleTransient();
                    if (mode.Contains("destroy", StringComparison.Ordinal))
                    {
                        registration.OnDestroy(value =>
                    {
                        callbacks++;
                        callbackThread = Environment.CurrentManagedThreadId;
                        value.DisposeAsync().AsTask().GetAwaiter().GetResult();
                    });
                    }

                    container.Register(registration);
                }
                if (before)
                {
                    Register();
                }

                var root = Build(container, new ServiceCollection(), factoryEntry);
                if (!before)
                {
                    Register();
                }

                var scope = child ? root.CreateScope() : null;
                var provider = scope?.ServiceProvider ?? root;
                try
                {
                    Assert.Same(resource, provider.GetRequiredService<NativeAsyncResource>());
                    var caller = Environment.CurrentManagedThreadId;
                    if (child)
                    {
                        await ((IAsyncDisposable)scope).DisposeAsync();
                    }
                    else
                    {
                        await ((IAsyncDisposable)root).DisposeAsync();
                    }

                    Assert.Equal(mode.StartsWith("external", StringComparison.Ordinal) ? 0 : 1, resource.Disposals);
                    Assert.Equal(mode == "destroy" ? 1 : 0, callbacks);
                    if (mode == "destroy")
                    {
                        Assert.NotEqual(caller, callbackThread);
                    }
                }
                finally
                {
                    if (scope != null)
                    {
                        await ((IAsyncDisposable)scope).DisposeAsync();
                    }

                    await ((IAsyncDisposable)root).DisposeAsync();
                }
                Assert.Equal(mode.StartsWith("external", StringComparison.Ordinal) ? 0 : 1, resource.Disposals);
                Assert.False(container.Kernel.ReleasePolicy.HasTrack(resource));
            }
        }

        [Theory]
        [InlineData(false, false, false, false)]
        [InlineData(false, false, false, true)]
        [InlineData(false, false, true, false)]
        [InlineData(false, false, true, true)]
        [InlineData(false, true, false, false)]
        [InlineData(false, true, false, true)]
        [InlineData(false, true, true, false)]
        [InlineData(false, true, true, true)]
        [InlineData(true, false, false, false)]
        [InlineData(true, false, false, true)]
        [InlineData(true, false, true, false)]
        [InlineData(true, false, true, true)]
        [InlineData(true, true, false, false)]
        [InlineData(true, true, false, true)]
        [InlineData(true, true, true, false)]
        [InlineData(true, true, true, true)]
        public async Task Shared_Child_Activations_Keep_The_Entire_Dependency_Chain_Alive(bool factoryEntry, bool keyed, bool asynchronous, bool dependency)
        {
            using var container = new WindsorContainer();
            var events = new List<string>();
            SharedResource shared = null;
            var services = new ServiceCollection();
            services.AddScoped(_ => new ChildDependency(events));
            SharedResource Create(IServiceProvider provider) => shared ??= new SharedResource(
                dependency ? provider.GetRequiredService<ChildDependency>() : null, events);
            if (keyed)
            {
                services.AddKeyedTransient<SharedResource>("k", (provider, _) => Create(provider));
                services.AddScoped<SharedOwner, KeyedOwner>();
            }
            else
            {
                services.AddTransient(Create);
                services.AddScoped<SharedOwner>();
            }
            var root = Build(container, services, factoryEntry);
            var scope = root.CreateScope();
            try
            {
                var first = keyed ? scope.ServiceProvider.GetRequiredKeyedService<SharedResource>("k")
                    : scope.ServiceProvider.GetRequiredService<SharedResource>();
                var owner = scope.ServiceProvider.GetRequiredService<SharedOwner>();
                Assert.Same(first, owner.Resource);
                if (asynchronous)
                {
                    await ((IAsyncDisposable)scope).DisposeAsync();
                }
                else
                {
                    scope.Dispose();
                }

                Assert.False(owner.Premature);
                Assert.False(first.Premature);
                Assert.Equal(dependency ? new[] { "owner", "resource", "resource", "dependency" }
                    : new[] { "owner", "resource", "resource" }, events);
                ((IDisposable)root).Dispose();
                Assert.Equal(dependency ? 4 : 3, events.Count);
            }
            finally
            {
                scope.Dispose();
                ((IDisposable)root).Dispose();
            }
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
        public void Scope_Validation_Uses_The_Real_Owner_Through_Cache_Decorators(bool factoryEntry, bool keyed, bool child)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            if (keyed)
            {
                services.AddKeyedScoped<ValidationResource>("k");
            }
            else
            {
                services.AddScoped<ValidationResource>();
            }

            var root = Build(container, services, factoryEntry, validateScopes: true);
            using var scope = (WindsorServiceScope)root.CreateScope();
            var owner = child ? scope.LifetimeScope : container.Resolve<GlobalMsLifetimeScope>();
            foreach (var decorateCache in new[] { false, true })
            {
                IServiceProvider provider;
                using (MsLifetimeScope.Using(new ForwardScope(owner, decorateCache)))
                {
                    provider = container.Resolve<IServiceProvider>();
                }

                ValidationResource Resolve() => keyed ? provider.GetRequiredKeyedService<ValidationResource>("k")
                                : provider.GetRequiredService<ValidationResource>();
                if (child)
                {
                    var value = Resolve();
                    Assert.Same(value, Resolve());
                    Assert.Equal(0, value.Disposals);
                }
                else
                {
                    Assert.Throws<InvalidOperationException>(() => Resolve());
                }
            }
            scope.Dispose();
            ((IDisposable)root).Dispose();
        }

        private static IServiceProvider Build(IWindsorContainer container, IServiceCollection services, bool factoryEntry, bool validateScopes = false)
        {
            if (!factoryEntry)
            {
                return WindsorRegistrationHelper.CreateServiceProvider(container, services, validateScopes);
            }

            services.AddSingleton(container);
            var factory = new WindsorServiceProviderFactory(validateScopes);
            return factory.CreateServiceProvider(factory.CreateBuilder(services));
        }

        public sealed class NativeAsyncResource : IAsyncDisposable
        {
            public int Disposals;

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Disposals++;
            }
        }

        public sealed class ChildDependency : IDisposable
        {
            private readonly List<string> _events;
            public bool Disposed;

            public ChildDependency(List<string> events)
            {
                _events = events;
            }

            public void Dispose()
            {
                Disposed = true;
                _events.Add("dependency");
            }
        }

        public sealed class SharedResource : IDisposable, IAsyncDisposable
        {
            private readonly ChildDependency _dependency;
            private readonly List<string> _events;
            public bool Disposed;
            public bool Premature;

            public List<string> Events
            {
                get
                {
                    return _events;
                }
            }

            public SharedResource(ChildDependency dependency, List<string> events)
            {
                _dependency = dependency;
                _events = events;
            }

            public void Dispose()
            {
                Premature |= _dependency?.Disposed == true;
                Disposed = true;
                _events.Add("resource");
            }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Dispose();
            }
        }

        public class SharedOwner : IDisposable, IAsyncDisposable
        {
            private readonly SharedResource _resource;

            public SharedResource Resource
            {
                get
                {
                    return _resource;
                }
            }

            public bool Premature;

            public SharedOwner(SharedResource resource)
            {
                _resource = resource;
            }

            public void Dispose()
            {
                Premature = _resource.Disposed;
                _resource.Events.Add("owner");
            }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Dispose();
            }
        }

        public sealed class KeyedOwner : SharedOwner
        {
            public KeyedOwner([FromKeyedServices("k")] SharedResource resource) : base(resource)
            {
            }
        }

        public sealed class ValidationResource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Disposals++;
            }
        }

        private sealed class ForwardScope : IMsLifetimeScope
        {
            private readonly IMsLifetimeScope _inner;

            public ILifetimeScope WindsorLifeTimeScope { get; }

            public ForwardScope(IMsLifetimeScope inner, bool decorateCache)
            {
                _inner = inner;
                WindsorLifeTimeScope = decorateCache ? new ForwardCache(inner.WindsorLifeTimeScope) : inner.WindsorLifeTimeScope;
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
