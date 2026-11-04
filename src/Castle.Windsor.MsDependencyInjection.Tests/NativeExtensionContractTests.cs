using System;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class NativeExtensionContractTests
    {
        [Theory]
        [InlineData("type", false, false)]
        [InlineData("type", false, true)]
        [InlineData("type", true, false)]
        [InlineData("type", true, true)]
        [InlineData("factory", false, false)]
        [InlineData("factory", false, true)]
        [InlineData("factory", true, false)]
        [InlineData("factory", true, true)]
        [InlineData("null", false, false)]
        [InlineData("null", false, true)]
        [InlineData("null", true, false)]
        [InlineData("null", true, true)]
        [InlineData("value", false, false)]
        [InlineData("value", false, true)]
        [InlineData("value", true, false)]
        [InlineData("value", true, true)]
        public void Native_Sub_Resolvers_Keep_Their_Precedence(string registration, bool resolverFirst, bool descriptorConsumer)
        {
            if (registration == "value")
            {
                CheckResolver(17, registration, resolverFirst, descriptorConsumer);
            }
            else
            {
                CheckResolver(new Item(), registration, resolverFirst, descriptorConsumer);
            }
        }

        private static void CheckResolver<T>(T expected, string registration, bool resolverFirst, bool descriptorConsumer)
        {
            using var container = new WindsorContainer();
            var resolver = new ValueResolver<T>(expected);
            if (resolverFirst)
            {
                container.Kernel.Resolver.AddSubResolver(resolver);
            }

            var services = new ServiceCollection();
            if (registration == "type")
            {
                services.AddTransient<Item>();
            }
            else if (registration == "factory")
            {
                services.AddTransient(_ => new Item());
            }
            else if (registration == "null")
            {
                services.AddTransient<Item>(_ => null);
            }
            else
            {
                services.AddSingleton(typeof(int), 42);
            }

            if (descriptorConsumer)
            {
                services.AddTransient<Consumer<T>>();
            }

            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            if (!descriptorConsumer)
            {
                container.Register(Component.For<Consumer<T>>().LifestyleTransient());
            }

            if (!resolverFirst)
            {
                container.Kernel.Resolver.AddSubResolver(resolver);
            }

            var consumer = container.Resolve<Consumer<T>>();
            Assert.Equal(expected, consumer.Value);
            Assert.Equal(1, resolver.Calls);
            ((IDisposable)root).Dispose();
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Ordinary_Factory_Dependencies_Notify_Windsor_Once(bool descriptorConsumer, bool nullValue)
        {
            using var container = new WindsorContainer();
            var expected = nullValue ? null : new Item();
            var factoryCalls = 0;
            var services = new ServiceCollection();
            services.AddTransient(_ =>
            {
                factoryCalls++;
                return expected;
            });
            if (descriptorConsumer)
            {
                services.AddTransient<Consumer<Item>>();
            }

            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            if (!descriptorConsumer)
            {
                container.Register(Component.For<Consumer<Item>>().LifestyleTransient());
            }

            var notifications = 0;
            container.Kernel.DependencyResolving += (model, dependency, value) =>
            {
                if (model.Implementation != typeof(Consumer<Item>) || dependency.TargetItemType != typeof(Item))
                {
                    return;
                }

                notifications++;
                Assert.Same(expected, value);
            };
            Assert.Same(expected, container.Resolve<Consumer<Item>>().Value);
            Assert.Equal(1, factoryCalls);
            Assert.Equal(1, notifications);
            ((IDisposable)root).Dispose();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Null_Factories_Keep_Their_Resolved_Dependencies_Scope_Owned(bool factoryEntry)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<IWindsorContainer>(container);
            services.AddTransient<Resource>();
            Resource resource = null;
            services.AddTransient<Item>(provider =>
            {
                resource = provider.GetRequiredService<Resource>();
                return null;
            });
            services.AddTransient<Consumer<Item>>();
            var factory = new WindsorServiceProviderFactory();
            var root = factoryEntry ? factory.CreateServiceProvider(factory.CreateBuilder(services))
                : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            using (var scope = root.CreateScope())
            {
                Assert.Null(scope.ServiceProvider.GetRequiredService<Consumer<Item>>().Value);
                Assert.Equal(0, resource.Disposals);
            }
            Assert.Equal(1, resource.Disposals);
            ((IDisposable)root).Dispose();
            Assert.Equal(1, resource.Disposals);
        }

        [Theory]
        [InlineData("default", false, false)]
        [InlineData("default", false, true)]
        [InlineData("default", true, false)]
        [InlineData("default", true, true)]
        [InlineData("descriptor", false, false)]
        [InlineData("descriptor", false, true)]
        [InlineData("descriptor", true, false)]
        [InlineData("descriptor", true, true)]
        [InlineData("native", false, false)]
        [InlineData("native", false, true)]
        [InlineData("native", true, false)]
        [InlineData("native", true, true)]
        public void Provider_Decorators_Keep_The_Root_Creation_Entry(string registration, bool keyed, bool factoryEntry)
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<IWindsorContainer>(container);
            if (registration == "descriptor")
            {
                services.AddTransient<IServiceProvider, ProviderDecorator>();
            }

            if (keyed)
            {
                services.AddKeyedScoped<Resource>("key");
            }
            else
            {
                services.AddScoped<Resource>();
            }

            var factory = new WindsorServiceProviderFactory();
            factory.CreateBuilder(services);
            if (registration == "native")
            {
                container.Register(Component.For<IServiceProvider>()
                .ImplementedBy<ProviderDecorator>().IsDefault().LifestyleTransient());
            }

            var root = factoryEntry ? factory.CreateServiceProvider(container)
                        : WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            using var scope = root.CreateScope();
            var resource = keyed ? scope.ServiceProvider.GetRequiredKeyedService<Resource>("key")
                : scope.ServiceProvider.GetRequiredService<Resource>();
            ((IDisposable)root).Dispose();
            Assert.Equal(factoryEntry ? 0 : 1, resource.Disposals);
            scope.Dispose();
            Assert.Equal(1, resource.Disposals);
        }

        public sealed class Item;

        public sealed class Consumer<T>
        {
            public T Value { get; }

            public Consumer(T value)
            {
                Value = value;
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

        public sealed class ProviderDecorator : IServiceProvider, IKeyedServiceProvider, IDisposable
        {
            private readonly IServiceProvider _inner;
            private readonly IWindsorContainer _container;
            private readonly bool _root;

            public ProviderDecorator(IServiceProvider inner, IWindsorContainer container, MsLifetimeScopeProvider lifetime)
            {
                _inner = inner;
                _container = container;
                _root = lifetime.LifetimeScope is GlobalMsLifetimeScope;
            }

            public object GetService(Type type)
            {
                return _inner.GetService(type);
            }

            public object GetKeyedService(Type type, object key)
            {
                return ((IKeyedServiceProvider)_inner).GetKeyedService(type, key);
            }

            public object GetRequiredKeyedService(Type type, object key)
            {
                return ((IKeyedServiceProvider)_inner).GetRequiredKeyedService(type, key);
            }

            public void Dispose()
            {
                if (_root)
                {
                    _container.Resolve<GlobalMsLifetimeScope>().Dispose();
                }
            }
        }

        private sealed class ValueResolver<T> : ISubDependencyResolver
        {
            private T _value;
            public int Calls;

            public ValueResolver(T value)
            {
                _value = value;
            }

            public bool CanResolve(CreationContext context, ISubDependencyResolver parent, ComponentModel model, DependencyModel dependency)
            {
                return dependency.TargetItemType == typeof(T);
            }

            public object Resolve(CreationContext context, ISubDependencyResolver parent, ComponentModel model, DependencyModel dependency)
            {
                Calls++;
                return _value;
            }
        }
    }
}
