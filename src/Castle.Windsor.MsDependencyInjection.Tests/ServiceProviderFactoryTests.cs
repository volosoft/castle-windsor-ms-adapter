using System;
using Microsoft.Extensions.DependencyInjection;
using Castle.MicroKernel.Registration;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ServiceProviderFactoryTests
    {
        [Fact]
        public void A_Provider_Decorator_Receives_The_Previous_Provider()
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddTransient<IServiceProvider, ProviderDecorator>();
            var facade = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var decorated = Assert.IsType<ProviderDecorator>(facade.GetRequiredService<IServiceProvider>());
            Assert.IsType<ScopedWindsorServiceProvider>(decorated.Inner);
            using var scope = facade.CreateScope();
            Assert.IsType<ProviderDecorator>(scope.ServiceProvider);
            ((IDisposable)facade).Dispose();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Factory_Honors_An_Explicit_Provider_Override(bool descriptor)
        {
            var expected = new CustomProvider();
            var services = new ServiceCollection();
            using var container = new WindsorContainer();
            services.AddSingleton<IWindsorContainer>(container);
            if (descriptor)
            {
                services.AddSingleton<IServiceProvider>(expected);
            }
            else
            {
                container.Register(Component.For<IServiceProvider>().Instance(expected));
            }

            var factory = new WindsorServiceProviderFactory();
            var provider = factory.CreateServiceProvider(factory.CreateBuilder(services));
            Assert.Same(expected, provider);
            var facade = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            Assert.NotSame(expected, facade);
            ((IDisposable)facade).Dispose();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Factory_Creates_A_Disposable_Root_With_Stable_Scoped_Instances(bool keyed)
        {
            var services = new ServiceCollection();
            if (keyed)
            {
                services.AddKeyedScoped<Resource>("k");
            }
            else
            {
                services.AddScoped<Resource>();
            }

            var factory = new WindsorServiceProviderFactory();
            using var container = factory.CreateBuilder(services);
            var provider = factory.CreateServiceProvider(container);
            var disposable = Assert.IsAssignableFrom<IDisposable>(provider);
            Resource Resolve(IServiceProvider current) => keyed
                ? current.GetRequiredKeyedService<Resource>("k")
                : current.GetRequiredService<Resource>();

            var rootInstance = Resolve(provider);
            Assert.Same(rootInstance, Resolve(provider));
            Resource childInstance;
            using (var scope = provider.CreateScope())
            {
                childInstance = Resolve(scope.ServiceProvider);
                Assert.NotSame(rootInstance, childInstance);
                Assert.Same(childInstance, Resolve(scope.ServiceProvider));
            }
            Assert.Equal(1, childInstance.Disposals);
            Assert.Equal(0, rootInstance.Disposals);
            disposable.Dispose();
            Assert.Equal(1, rootInstance.Disposals);
            Assert.Equal(1, childInstance.Disposals);
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals { get; private set; }

            public void Dispose()
            {
                Disposals++;
            }
        }

        public sealed class CustomProvider : IServiceProvider
        {
            public object GetService(Type type)
            {
                return null;
            }
        }

        public sealed class ProviderDecorator : IServiceProvider
        {
            public IServiceProvider Inner { get; }

            public ProviderDecorator(IServiceProvider inner)
            {
                Inner = inner;
            }

            public object GetService(Type type)
            {
                return Inner.GetService(type);
            }
        }
    }
}
