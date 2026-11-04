using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class KeyedRegistrationSelectionTests
    {
        [Fact]
        public void Unrelated_Parameter_Attributes_Are_Not_Constructed()
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<UnannotatedDependency>();
            services.AddTransient<UnrelatedAttributeOwner>();
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            Assert.NotNull(provider.GetRequiredService<UnrelatedAttributeOwner>().Dependency);
            ((IDisposable)provider).Dispose();
        }

        [AttributeUsage(AttributeTargets.Parameter)]
        public sealed class ExplosiveAttribute : Attribute
        {
            public ExplosiveAttribute()
            {
                throw new InvalidOperationException("Unrelated attribute was constructed.");
            }
        }

        public sealed class UnannotatedDependency;

        public sealed class UnrelatedAttributeOwner
        {
            public UnannotatedDependency Dependency { get; }

            public UnrelatedAttributeOwner([Explosive] UnannotatedDependency dependency)
            {
                Dependency = dependency;
            }
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Missing_Keyed_Service_Uses_The_Resolvable_Constructor(bool microsoft)
        {
            var services = new ServiceCollection();
            services.AddSingleton<IItem<int>, ClosedItem>();
            services.AddTransient<OptionalOwner>();
            using var container = new WindsorContainer();
            var provider = Build(services, container, microsoft);
            Assert.Equal("short", provider.GetRequiredService<OptionalOwner>().Constructor);
            ((IDisposable)provider).Dispose();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Missing_Keyed_Options_Do_Not_Use_Ordinary_Options(bool microsoft)
        {
            var services = new ServiceCollection();
            services.AddOptions();
            services.AddTransient<OptionsOwner>();
            using var container = new WindsorContainer();
            var provider = Build(services, container, microsoft);
            Assert.Equal("short", provider.GetRequiredService<OptionsOwner>().Constructor);
            ((IDisposable)provider).Dispose();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Ordinary_Constructors_Do_Not_Consume_Keyed_Only_Services(bool microsoft)
        {
            var services = new ServiceCollection();
            services.AddKeyedTransient<IItem<int>, ClosedItem>("k");
            services.AddTransient<PlainOwner>();
            using var container = new WindsorContainer();
            var provider = Build(services, container, microsoft);
            Assert.ThrowsAny<Exception>(() => provider.GetRequiredService<PlainOwner>());
            ((IDisposable)provider).Dispose();
        }

        [Fact]
        public void Ordinary_Open_Generic_Is_Available_Beside_A_Closed_Keyed_Service()
        {
            var services = new ServiceCollection();
            services.AddTransient(typeof(IItem<>), typeof(OpenItem<>));
            services.AddKeyedTransient<IItem<int>, ClosedItem>("k");
            services.AddTransient<PlainOwner>();
            using var container = new WindsorContainer();
            var provider = Build(services, container, false);
            Assert.Equal("open", provider.GetRequiredService<PlainOwner>().Item.Name);
            Assert.Equal("closed", provider.GetRequiredKeyedService<IItem<int>>("k").Name);
            ((IDisposable)provider).Dispose();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Closed_AnyKey_Precedes_Open_Specific_Key(bool microsoft)
        {
            var services = new ServiceCollection();
            services.AddKeyedTransient(typeof(IItem<>), "k", typeof(OpenItem<>));
            services.AddKeyedTransient<IItem<int>, ClosedItem>(KeyedService.AnyKey);
            using var container = new WindsorContainer();
            var provider = Build(services, container, microsoft);
            Assert.Equal("closed", provider.GetRequiredKeyedService<IItem<int>>("k").Name);
            ((IDisposable)provider).Dispose();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Collections_Preserve_Registration_Order_Across_Open_And_Closed_Generics(bool microsoft)
        {
            var services = new ServiceCollection();
            services.AddKeyedTransient(typeof(IItem<>), "k", typeof(OpenItem<>));
            services.AddKeyedTransient<IItem<int>, ClosedItem>("k");
            using var container = new WindsorContainer();
            var provider = Build(services, container, microsoft);
            Assert.Equal(new[] { "open", "closed" }, provider.GetKeyedServices<IItem<int>>("k").Select(item => item.Name));
            ((IDisposable)provider).Dispose();
        }

        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void Explicit_Enumerable_Precedes_Synthesized_Collection(bool microsoft)
        {
            IEnumerable<IItem<int>> instance = new[] { new ClosedItem() };
            var services = new ServiceCollection();
            services.AddKeyedTransient(typeof(IItem<>), "k", typeof(OpenItem<>));
            services.AddKeyedSingleton("k", instance);
            services.AddTransient<ArrayOwner>();
            using var container = new WindsorContainer();
            var provider = Build(services, container, microsoft);
            Assert.Same(instance, provider.GetRequiredKeyedService<IEnumerable<IItem<int>>>("k"));
            Assert.Same(instance, provider.GetRequiredService<ArrayOwner>().Items);
            ((IDisposable)provider).Dispose();
        }

        private static IServiceProvider Build(IServiceCollection services, IWindsorContainer container, bool microsoft)
        {
            return microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        }

        public interface IItem<T>
        {
            string Name { get; }
        }

        public sealed class OpenItem<T> : IItem<T>
        {
            public string Name
            {
                get
                {
                    return "open";
                }
            }
        }

        public sealed class ClosedItem : IItem<int>
        {
            public string Name
            {
                get
                {
                    return "closed";
                }
            }
        }

        public sealed class PlainOwner
        {
            public IItem<int> Item { get; }

            public PlainOwner(IItem<int> item)
            {
                Item = item;
            }
        }

        public sealed class ArrayOwner
        {
            public IEnumerable<IItem<int>> Items { get; }

            public ArrayOwner([FromKeyedServices("k")] IEnumerable<IItem<int>> items)
            {
                Items = items;
            }
        }

        public sealed class OptionalOwner
        {
            public string Constructor { get; }

            public OptionalOwner()
            {
                Constructor = "short";
            }

            public OptionalOwner([FromKeyedServices("missing")] IItem<int> item)
            {
                Constructor = "long";
            }
        }

        public sealed class Settings;

        public sealed class OptionsOwner
        {
            public string Constructor { get; }

            public OptionsOwner()
            {
                Constructor = "short";
            }

            public OptionsOwner([FromKeyedServices("missing")] IOptions<Settings> options)
            {
                Constructor = "long";
            }
        }
    }
}
