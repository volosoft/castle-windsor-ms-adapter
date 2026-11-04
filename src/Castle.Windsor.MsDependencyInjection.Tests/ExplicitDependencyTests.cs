using System;
using System.Collections.Generic;
using System.Linq;
using Castle.Core.Configuration;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ExplicitDependencyTests
    {
        [Fact]
        public void A_Keyed_Constructor_Collection_Receives_Native_Runtime_Arguments()
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddKeyedTransient<ArgumentItem>("k");
            services.AddTransient<ArgumentOwner>();
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var owner = container.Resolve<ArgumentOwner>(new Castle.MicroKernel.Arguments { { "token", "runtime" } });
            Assert.Equal("runtime", Assert.Single(owner.Items).Token);
            ((IDisposable)provider).Dispose();
        }

        [Fact]
        public void A_Named_Component_Reference_Precedes_A_Keyed_Attribute()
        {
            using var container = new WindsorContainer();
            var expected = new Item();
            container.Register(Component.For<Item>().Named("expected").Instance(expected));
            container.Register(Component.For<NamedOwner>()
                .DependsOn(Dependency.OnComponent("item", "expected")).LifestyleTransient());
            Assert.Same(expected, container.Resolve<NamedOwner>().Item);
            var services = new ServiceCollection();
            services.AddKeyedTransient<Item>("k");
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            Assert.Same(expected, container.Resolve<NamedOwner>().Item);
            Assert.Same(expected, provider.GetRequiredService<NamedOwner>().Item);
            ((IDisposable)provider).Dispose();
        }

        [Theory]
        [InlineData("factory", false)]
        [InlineData("factory", true)]
        [InlineData("null", false)]
        [InlineData("null", true)]
        [InlineData("instance", false)]
        [InlineData("instance", true)]
        public void Native_Explicit_Configuration_Precedes_A_Service_Descriptor(string registration, bool beforeInstallation)
        {
            using var container = new WindsorContainer();
            var expected = new Item();
            var other = new Item();
            var calls = 0;
            container.Register(Component.For<Item>().Named("expected").Instance(expected));
            var services = new ServiceCollection();
            if (registration == "instance")
            {
                services.AddSingleton<IEnumerable<Item>>(new[] { other });
            }
            else
            {
                services.AddTransient<IEnumerable<Item>>(_ =>
                {
                    calls++;
                    return registration == "null" ? null : new[] { other };
                });
            }

            var configuration = new MutableConfiguration("list");
            configuration.Children.Add(new MutableConfiguration("item", "${expected}"));
            void Register() => container.Register(Component.For<Owner>()
                .DependsOn(Dependency.OnConfigValue("items", configuration)).LifestyleTransient());
            if (beforeInstallation)
            {
                Register();
            }

            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            if (!beforeInstallation)
            {
                Register();
            }

            Assert.Same(expected, Assert.Single(container.Resolve<Owner>().Items));
            Assert.Same(expected, Assert.Single(provider.GetRequiredService<Owner>().Items));
            Assert.Equal(0, calls);
            ((IDisposable)provider).Dispose();
        }

        public sealed class Item;

        public sealed class Owner
        {
            public IEnumerable<Item> Items { get; }

            public Owner(IEnumerable<Item> items)
            {
                Items = items;
            }
        }

        public sealed class NamedOwner
        {
            public Item Item { get; }

            public NamedOwner([FromKeyedServices("missing")] Item item)
            {
                Item = item;
            }
        }

        public sealed class ArgumentItem
        {
            public string Token { get; }

            public ArgumentItem(string token)
            {
                Token = token;
            }
        }

        public sealed class ArgumentOwner
        {
            public IEnumerable<ArgumentItem> Items { get; }

            public ArgumentOwner([FromKeyedServices("k")] IEnumerable<ArgumentItem> items)
            {
                Items = items;
            }
        }
    }
}
