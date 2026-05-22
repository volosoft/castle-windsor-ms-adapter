using System.Collections.Generic;
using System.Linq;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Regression tests for https://github.com/volosoft/castle-windsor-ms-adapter/issues/48 :
    /// before the keyed-services PR, any <see cref="IServiceCollection"/> containing a keyed
    /// descriptor (added by the user OR a third-party library such as <c>AddOpenApi</c> /
    /// <c>AddHttpClient().AddStandardResilienceHandler</c> / Scalar) crashed the adapter at
    /// registration time with
    /// "This service descriptor is keyed. Your service provider may not support keyed services."
    /// (<see cref="ServiceDescriptor.ImplementationType"/> / <c>.ImplementationInstance</c> /
    /// <c>.ImplementationFactory</c> getters throw on keyed). The tests pin all three keyed
    /// descriptor shapes plus the empty-<see cref="IEnumerable{T}"/> ctor contract that the
    /// pre-PR 4.x adapter honored via <c>allowEmptyCollections: true</c>.
    /// </summary>
    public sealed class Issue48ParityTests
    {
        [Fact]
        public void Issue48_KeyedTypeDescriptor_DoesNotBreakProviderBuild()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("third-party-key");
                    services.AddTransient<IKeyedFake, KeyedFakeB>();
                },
                ctx => ctx.Provider.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>());
        }

        [Fact]
        public void Issue48_KeyedFactoryDescriptor_DoesNotBreakProviderBuild()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake>(
                        "third-party-key",
                        (sp, _) => new KeyedFakeA(sp.GetRequiredService<DisposeTracker>()));
                    services.AddTransient<IKeyedFake, KeyedFakeB>();
                },
                ctx => ctx.Provider.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>());
        }

        [Fact]
        public void Issue48_KeyedInstanceDescriptor_DoesNotBreakProviderBuild()
        {
            var prebuilt = new PreBuiltFake();
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake>("third-party-key", prebuilt);
                    services.AddTransient<IKeyedFake, KeyedFakeB>();
                },
                ctx => ctx.Provider.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>());
        }

        [Fact]
        public void Issue48_AllKeyedDescriptorShapes_Coexist_With_NonKeyed()
        {
            var prebuilt = new PreBuiltFake();
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("type-key");
                    services.AddKeyedSingleton<IKeyedFake>(
                        "factory-key",
                        (sp, _) => new KeyedFakeA(sp.GetRequiredService<DisposeTracker>()));
                    services.AddKeyedSingleton<IKeyedFake>("instance-key", prebuilt);
                    services.AddTransient<IKeyedFake, KeyedFakeB>();
                },
                ctx =>
                {
                    ctx.Provider.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>();
                    ctx.Provider.GetRequiredKeyedService<IKeyedFake>("type-key").ShouldBeOfType<KeyedFakeA>();
                    ctx.Provider.GetRequiredKeyedService<IKeyedFake>("factory-key").ShouldBeOfType<KeyedFakeA>();
                    ctx.Provider.GetRequiredKeyedService<IKeyedFake>("instance-key").ShouldBeSameAs(prebuilt);
                });
        }

        // Surfaced by integration-testing the adapter against a live ASP.NET Zero / Abp 11.2.0
        // Web.Mvc host: a ctor IEnumerable<T> with no T registered must inject as an empty
        // sequence (MS DI contract). Without allowEmptyCollections: true on
        // MsCompatibleCollectionResolver the resolve falls through and HostBuilder.Build throws.
        [Fact]
        public void Empty_EnumerableDependency_Injected_As_Empty_Sequence()
        {
            ParityRunner.RunAssertParity(
                services => services.AddTransient<EmptyCollectionConsumer>(),
                ctx => ctx.Provider.GetRequiredService<EmptyCollectionConsumer>().Items.ShouldBeEmpty());
        }

        public interface INeverRegisteredService { }

        public sealed class EmptyCollectionConsumer
        {
            public EmptyCollectionConsumer(IEnumerable<INeverRegisteredService> items)
            {
                Items = items.ToList();
            }

            public IReadOnlyList<INeverRegisteredService> Items { get; }
        }
    }
}
