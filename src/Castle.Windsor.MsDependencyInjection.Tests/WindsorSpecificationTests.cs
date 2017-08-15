using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Castle.Windsor.MsDependencyInjection.Tests.TestClasses;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection.Specification;
using Microsoft.Extensions.DependencyInjection.Specification.Fakes;
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

        [Fact]
        public void ResolvingAndDisposingWithIInterceptorShouldWork()
        {
            var collection = new ServiceCollection();

            collection.AddScoped<MyTestClass1>();

            var serviceProvider = CreateServiceProvider(collection);
            var windsorContainer = serviceProvider.GetService<IWindsorContainer>();

            windsorContainer.Register(Component.For<MyTestInterceptor>().LifestyleTransient());

            windsorContainer.Register(Component.For<MyTestClass2>().LifestyleTransient());

            windsorContainer.Register(
                Component
                    .For<MyTestClass3>()
                    .Interceptors<MyTestInterceptor>()
                    .LifestyleTransient()
            );

            using (var scope = serviceProvider.CreateScope())
            {
                var test1 = scope.ServiceProvider.GetService<MyTestClass1>();

                _disposeCounter.Get<MyTestClass1>().ShouldBe(0);
                _disposeCounter.Get<MyTestClass2>().ShouldBe(0);
                _disposeCounter.Get<MyTestClass3>().ShouldBe(0);
            }

            _disposeCounter.Get<MyTestClass1>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void ResolvingAndDisposingWithIInterceptorShouldWorkForTransient()
        {
            var collection = new ServiceCollection();

            var serviceProvider = CreateServiceProvider(collection);
            var windsorContainer = serviceProvider.GetService<IWindsorContainer>();

            windsorContainer.Register(
                Component.For<MyTestInterceptor>().LifestyleTransient(),
                Component.For<MyTestClass2>().LifestyleTransient(),
                Component.For<MyTestClass3>().Interceptors<MyTestInterceptor>().LifestyleTransient()
            );

            using (var scope = serviceProvider.CreateScope())
            {
                var test1 = scope.ServiceProvider.GetService<MyTestClass2>();

                _disposeCounter.Get<MyTestClass2>().ShouldBe(0);
                _disposeCounter.Get<MyTestClass3>().ShouldBe(0);
            }

            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void ResolvingAndDisposingWithIInterceptorShouldWorkForTransientDirectlyFromContainer()
        {
            var collection = new ServiceCollection();

            var serviceProvider = CreateServiceProvider(collection);
            var windsorContainer = serviceProvider.GetService<IWindsorContainer>();

            windsorContainer.Register(
                Component.For<MyTestInterceptor>().LifestyleTransient(),
                Component.For<MyTestClass2>().LifestyleTransient(),
                Component.For<MyTestClass3>().Interceptors<MyTestInterceptor>().LifestyleTransient()
            );

            var test1 = windsorContainer.Resolve<MyTestClass2>();
            _disposeCounter.Get<MyTestClass2>().ShouldBe(0);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(0);

            windsorContainer.Release(test1);
            _disposeCounter.Get<MyTestClass2>().ShouldBe(1);
            _disposeCounter.Get<MyTestClass3>().ShouldBe(1);
        }

        [Fact]
        public void Windsor_Dispose_Test_With_Interceptor()
        {
            var windsorContainer = new WindsorContainer();
            windsorContainer.Register(
                Component.For<DisposeCounter>().LifestyleSingleton(),
                Component.For<MyTestInterceptor>().LifestyleTransient(),
                Component.For<MyTestClass2>().LifestyleTransient(),
                Component.For<MyTestClass3>().Interceptors<MyTestInterceptor>().LifestyleTransient()
            );

            var obj = windsorContainer.Resolve<MyTestClass2>();
            windsorContainer.Release(obj);
        }

        [Fact]
        public void Should_Resolve_Registered_Enumerable()
        {
            var collection = new ServiceCollection();

            collection.AddSingleton((IEnumerable<MyTestClass3>)new List<MyTestClass3>
            {
                new MyTestClass3(),
                new MyTestClass3(),
                new MyTestClass3()
            });

            collection.AddTransient<MyClassInjectsEnumerable>();

            var serviceProvider = CreateServiceProvider(collection);

            using (var scope = serviceProvider.CreateScope())
            {
                var singletonEnumerable = scope.ServiceProvider.GetService<IEnumerable<MyTestClass3>>();
                var list = singletonEnumerable.ToList();
                list.Count.ShouldBe(3);

                var injectedObj = scope.ServiceProvider.GetService<MyClassInjectsEnumerable>();
                injectedObj.Objects.Count.ShouldBe(3);
            }
        }

        [Fact]
        public void Resolving_Scoped_Test()
        {
            var collection = new ServiceCollection();

            collection.AddScoped<MyTestClass3>();

            var serviceProvider = CreateServiceProvider(collection);

            using (var scope = serviceProvider.CreateScope())
            {
                var obj1 = scope.ServiceProvider.GetService<MyTestClass3>();
                var obj2 = scope.ServiceProvider.GetService<MyTestClass3>();
                obj1.ShouldBeSameAs(obj2);
            }
        }

        [Fact]
        public void Resolving_Scoped_From_Container_Test()
        {
            var collection = new ServiceCollection();

            collection.AddScoped<MyTestClass3>();

            var serviceProvider = CreateServiceProvider(collection);
            var windsorContainer = serviceProvider.GetService<IWindsorContainer>();

            var obj1 = windsorContainer.Resolve<MyTestClass3>();
            var obj2 = windsorContainer.Resolve<MyTestClass3>();
            obj1.ShouldNotBeSameAs(obj2);
        }

        [Fact]
        public void FactoryServicesAreCreatedAsPartOfCreatingObjectGraph_2()
        {
            var services = new ServiceCollection();
            services.AddTransient<IFakeService, FakeService>();
            services.AddTransient<IFactoryService>((Func<IServiceProvider, IFactoryService>)(p =>
            {
                IFakeService service = p.GetService<IFakeService>();
                return (IFactoryService)new TransientFactoryService()
                {
                    FakeService = service,
                    Value = 42
                };
            }));
            services.AddScoped<ScopedFactoryService>((Func<IServiceProvider, ScopedFactoryService>)(p =>
            {
                IFakeService service = p.GetService<IFakeService>();
                return new ScopedFactoryService()
                {
                    FakeService = service
                };
            }));
            services.AddTransient<ServiceAcceptingFactoryService>();
            IServiceProvider serviceProvider = this.CreateServiceProvider((IServiceCollection)services);
            using (var scope = serviceProvider.CreateScope())
            {
                serviceProvider = scope.ServiceProvider;

                ServiceAcceptingFactoryService service1 = serviceProvider.GetService<ServiceAcceptingFactoryService>();
                ServiceAcceptingFactoryService service2 = serviceProvider.GetService<ServiceAcceptingFactoryService>();
                Assert.Equal<int>(42, service1.TransientService.Value);
                Assert.NotNull((object)service1.TransientService.FakeService);
                Assert.Equal<int>(42, service2.TransientService.Value);
                Assert.NotNull((object)service2.TransientService.FakeService);
                Assert.NotNull((object)service1.ScopedService.FakeService);
                Assert.NotSame((object)service1.TransientService, (object)service2.TransientService);
                Assert.Same((object)service1.ScopedService, (object)service2.ScopedService);
            }
        }

        [Fact]
        public void ShouldReleaseScopedOnScopeDisposeButNotBefore()
        {
            var collection = new ServiceCollection();

            collection.AddScoped<MyTestClass3>();
            var serviceProvider = CreateServiceProvider(collection);
            var windsorContainer = serviceProvider.GetService<IWindsorContainer>();

            MyTestClass3 obj;

            using (var scope = serviceProvider.CreateScope())
            {
                obj = scope.ServiceProvider.GetService<MyTestClass3>();
                obj.IsDisposed.ShouldBeFalse();

                windsorContainer.Release(obj);
                obj.IsDisposed.ShouldBeFalse();
            }

            obj.IsDisposed.ShouldBeTrue();
        }

        [Fact]
        public void Resolving_On_Same_Scope_Should_Be_Thread_Safe()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.TryAddEnumerable(new ServiceCollection()
                .AddScoped<IEntityStateListener, INavigationFixer>(p => p.GetService<INavigationFixer>())
                .AddScoped<INavigationListener, INavigationFixer>(p => p.GetService<INavigationFixer>())
            );

            serviceCollection.TryAdd(new ServiceCollection()
                .AddScoped<INavigationFixer, NavigationFixer>()
            );

            var serviceProvider = CreateServiceProvider(serviceCollection);

            Parallel.For(1, 100, (i) =>
            {
                var listener = serviceProvider.GetRequiredService<INavigationListener>();
                (listener is NavigationFixer).ShouldBeTrue();
            });
        }

        public void Dispose()
        {
            Assert.Null(MsLifetimeScope.Current);
        }
    }
}