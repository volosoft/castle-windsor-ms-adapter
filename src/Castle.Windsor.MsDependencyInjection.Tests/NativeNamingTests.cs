using System;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel;
using Castle.MicroKernel.Registration;
using Castle.MicroKernel.SubSystems.Naming;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class NativeNamingTests
    {
        [Fact]
        public void Native_Constructor_Collections_Preserve_Activation_Order()
        {
            var log = new List<string>();
            using var container = new WindsorContainer();
            container.Register(Component.For<Item>().Named("first").UsingFactoryMethod(() =>
                {
                    log.Add("first");
                    return new Item("first");
                }).LifestyleTransient());
            container.Register(Component.For<Item>().Named("second").UsingFactoryMethod(() =>
                {
                    log.Add("second");
                    return new Item("second");
                }).LifestyleTransient());
            container.Register(Component.For<Owner>());
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            var owner = provider.GetRequiredService<Owner>();
            Assert.Equal(new[] { "first", "second" }, log);
            Assert.Equal(new[] { "second", "first" }, owner.Items.Select(item => item.Name));
            ((IDisposable)provider).Dispose();
        }

        [Fact]
        public void Custom_Availability_Is_Respected_After_Installation()
        {
            using var container = new WindsorContainer();
            var naming = new ConditionalNaming();
            container.Kernel.AddSubSystem(SubSystemConstants.NamingKey, naming);
            container.Register(Component.For<Item>().UsingFactoryMethod(() => new Item("native")));
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            Assert.NotNull(provider.GetService<Item>());
            naming.Hide = true;
            Assert.Null(provider.GetService<Item>());
            Assert.False(provider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(Item)));
            naming.Hide = false;
            Assert.NotNull(provider.GetService<Item>());
            ((IDisposable)provider).Dispose();
        }

        private sealed class ConditionalNaming : DefaultNamingSubSystem
        {
            public bool Hide;

            public override bool Contains(Type service)
            {
                return service != typeof(Item) || !Hide ? base.Contains(service) : false;
            }
        }

        public sealed record Item(string Name);

        public sealed class Owner
        {
            public IEnumerable<Item> Items { get; }

            public Owner(IEnumerable<Item> items)
            {
                Items = items;
            }
        }
    }
}
