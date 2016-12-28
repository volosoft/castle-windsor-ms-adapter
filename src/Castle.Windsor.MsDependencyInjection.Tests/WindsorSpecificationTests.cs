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
        private DisposeCounter _disposeCounter;

        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            var windsorContainer = new WindsorContainer();

            windsorContainer.Register(Component.For<DisposeCounter>().LifestyleSingleton());

            _disposeCounter = windsorContainer.Resolve<DisposeCounter>();

            return WindsorRegistrationHelper.CreateServiceProvider(
                windsorContainer,
                serviceCollection
                );
        }

        [Fact]
        public void ResolvingFromScopeAndReleasingShouldWorkForWindsorTransients()
        {
            var collection = new ServiceCollection();
            collection.AddScoped<MyTestClass2>();
            collection.AddTransient<MyTestClass3>();

            var serviceProvider = CreateServiceProvider(collection);

            serviceProvider.GetService<IWindsorContainer>().Register(Component.For<MyTestClass1>().LifestyleTransient());

            var scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                var testObj1 = scope.ServiceProvider.GetService<MyTestClass1>();
                testObj1.IsDisposed.ShouldBeFalse();
                serviceProvider.GetService<IWindsorContainer>().Release(testObj1);
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
            var collection = new ServiceCollection();
            collection.AddScoped<MyTestClass2>();
            collection.AddTransient<MyTestClass3>();

            var serviceProvider = CreateServiceProvider(collection);

            serviceProvider.GetService<IWindsorContainer>().Register(Component.For<MyTestClass1>().LifestyleTransient());

            var testObj1 = serviceProvider.GetService<IWindsorContainer>().Resolve<MyTestClass1>();
            testObj1.IsDisposed.ShouldBeFalse();
            serviceProvider.GetService<IWindsorContainer>().Release(testObj1);
            testObj1.IsDisposed.ShouldBeTrue();

            _disposeCounter.Get<MyTestClass1>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void ResolvingFromScopeShouldWorkForWindsorTransients()
        {

            var collection = new ServiceCollection();
            collection.AddTransient<MyTestClass1>();
            collection.AddScoped<MyTestClass2>();

            var serviceProvider = CreateServiceProvider(collection);

            serviceProvider.GetService<IWindsorContainer>().Register(Component.For<MyTestClass3>().LifestyleTransient());

            var scopeFactory = serviceProvider.GetService<IServiceScopeFactory>();
            using (var scope = scopeFactory.CreateScope())
            {
                scope.ServiceProvider.GetService<MyTestClass1>();
            }

            _disposeCounter.Get<MyTestClass1>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void Should_Resolve_Same_Object_In_Same_Scope_For_Scoped_Lifestyle()
        {
            var collection = new ServiceCollection();
            collection.AddScoped<MyTestClass3>();
            var serviceProvider = CreateServiceProvider(collection);

            using (var scope = serviceProvider.CreateScope())
            {
                var obj1 = scope.ServiceProvider.GetRequiredService<MyTestClass3>();
                var obj2 = scope.ServiceProvider.GetRequiredService<MyTestClass3>();
                obj1.ShouldBeSameAs(obj2);
            }
        }

        public void Dispose()
        {
            Assert.Null(MsLifetimeScope.Current);
        }
    }
}