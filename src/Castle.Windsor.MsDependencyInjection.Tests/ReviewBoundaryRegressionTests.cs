using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel;
using Castle.MicroKernel.Registration;
using Castle.MicroKernel.Lifestyle.Scoped;
using Microsoft.Extensions.DependencyInjection;
using ServiceDescriptor = Microsoft.Extensions.DependencyInjection.ServiceDescriptor;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ReviewBoundaryRegressionTests
    {
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
        public async Task Every_Shared_Activation_Closes_Before_Its_Own_Dependency(bool factoryEntry, bool nativeDependency, bool asynchronous, bool throwFromDependency)
        {
            foreach (var nativeOwner in new[] { false, true })
            {
                var events = new List<string>();
                var dependency = new Dependency(events, throwFromDependency);
                using var container = new WindsorContainer();
                var services = new ServiceCollection();
                if (nativeDependency)
                {
                    container.Register(Component.For<Dependency>().UsingFactoryMethod(() => dependency).LifestyleSingleton());
                }
                else
                {
                    services.AddSingleton(_ => dependency);
                }

                DependentResource shared = null;
                services.AddTransient(provider => shared ??= new DependentResource(provider.GetRequiredService<Dependency>(), events));
                if (nativeOwner)
                {
                    container.Register(Component.For<DependentOwner>().LifestyleSingleton());
                }
                else
                {
                    services.AddSingleton<DependentOwner>();
                }

                var root = Build(container, services, factoryEntry);
                root.GetRequiredService<DependentResource>();
                var owner = root.GetRequiredService<DependentOwner>();
                var error = asynchronous ? await Record.ExceptionAsync(() => ((IAsyncDisposable)root).DisposeAsync().AsTask())
                    : Record.Exception(() => ((IDisposable)root).Dispose());
                Assert.Same(throwFromDependency ? dependency.Error : null, error);
                Assert.False(owner.Premature);
                Assert.False(shared.Premature);
                Assert.Equal(new[] { "owner", "resource", "resource", "dependency" }, events);
                ((IDisposable)root).Dispose();
                Assert.Equal(4, events.Count);
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
        public void Independent_Custom_Scopes_Receive_Their_Transient_Instances(bool keyed, bool factoryRegistration, bool collection)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            if (keyed)
            {
                if (factoryRegistration)
                {
                    services.AddKeyedTransient<Resource>("k", (_, _) => new Resource());
                }
                else
                {
                    services.AddKeyedTransient<Resource>("k");
                }
            }
            else if (factoryRegistration)
            {
                services.AddTransient(_ => new Resource());
            }
            else
            {
                services.AddTransient<Resource>();
            }

            var root = Build(container, services, false);
            var custom = new CustomScope(container);
            Resource resource;
            using (MsLifetimeScope.Using(custom))
            {
                var provider = container.Resolve<IServiceProvider>();
                resource = keyed ? collection ? provider.GetKeyedServices<Resource>("k").Single() : provider.GetRequiredKeyedService<Resource>("k")
                    : collection ? provider.GetServices<Resource>().Single() : provider.GetRequiredService<Resource>();
            }
            custom.Dispose();
            Assert.Equal(1, resource.Disposals);
            Assert.Equal(1, custom.Notifications);
            ((IDisposable)root).Dispose();
            Assert.Equal(1, resource.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void A_Live_First_Scope_Does_Not_Capture_Independent_Factory_Scopes(bool factoryEntry, bool keyed)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            if (keyed)
            {
                services.AddKeyedScoped<Resource>("k");
            }
            else
            {
                services.AddScoped<Resource>();
            }

            var root = Build(container, services, factoryEntry);
            using var first = new WindsorServiceScope(container, container.Resolve<GlobalMsLifetimeScope>());
            first.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            using var second = root.CreateScope();
            Resource Resolve() => keyed ? second.ServiceProvider.GetRequiredKeyedService<Resource>("k") : second.ServiceProvider.GetRequiredService<Resource>();
            var resource = Resolve();
            first.Dispose();
            Assert.Equal(factoryEntry ? 0 : 1, resource.Disposals);
            if (factoryEntry)
            {
                Assert.Same(resource, Resolve());
            }
            else
            {
                Assert.Throws<ObjectDisposedException>(() => Resolve());
            }

            second.Dispose();
            ((IDisposable)root).Dispose();
            Assert.Equal(1, resource.Disposals);
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
        public async Task A_Previously_Resolved_Shared_Resource_Outlives_Its_Cached_Owner(bool factoryEntry, bool scoped, bool keyed, bool asynchronous)
        {
            foreach (var microsoft in new[] { true, false })
            {
                using var container = new WindsorContainer();
                var resource = new WarmResource();
                IServiceCollection services = new ServiceCollection();
                if (keyed)
                {
                    services.AddKeyedTransient<WarmResource>("k", (_, _) => resource);
                }
                else
                {
                    services.AddTransient(_ => resource);
                }

                var lifetime = scoped ? ServiceLifetime.Scoped : ServiceLifetime.Singleton;
                services.Add(keyed ? ServiceDescriptor.DescribeKeyed(typeof(WarmOwner), "owner", typeof(KeyedWarmOwner), lifetime)
                    : ServiceDescriptor.Describe(typeof(WarmOwner), typeof(WarmOwner), lifetime));
                var root = microsoft ? services.BuildServiceProvider() : Build(container, services, factoryEntry);
                if (keyed)
                {
                    root.GetRequiredKeyedService<WarmResource>("k");
                }
                else
                {
                    root.GetRequiredService<WarmResource>();
                }

                var owner = keyed ? root.GetRequiredKeyedService<WarmOwner>("owner") : root.GetRequiredService<WarmOwner>();
                if (asynchronous)
                {
                    await ((IAsyncDisposable)root).DisposeAsync();
                }
                else
                {
                    ((IDisposable)root).Dispose();
                }

                Assert.False(owner.Premature);
                Assert.Equal(new[] { "owner", "resource", "resource" }, resource.Events);
            }
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task Tracking_Callback_Can_Wait_For_Another_Activation_Of_The_Same_Instance(bool throwAfterNested)
        {
            var resource = new Resource();
            using var container = new WindsorContainer();
            var policy = new ReentrantSharedPolicy(container.Kernel.ReleasePolicy, resource, throwAfterNested);
            container.Kernel.ReleasePolicy = policy;
            var services = new ServiceCollection();
            services.AddTransient(_ => resource);
            var root = Build(container, services, false);
            policy.Resolve = () => root.GetRequiredService<Resource>();
            try
            {
                var error = Record.Exception(() => root.GetRequiredService<Resource>());
                Assert.Same(resource, await policy.Nested.WaitAsync(TimeSpan.FromSeconds(5)));
                Assert.True(policy.CompletedInsideTrack);
                if (throwAfterNested)
                {
                    Assert.Contains("Policy failure.", error?.ToString());
                }
                else
                {
                    Assert.Null(error);
                }
            }
            finally
            {
                ((IDisposable)root).Dispose();
            }
            Assert.Equal(throwAfterNested ? 1 : 2, resource.Disposals);
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
        public async Task Concurrent_Shared_Factory_Tracking_Is_Atomic(bool factoryEntry, bool child, bool keyed)
        {
            var resource = new Resource();
            using var container = new WindsorContainer();
            using var barrier = new Barrier(2);
            container.Kernel.ReleasePolicy = new CoordinatedPolicy(container.Kernel.ReleasePolicy, resource, barrier);
            var services = new ServiceCollection();
            if (keyed)
            {
                services.AddKeyedTransient<Resource>("k", (_, _) => resource);
            }
            else
            {
                services.AddTransient(_ => resource);
            }

            var root = Build(container, services, factoryEntry);
            var scope = child ? root.CreateScope() : null;
            var provider = scope?.ServiceProvider ?? root;
            try
            {
                var values = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Factory.StartNew(
                    () => keyed ? provider.GetKeyedServices<Resource>("k").Single() : provider.GetRequiredService<Resource>(),
                    CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default))).WaitAsync(TimeSpan.FromSeconds(10));
                Assert.All(values, value => Assert.Same(resource, value));
            }
            finally
            {
                scope?.Dispose();
                ((IDisposable)root).Dispose();
            }
            Assert.Equal(2, resource.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Closed_First_Scope_Preserves_The_Root_Creation_Entry(bool factoryEntry, bool warmInChild)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddTransient<Resource>();
            var root = Build(container, services, factoryEntry);
            if (warmInChild)
            {
                using var first = new WindsorServiceScope(container, container.Resolve<GlobalMsLifetimeScope>());
                first.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            }
            using var second = root.CreateScope();
            var resource = second.ServiceProvider.GetRequiredService<Resource>();
            ((IDisposable)root).Dispose();
            Assert.Equal(factoryEntry ? 0 : 1, resource.Disposals);
            second.Dispose();
            Assert.Equal(1, resource.Disposals);
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
        public async Task Opposite_Native_And_Ms_Factory_Chains_Keep_Dependencies_Alive(bool factoryEntry, bool asynchronous, bool warmNative)
        {
            var events = new List<string>();
            using var container = new WindsorContainer();
            container.Register(Component.For<NativeOwner>().UsingFactoryMethod(kernel =>
                new NativeOwner(kernel.Resolve<IServiceProvider>().GetRequiredService<MsResource>(), events)).LifestyleSingleton());
            container.Register(Component.For<NativeResource>().UsingFactoryMethod(() => new NativeResource(events)).LifestyleSingleton());
            var services = new ServiceCollection();
            services.AddSingleton(_ => new MsResource(events));
            services.AddSingleton(provider => new MsOwner(provider.GetRequiredService<NativeResource>(), events));
            var root = Build(container, services, factoryEntry);
            var msOwner = root.GetRequiredService<MsOwner>();
            root.GetRequiredService<MsResource>();
            var nativeOwner = root.GetRequiredService<NativeOwner>();
            if (warmNative)
            {
                root.GetRequiredService<NativeResource>();
            }

            if (asynchronous)
            {
                await ((IAsyncDisposable)root).DisposeAsync();
            }
            else
            {
                ((IDisposable)root).Dispose();
            }

            Assert.False(msOwner.Premature);
            Assert.False(nativeOwner.Premature);
            Assert.Equal(4, events.Count);
            Assert.True(events.IndexOf("ms-owner") < events.IndexOf("native-resource"));
            Assert.True(events.IndexOf("native-owner") < events.IndexOf("ms-resource"));
        }

        [Theory]
        [InlineData("microsoft")]
        [InlineData("helper")]
        [InlineData("factory")]
        [InlineData("lifetime")]
        public async Task Async_Close_Rejects_Resolution_Before_The_Worker_Runs(string entry)
        {
            using var entered = new ManualResetEventSlim();
            using var resume = new ManualResetEventSlim();
            var caller = Environment.CurrentManagedThreadId;
            var ambient = new AsyncLocal<int>(change =>
            {
                if (!change.ThreadContextChanged || change.CurrentValue != 42 || Environment.CurrentManagedThreadId == caller || resume.IsSet)
                {
                    return;
                }

                entered.Set();
                resume.Wait(TimeSpan.FromSeconds(10));
            });
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<Resource>();
            services.AddSingleton<AsyncResource>();
            var root = entry == "microsoft" ? services.BuildServiceProvider() : Build(container, services, entry == "factory");
            root.GetRequiredService<Resource>();
            var resource = root.GetRequiredService<AsyncResource>();
            ambient.Value = 42;
            var closing = entry == "lifetime" ? container.Resolve<GlobalMsLifetimeScope>().DisposeAsync() : ((IAsyncDisposable)root).DisposeAsync();
            ambient.Value = 0;
            try
            {
                if (entry != "microsoft")
                {
                    Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                }

                Assert.False(closing.IsCompleted);
                Assert.Throws<ObjectDisposedException>(() => root.GetRequiredService<Resource>());
            }
            finally
            {
                resume.Set();
                resource.Resume.TrySetResult();
                await closing;
            }
            Assert.Equal(1, resource.Disposals);
            Assert.Throws<ObjectDisposedException>(() => root.GetRequiredService<Resource>());
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

        public sealed class Resource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Interlocked.Increment(ref Disposals);
            }
        }

        public sealed class WarmResource : IDisposable, IAsyncDisposable
        {
            public readonly List<string> Events = new();

            public void Dispose()
            {
                Events.Add("resource");
            }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Dispose();
            }
        }

        public sealed class Dependency : IDisposable
        {
            private readonly List<string> _events;
            private readonly bool _throws;
            public readonly Exception Error = new ApplicationException("Dependency failure.");
            public bool Disposed;

            public Dependency(List<string> events, bool throws)
            {
                _events = events;
                _throws = throws;
            }

            public void Dispose()
            {
                Disposed = true;
                _events.Add("dependency");
                if (_throws)
                {
                    throw Error;
                }
            }
        }

        public sealed class DependentResource : IDisposable, IAsyncDisposable
        {
            private readonly Dependency _dependency;
            private readonly List<string> _events;
            public bool Premature;
            public bool Disposed;

            public List<string> Events
            {
                get
                {
                    return _events;
                }
            }

            public DependentResource(Dependency dependency, List<string> events)
            {
                _dependency = dependency;
                _events = events;
            }

            public void Dispose()
            {
                Premature |= _dependency.Disposed;
                Disposed = true;
                _events.Add("resource");
            }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Dispose();
            }
        }

        public sealed class DependentOwner : IDisposable
        {
            private readonly DependentResource _resource;
            public bool Premature;

            public DependentOwner(DependentResource resource)
            {
                _resource = resource;
            }

            public void Dispose()
            {
                Premature = _resource.Disposed;
                _resource.Events.Add("owner");
            }
        }

        public class WarmOwner : IDisposable, IAsyncDisposable
        {
            private readonly WarmResource _resource;
            public bool Premature;

            public WarmOwner(WarmResource resource)
            {
                _resource = resource;
            }

            public void Dispose()
            {
                Premature = _resource.Events.Count != 0;
                _resource.Events.Add("owner");
            }

            public async ValueTask DisposeAsync()
            {
                await Task.Yield();
                Dispose();
            }
        }

        public sealed class KeyedWarmOwner : WarmOwner
        {
            public KeyedWarmOwner([FromKeyedServices("k")] WarmResource resource) : base(resource)
            {
            }
        }

        private sealed class CustomScope : IMsLifetimeScope
        {
            private readonly IWindsorContainer _container;
            private readonly List<object> _instances = new();
            private readonly List<MsLifetimeScope> _children = new();
            public int Notifications;

            public ILifetimeScope WindsorLifeTimeScope { get; } = new DefaultLifetimeScope();

            public CustomScope(IWindsorContainer container)
            {
                _container = container;
            }

            public void AddInstance(object instance)
            {
                Notifications++;
                _instances.Add(instance);
            }

            public void AddChild(MsLifetimeScope child)
            {
                _children.Add(child);
            }

            public void RemoveChild(MsLifetimeScope child)
            {
                _children.Remove(child);
            }

            public void Dispose()
            {
                foreach (var child in _children.AsEnumerable().Reverse())
                {
                    child.Dispose();
                }

                foreach (var instance in _instances.AsEnumerable().Reverse())
                {
                    _container.Release(instance);
                }

                WindsorLifeTimeScope.Dispose();
            }
        }

        public sealed class AsyncResource : IAsyncDisposable
        {
            public readonly TaskCompletionSource Resume = new(TaskCreationOptions.RunContinuationsAsynchronously);
            public int Disposals;

            public async ValueTask DisposeAsync()
            {
                await Resume.Task;
                Disposals++;
            }
        }

        public sealed class NativeResource : IDisposable
        {
            private readonly List<string> _events;
            public bool Disposed;

            public NativeResource(List<string> events)
            {
                _events = events;
            }

            public void Dispose()
            {
                Disposed = true;
                _events.Add("native-resource");
            }
        }

        public sealed class MsResource : IDisposable
        {
            private readonly List<string> _events;
            public bool Disposed;

            public MsResource(List<string> events)
            {
                _events = events;
            }

            public void Dispose()
            {
                Disposed = true;
                _events.Add("ms-resource");
            }
        }

        public sealed class MsOwner : IDisposable
        {
            private readonly NativeResource _resource;
            private readonly List<string> _events;
            public bool Premature;

            public MsOwner(NativeResource resource, List<string> events)
            {
                _resource = resource;
                _events = events;
            }

            public void Dispose()
            {
                Premature = _resource.Disposed;
                _events.Add("ms-owner");
            }
        }

        public sealed class NativeOwner : IDisposable
        {
            private readonly MsResource _resource;
            private readonly List<string> _events;
            public bool Premature;

            public NativeOwner(MsResource resource, List<string> events)
            {
                _resource = resource;
                _events = events;
            }

            public void Dispose()
            {
                Premature = _resource.Disposed;
                _events.Add("native-owner");
            }
        }

        private sealed class CoordinatedPolicy : IReleasePolicy
        {
            private readonly IReleasePolicy _inner;
            private readonly object _target;
            private readonly Barrier _barrier;
            private int _checks;

            public CoordinatedPolicy(IReleasePolicy inner, object target, Barrier barrier)
            {
                _inner = inner;
                _target = target;
                _barrier = barrier;
            }

            public bool HasTrack(object instance)
            {
                var result = _inner.HasTrack(instance);
                if (ReferenceEquals(instance, _target) && Interlocked.Increment(ref _checks) <= 2 && !result)
                {
                    if (!_barrier.SignalAndWait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Concurrent checks did not meet.");
                    }
                }

                return result;
            }

            public void Track(object instance, Burden burden)
            {
                _inner.Track(instance, burden);
            }

            public void Release(object instance)
            {
                _inner.Release(instance);
            }

            public IReleasePolicy CreateSubPolicy()
            {
                return new CoordinatedPolicy(_inner.CreateSubPolicy(), _target, _barrier);
            }

            public void Dispose()
            {
                _inner.Dispose();
            }
        }

        private sealed class ReentrantSharedPolicy : IReleasePolicy
        {
            private readonly IReleasePolicy _inner;
            private readonly Resource _target;
            private readonly bool _throwAfterNested;
            public Func<Resource> Resolve;
            public Task<Resource> Nested;
            public bool CompletedInsideTrack;
            private int _entered;

            public ReentrantSharedPolicy(IReleasePolicy inner, Resource target, bool throwAfterNested)
            {
                _inner = inner;
                _target = target;
                _throwAfterNested = throwAfterNested;
            }

            public bool HasTrack(object instance)
            {
                return _inner.HasTrack(instance);
            }

            public void Track(object instance, Burden burden)
            {
                if (ReferenceEquals(instance, _target) && Interlocked.Exchange(ref _entered, 1) == 0)
                {
                    Nested = Task.Run(Resolve);
                    CompletedInsideTrack = Nested.Wait(TimeSpan.FromSeconds(2));
                    if (!CompletedInsideTrack)
                    {
                        throw new TimeoutException("Nested tracking did not complete.");
                    }

                    if (_throwAfterNested)
                    {
                        throw new ApplicationException("Policy failure.");
                    }
                }
                _inner.Track(instance, burden);
            }

            public void Release(object instance)
            {
                _inner.Release(instance);
            }

            public IReleasePolicy CreateSubPolicy()
            {
                return _inner.CreateSubPolicy();
            }

            public void Dispose()
            {
                _inner.Dispose();
            }
        }
    }
}
