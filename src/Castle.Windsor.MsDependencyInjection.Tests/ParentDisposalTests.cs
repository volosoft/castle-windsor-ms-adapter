using System;
using System.Collections.Generic;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ParentDisposalTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void A_Child_Native_Consumer_Is_Released_Before_The_Parent_Resource(bool warm, bool closeChildFirst)
        {
            using var parent = new WindsorContainer();
            using var child = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<Resource>();
            var parentProvider = WindsorRegistrationHelper.CreateServiceProvider(parent, services);
            var childProvider = WindsorRegistrationHelper.CreateServiceProvider(child, new ServiceCollection());
            parent.AddChildContainer(child);
            child.Register(Component.For<Consumer>().LifestyleSingleton());
            if (warm)
            {
                childProvider.GetServices<Resource>();
            }

            var owner = childProvider.GetRequiredService<Consumer>();
            var resource = parentProvider.GetRequiredService<Resource>();
            if (closeChildFirst)
            {
                ((IDisposable)childProvider).Dispose();
                Assert.Equal(0, resource.Disposals);
            }
            ((IDisposable)parentProvider).Dispose();
            Assert.False(owner.DependencyWasDisposed);
            Assert.Equal(1, resource.Disposals);
            Assert.Equal(1, owner.Disposals);
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Disposals++;
            }
        }

        public sealed class Consumer : IDisposable
        {
            private readonly IEnumerable<Resource> _resources;
            public bool DependencyWasDisposed;
            public int Disposals;

            public Consumer(IEnumerable<Resource> resources)
            {
                _resources = resources;
            }

            public void Dispose()
            {
                foreach (var resource in _resources)
                {
                    DependencyWasDisposed |= resource.Disposals != 0;
                }

                Disposals++;
            }
        }
    }
}
