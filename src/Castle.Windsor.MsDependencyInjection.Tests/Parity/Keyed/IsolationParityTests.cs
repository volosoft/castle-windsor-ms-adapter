using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>Keyed and non-keyed services are isolated from each other in both directions.</summary>
    public sealed class IsolationParityTests
    {
        [Fact]
        public void NonKeyed_GetService_Ignores_Keyed()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddSingleton<IKeyedFake, KeyedFakeB>();
                },
                ctx => ctx.Provider.GetService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>());
        }

        [Fact]
        public void NonKeyed_GetServices_Excludes_Keyed()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddSingleton<IKeyedFake, KeyedFakeB>();
                    services.AddSingleton<IKeyedFake, KeyedFakeC>();
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetServices<IKeyedFake>()));
        }

        [Fact]
        public void Keyed_GetKeyedService_Ignores_NonKeyed()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeB>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                },
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>("k").ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void Keyed_GetKeyedServices_Excludes_NonKeyed()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeB>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k")));
        }

        [Fact]
        public void GetKeyedService_Missing_With_Only_NonKeyed_Returns_Null()
        {
            ParityRunner.RunAssertParity(
                services => services.AddSingleton<IKeyedFake, KeyedFakeB>(),
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>("missing").ShouldBeNull());
        }

        [Fact]
        public void NonKeyed_And_Keyed_Singleton_SameType_Are_Distinct()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                },
                ctx => Outcome.Order(
                    ctx.Provider.GetService<IKeyedFake>(),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k")));
        }
    }
}
