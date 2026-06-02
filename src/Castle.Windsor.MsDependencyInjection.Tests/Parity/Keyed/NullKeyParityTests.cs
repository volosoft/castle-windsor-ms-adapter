using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Null-key registration and resolution: registering with a null key, resolving a null key when
    /// only a non-keyed registration exists, <c>[FromKeyedServices(null)]</c>, and the required-resolve
    /// failure type.
    /// </summary>
    public sealed class NullKeyParityTests
    {
        [Fact]
        public void AddKeyed_Null_Then_GetKeyedService_Null()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>(null),
                ctx => Outcome.Result(() => ctx.Provider.GetKeyedService<IKeyedFake>(null)));
        }

        [Fact]
        public void AddKeyed_Null_Then_NonKeyed_GetService()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>(null),
                ctx => Outcome.Result(() => ctx.Provider.GetService<IKeyedFake>()));
        }

        [Fact]
        public void GetKeyedService_Null_With_Only_NonKeyed_Returns_NonKeyed()
        {
            ParityRunner.RunAssertParity(
                services => services.AddSingleton<IKeyedFake, KeyedFakeA>(),
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>(null).ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void FromKeyedServices_Null_Falls_Back_To_NonKeyed_Default()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddTransient<FromKeyedNullConsumer>();
                },
                ctx => Outcome.Result(
                    () => ctx.Provider.GetRequiredService<FromKeyedNullConsumer>().Dep));
        }

        [Fact]
        public void GetRequiredKeyedService_Null_Nothing_Registered_ThrowType()
        {
            ParityRunner.RunOutcomeParity(
                services => { },
                ctx => Outcome.Result(() => ctx.Provider.GetRequiredKeyedService<IKeyedFake>(null)));
        }
    }
}
