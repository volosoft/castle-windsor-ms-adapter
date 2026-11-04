using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ContainerHierarchyDisposalTests
    {
        [Theory]
        [InlineData(false, false, 0)]
        [InlineData(false, false, 1)]
        [InlineData(false, false, 2)]
        [InlineData(false, true, 0)]
        [InlineData(false, true, 1)]
        [InlineData(false, true, 2)]
        [InlineData(true, false, 0)]
        [InlineData(true, false, 1)]
        [InlineData(true, false, 2)]
        [InlineData(true, true, 0)]
        [InlineData(true, true, 1)]
        [InlineData(true, true, 2)]
        public async Task Closing_A_Parent_Releases_The_Whole_Hierarchy_Once(bool factory, bool factoryRoot, int closeMode)
        {
            using var parent = new WindsorContainer();
            using var child = new WindsorContainer();
            using var leaf = new WindsorContainer();
            var containers = new[] { parent, child, leaf };
            var providers = new List<IServiceProvider>();
            var roots = new List<TrackingRoot>();
            var resources = new List<Resource>();
            var released = new List<string>();
            foreach (var container in containers)
            {
                var registration = Component.For<GlobalMsLifetimeScope>();
                registration = factoryRoot
                    ? registration.UsingFactoryMethod(() => new TrackingRoot(container))
                    : registration.ImplementedBy<TrackingRoot>();
                container.Register(registration.LifestyleSingleton()
                    .OnDestroy(root => ((TrackingRoot)root).Notifications++));
                var name = providers.Count.ToString();
                var services = new ServiceCollection();
                services.AddSingleton<IWindsorContainer>(container);
                services.AddScoped(_ => new Resource(name, released));
                var providerFactory = new WindsorServiceProviderFactory();
                var provider = factory
                    ? providerFactory.CreateServiceProvider(providerFactory.CreateBuilder(services))
                    : WindsorRegistrationHelper.CreateServiceProvider(container, services);
                providers.Add(provider);
                roots.Add((TrackingRoot)container.Resolve<GlobalMsLifetimeScope>());
                resources.Add(provider.GetRequiredService<Resource>());
            }
            parent.AddChildContainer(child);
            child.AddChildContainer(leaf);

            if (closeMode == 0)
            {
                parent.Dispose();
            }
            else if (closeMode == 1)
            {
                ((IDisposable)providers[0]).Dispose();
            }
            else
            {
                await ((IAsyncDisposable)providers[0]).DisposeAsync();
            }

            Assert.Equal(new[] { "2", "1", "0" }, released);
            foreach (var root in roots)
            {
                Assert.Equal(1, root.Calls);
                Assert.Equal(1, root.Notifications);
            }
            foreach (var resource in resources)
            {
                Assert.Equal(closeMode == 2 ? 0 : 1, resource.SyncCalls);
                Assert.Equal(closeMode == 2 ? 1 : 0, resource.AsyncCalls);
            }
            foreach (var provider in providers)
            {
                ((IDisposable)provider).Dispose();
            }

            Assert.Equal(3, released.Count);
        }

        public sealed class TrackingRoot : GlobalMsLifetimeScope
        {
            public int Calls;
            public int Notifications;

            public TrackingRoot(IWindsorContainer container) : base(container)
            {
            }

            protected override void DisposeInternal()
            {
                Calls++;
                base.DisposeInternal();
            }
        }

        public sealed class Resource : IDisposable, IAsyncDisposable
        {
            private readonly string _name;
            private readonly List<string> _released;
            public int SyncCalls;
            public int AsyncCalls;

            public Resource(string name, List<string> released)
            {
                _name = name;
                _released = released;
            }

            public void Dispose()
            {
                SyncCalls++;
                _released.Add(_name);
            }

            public ValueTask DisposeAsync()
            {
                AsyncCalls++;
                _released.Add(_name);
                return default;
            }
        }
    }
}
