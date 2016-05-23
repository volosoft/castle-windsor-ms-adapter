using System;
using Castle.MicroKernel.Registration;
using Castle.Windsor.MsDependencyInjection.Tests.TestClasses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class WindsorSpecificationTests : DependencyInjectionSpecificationTests, IDisposable
    {
        private readonly IWindsorContainer _container;
        private readonly DisposeCounter _disposeCounter;

        public WindsorSpecificationTests()
        {
            _container = new WindsorContainer();
            _container.Register(Component.For<DisposeCounter>().LifestyleSingleton());
            _disposeCounter = _container.Resolve<DisposeCounter>();
        }

        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            return WindsorRegistrationHelper.CreateServiceProvider(
                _container,
                serviceCollection
                );
        }

        [Fact]
        public void ResolvingFromScopeAndReleasingShouldWorkForWindsorTransients()
        {
            _container.Register(Component.For<MyTestClass1>().LifestyleTransient());

            var collection = new ServiceCollection();
            collection.AddScoped<MyTestClass2>();
            collection.AddTransient<MyTestClass3>();

            var serviceProvider = CreateServiceProvider(collection);
            var scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                var testObj1 = scope.ServiceProvider.GetService<MyTestClass1>();
                testObj1.IsDisposed.ShouldBeFalse();
                _container.Release(testObj1);
                testObj1.IsDisposed.ShouldBeTrue();

                _disposeCounter.Get<MyTestClass1>().ShouldBe(1);
                _disposeCounter.Get<MyTestClass2>().ShouldBe(0);
                _disposeCounter.Get<MyTestClass3>().ShouldBe(0);
            }

            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void ResolvingFromContainerShouldWork()
        {
            _container.Register(Component.For<MyTestClass1>().LifestyleTransient());

            var collection = new ServiceCollection();
            collection.AddScoped<MyTestClass2>();
            collection.AddTransient<MyTestClass3>();

            var serviceProvider = CreateServiceProvider(collection);

            var testObj1 = _container.Resolve<MyTestClass1>();
            testObj1.IsDisposed.ShouldBeFalse();
            _container.Release(testObj1);
            testObj1.IsDisposed.ShouldBeTrue();

            _disposeCounter.Get<MyTestClass1>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void ResolvingFromScopeShouldWorkForWindsorTransients()
        {
            _container.Register(Component.For<MyTestClass3>().LifestyleTransient());

            var collection = new ServiceCollection();
            collection.AddTransient<MyTestClass1>();
            collection.AddScoped<MyTestClass2>();

            var serviceProvider = CreateServiceProvider(collection);
            var scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                scope.ServiceProvider.GetService<MyTestClass1>();
            }

            _disposeCounter.Get<MyTestClass1>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        public void Dispose()
        {
            Assert.Null(MsLifetimeScope.Current);
        }
    }
}