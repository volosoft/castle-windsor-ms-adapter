using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class PublicWindsorScopeTests
    {
        [Fact]
        public void A_Native_Ms_Scope_Can_Be_Warmed_Before_Installation()
        {
            using var container = new WindsorContainer();
            container.Register(Castle.MicroKernel.Registration.Component.For<Resource>()
                .LifestyleCustom<MsScopedLifestyleManager>());
            var scope = new MsLifetimeScope(container);
            Resource resource;
            using (MsLifetimeScope.Using(scope))
            {
                resource = container.Resolve<Resource>();
            }

            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            scope.Dispose();
            Assert.Equal(1, resource.Disposals);
            ((IDisposable)provider).Dispose();
            Assert.Equal(1, resource.Disposals);
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Closing_The_Public_Cache_Releases_Only_Scoped_Instances(bool child)
        {
            var services = new ServiceCollection();
            services.AddSingleton<SingletonResource>();
            services.AddScoped<ScopedResource>();
            services.AddKeyedScoped<ScopedResource>("k");
            services.AddTransient<TransientResource>();
            using var container = new WindsorContainer();
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = root.CreateScope();
            var provider = child ? scope.ServiceProvider : root;
            var lifetime = child ? ((WindsorServiceScope)scope).LifetimeScope : container.Resolve<GlobalMsLifetimeScope>();
            var singleton = provider.GetRequiredService<SingletonResource>();
            var scoped = provider.GetRequiredService<ScopedResource>();
            var keyed = provider.GetKeyedServices<ScopedResource>("k");
            var transient = provider.GetRequiredService<TransientResource>();
            lifetime.WindsorLifeTimeScope.Dispose();
            Assert.Equal(1, scoped.Disposals);
            Assert.Equal(1, System.Linq.Enumerable.Single(keyed).Disposals);
            Assert.Equal(0, transient.Disposals);
            Assert.Equal(0, singleton.Disposals);
            Assert.Same(singleton, provider.GetRequiredService<SingletonResource>());
            Assert.NotNull(provider.GetRequiredService<TransientResource>());
            Assert.Throws<ObjectDisposedException>(() => provider.GetRequiredService<ScopedResource>());
            Assert.Throws<ObjectDisposedException>(() => provider.GetKeyedServices<ScopedResource>("k"));
            scope.Dispose();
            ((IDisposable)root).Dispose();
            Assert.Equal(1, scoped.Disposals);
            Assert.Equal(1, singleton.Disposals);
            Assert.Equal(1, transient.Disposals);
        }

        public class Resource : IDisposable
        {
            public int Disposals { get; private set; }

            public void Dispose()
            {
                Disposals++;
            }
        }

        public sealed class SingletonResource : Resource;

        public sealed class ScopedResource : Resource;

        public sealed class TransientResource : Resource;
    }
}
