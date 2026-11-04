using System;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class NativeProviderLifetimeTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void A_Native_Singleton_Provider_Outlives_The_Request_That_Created_It(bool registerFirst)
        {
            using var container = new WindsorContainer();
            if (registerFirst)
            {
                container.Register(Component.For<Owner>().LifestyleSingleton());
            }

            var services = new ServiceCollection();
            services.AddTransient<Resource>();
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            if (!registerFirst)
            {
                container.Register(Component.For<Owner>().LifestyleSingleton());
            }

            Owner owner;
            using (var scope = root.CreateScope())
            {
                owner = scope.ServiceProvider.GetRequiredService<Owner>();
            }

            Assert.Equal(0, owner.Resource.Disposals);
            var later = owner.Provider.GetRequiredService<Resource>();
            Assert.Equal(0, later.Disposals);
            ((IDisposable)root).Dispose();
            Assert.False(owner.DependencyWasDisposed);
            Assert.Equal(1, owner.Resource.Disposals);
            Assert.Equal(1, later.Disposals);
        }

        public sealed class Owner : IDisposable
        {
            public IServiceProvider Provider { get; }

            public Resource Resource { get; }

            public bool DependencyWasDisposed;

            public Owner(IServiceProvider provider)
            {
                Provider = provider;
                Resource = provider.GetRequiredService<Resource>();
            }

            public void Dispose()
            {
                DependencyWasDisposed = Resource.Disposals != 0;
            }
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Disposals++;
            }
        }
    }
}
