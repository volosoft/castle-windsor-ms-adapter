using System;
using System.Collections.Generic;
using Castle.MicroKernel.Registration;
using Castle.MicroKernel.Lifestyle;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    public class NativeSingletonDisposalTests
    {
        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public void Cached_Inner_Remains_Alive_Until_Outer_Is_Disposed(bool keyed, bool disposeContainer, bool warm)
        {
            using var container = new WindsorContainer();
            var events = new List<string>();
            if (keyed)
            {
                container.Register(Component.For<NativeConsumer>().ImplementedBy<KeyedConsumer>().LifestyleSingleton());
            }
            else
            {
                container.Register(Component.For<NativeConsumer>().LifestyleSingleton());
            }

            container.Register(Component.For<OuterConsumer>().LifestyleSingleton());
            var services = new ServiceCollection();
            services.AddSingleton(new EventLog(events));
            if (keyed)
            {
                services.AddKeyedSingleton<Resource>("k");
            }
            else
            {
                services.AddSingleton<Resource>();
            }

            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            if (warm)
            {
                provider.GetRequiredService<NativeConsumer>();
            }

            var outer = provider.GetRequiredService<OuterConsumer>();
            if (disposeContainer)
            {
                container.Dispose();
            }
            else
            {
                ((IDisposable)provider).Dispose();
            }

            Assert.False(outer.DependencyWasDisposed);
            Assert.False(outer.Inner.DependencyWasDisposed);
            Assert.Equal(new[] { "outer", "consumer", "resource" }, events);
            Assert.Equal(1, outer.Inner.Disposals);
            Assert.Equal(1, outer.Inner.Resource.Disposals);
        }

        [Theory]
        [InlineData("perthread", false, false)]
        [InlineData("perthread", false, true)]
        [InlineData("perthread", true, false)]
        [InlineData("perthread", true, true)]
        [InlineData("custom", false, false)]
        [InlineData("custom", false, true)]
        [InlineData("custom", true, false)]
        [InlineData("custom", true, true)]
        [InlineData("pooled", false, false)]
        [InlineData("pooled", false, true)]
        [InlineData("pooled", true, false)]
        [InlineData("pooled", true, true)]
        public void Native_Cached_Lifestyles_Preserve_Dependency_Disposal_Order(string lifestyle, bool keyed, bool disposeContainer)
        {
            using var container = new WindsorContainer();
            var registration = keyed ? Component.For<NativeConsumer>().ImplementedBy<KeyedConsumer>() : Component.For<NativeConsumer>();
            if (lifestyle == "perthread")
            {
                registration.LifestylePerThread();
            }
            else if (lifestyle == "custom")
            {
                registration.LifestyleCustom<CustomSingleton>();
            }
            else
            {
                registration.LifestylePooled(1, 1);
            }

            container.Register(registration);
            var events = new List<string>();
            var services = new ServiceCollection();
            services.AddSingleton(new EventLog(events));
            if (keyed)
            {
                services.AddKeyedSingleton<Resource>("k");
            }
            else
            {
                services.AddSingleton<Resource>();
            }

            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var consumer = provider.GetRequiredService<NativeConsumer>();
            if (disposeContainer)
            {
                container.Dispose();
            }
            else
            {
                ((IDisposable)provider).Dispose();
            }

            Assert.False(consumer.DependencyWasDisposed);
            Assert.Equal(1, consumer.Disposals);
            Assert.Equal(1, consumer.Resource.Disposals);
            Assert.Equal(new[] { "consumer", "resource" }, events);
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        public void Native_Consumer_Is_Disposed_Before_Adapter_Dependency(bool keyed, bool disposeContainer, bool registerAfterAdapter)
        {
            var events = new List<string>();
            using var container = new WindsorContainer();
            void RegisterConsumer()
            {
                if (keyed)
                {
                    container.Register(Component.For<KeyedConsumer>().LifestyleSingleton());
                }
                else
                {
                    container.Register(Component.For<NativeConsumer>().LifestyleSingleton());
                }
            }
            if (!registerAfterAdapter)
            {
                RegisterConsumer();
            }

            var services = new ServiceCollection();
            services.AddSingleton(new EventLog(events));
            if (keyed)
            {
                services.AddKeyedSingleton<Resource>("k");
            }
            else
            {
                services.AddSingleton<Resource>();
            }

            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            if (registerAfterAdapter)
            {
                RegisterConsumer();
            }

            var consumer = keyed ? provider.GetRequiredService<KeyedConsumer>() : provider.GetRequiredService<NativeConsumer>();
            if (disposeContainer)
            {
                container.Dispose();
            }
            else
            {
                ((IDisposable)provider).Dispose();
            }

            Assert.False(consumer.DependencyWasDisposed);
            Assert.Equal(1, consumer.Disposals);
            Assert.Equal(1, consumer.Resource.Disposals);
            Assert.Equal(new[] { "consumer", "resource" }, events);
        }

        [Fact]
        public void Native_Consumer_And_Adapter_Singleton_Keep_A_Root_Scoped_Dependency_Alive()
        {
            var events = new List<string>();
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton(new EventLog(events));
            services.AddScoped<Resource>();
            services.AddSingleton<ScopedDependencyOwner>();
            container.Register(Component.For<ScopedNativeConsumer>().LifestyleSingleton());
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scoped = provider.GetRequiredService<Resource>();
            var consumer = provider.GetRequiredService<ScopedNativeConsumer>();
            Assert.Same(scoped, consumer.Owner.Resource);
            ((IDisposable)provider).Dispose();
            Assert.False(consumer.DependencyWasDisposed);
            Assert.False(consumer.Owner.DependencyWasDisposed);
            Assert.Equal(new[] { "native", "adapter", "resource" }, events);
            Assert.Equal(1, scoped.Disposals);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Native_Factory_Consumer_Keeps_Adapter_Factory_Dependency_Alive(bool keyed, bool disposeContainer)
        {
            var events = new List<string>();
            using var container = new WindsorContainer();
            container.Register(Component.For<NativeConsumer>().UsingFactoryMethod(kernel =>
            {
                var provider = kernel.Resolve<IServiceProvider>();
                var resource = keyed ? provider.GetRequiredKeyedService<Resource>("k") : provider.GetRequiredService<Resource>();
                return new NativeConsumer(resource, provider.GetRequiredService<EventLog>());
            }).LifestyleSingleton());
            var services = new ServiceCollection();
            services.AddSingleton(new EventLog(events));
            if (keyed)
            {
                services.AddKeyedSingleton<Resource>("k", (provider, _) => new Resource(provider.GetRequiredService<EventLog>()));
            }
            else
            {
                services.AddSingleton(provider => new Resource(provider.GetRequiredService<EventLog>()));
            }

            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var consumer = root.GetRequiredService<NativeConsumer>();
            if (disposeContainer)
            {
                container.Dispose();
            }
            else
            {
                ((IDisposable)root).Dispose();
            }

            Assert.False(consumer.DependencyWasDisposed);
            Assert.Equal(1, consumer.Disposals);
            Assert.Equal(1, consumer.Resource.Disposals);
            Assert.Equal(new[] { "consumer", "resource" }, events);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Closed_Generic_Native_Consumers_Are_Released_Individually(bool disposeContainer)
        {
            using var container = new WindsorContainer();
            container.Register(Component.For(typeof(GenericConsumer<>)).ImplementedBy(typeof(GenericConsumer<>)).LifestyleSingleton());
            var services = new ServiceCollection();
            services.AddSingleton(typeof(GenericResource<>));
            services.AddSingleton<AdapterGenericOwner>();
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var first = provider.GetRequiredService<GenericConsumer<int>>();
            var adapter = provider.GetRequiredService<AdapterGenericOwner>();
            var second = provider.GetRequiredService<GenericConsumer<string>>();
            if (disposeContainer)
            {
                container.Dispose();
            }
            else
            {
                ((IDisposable)provider).Dispose();
            }

            Assert.False(adapter.DependencyWasDisposed);
            Assert.False(first.DependencyWasDisposed);
            Assert.False(second.DependencyWasDisposed);
            Assert.Equal(1, first.Disposals);
            Assert.Equal(1, second.Disposals);
            Assert.Equal(1, first.Resource.Disposals);
            Assert.Equal(1, second.Resource.Disposals);
        }

        public sealed class EventLog
        {
            public List<string> Events { get; }

            public EventLog(List<string> events)
            {
                Events = events;
            }
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals { get; private set; }

            public EventLog Log { get; }

            public Resource(EventLog log)
            {
                Log = log;
            }

            public void Dispose()
            {
                Disposals++;
                Log.Events.Add("resource");
            }
        }

        public class NativeConsumer : IDisposable
        {
            private readonly EventLog _log;

            public Resource Resource { get; }

            public bool DependencyWasDisposed { get; private set; }

            public int Disposals { get; private set; }

            public NativeConsumer(Resource resource, EventLog log)
            {
                _log = log;
                Resource = resource;
            }

            public void Dispose()
            {
                DependencyWasDisposed = Resource.Disposals != 0;
                Disposals++;
                _log.Events.Add("consumer");
            }
        }

        public sealed class KeyedConsumer : NativeConsumer
        {
            public KeyedConsumer([FromKeyedServices("k")] Resource resource, EventLog log) : base(resource, log)
            {
            }
        }

        public sealed class ScopedDependencyOwner : IDisposable
        {
            public Resource Resource { get; }

            public bool DependencyWasDisposed { get; private set; }

            public ScopedDependencyOwner(Resource resource)
            {
                Resource = resource;
            }

            public void Dispose()
            {
                DependencyWasDisposed = Resource.Disposals != 0;
                Resource.Log.Events.Add("adapter");
            }
        }

        public sealed class ScopedNativeConsumer : IDisposable
        {
            public ScopedDependencyOwner Owner { get; }

            public bool DependencyWasDisposed { get; private set; }

            public ScopedNativeConsumer(ScopedDependencyOwner owner)
            {
                Owner = owner;
            }

            public void Dispose()
            {
                DependencyWasDisposed = Owner.Resource.Disposals != 0;
                Owner.Resource.Log.Events.Add("native");
            }
        }

        public sealed class GenericResource<T> : IDisposable
        {
            public int Disposals { get; private set; }

            public void Dispose()
            {
                Disposals++;
            }
        }

        public sealed class GenericConsumer<T> : IDisposable
        {
            public GenericResource<T> Resource { get; }

            public bool DependencyWasDisposed { get; private set; }

            public int Disposals { get; private set; }

            public GenericConsumer(GenericResource<T> resource)
            {
                Resource = resource;
            }

            public void Dispose()
            {
                DependencyWasDisposed = Resource.Disposals != 0;
                Disposals++;
            }
        }

        public sealed class AdapterGenericOwner : IDisposable
        {
            private readonly GenericConsumer<int> _consumer;

            public bool DependencyWasDisposed { get; private set; }

            public AdapterGenericOwner(GenericConsumer<int> consumer)
            {
                _consumer = consumer;
            }

            public void Dispose()
            {
                DependencyWasDisposed = _consumer.Disposals != 0;
            }
        }

        public sealed class CustomSingleton : SingletonLifestyleManager;

        public sealed class OuterConsumer : IDisposable
        {
            private readonly EventLog _log;

            public NativeConsumer Inner { get; }

            public bool DependencyWasDisposed { get; private set; }

            public OuterConsumer(NativeConsumer inner, EventLog log)
            {
                _log = log;
                Inner = inner;
            }

            public void Dispose()
            {
                DependencyWasDisposed = Inner.Disposals != 0;
                _log.Events.Add("outer");
            }
        }
    }
}
