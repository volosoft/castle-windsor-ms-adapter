using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// <see cref="ActivatorUtilities"/> with keyed constructor parameters: <c>CreateInstance</c> and
    /// <c>GetServiceOrCreateInstance</c> resolving <c>[FromKeyedServices]</c>, explicit extra arguments
    /// alongside a keyed parameter, and a <c>[ServiceKey]</c> parameter outside a keyed resolution.
    /// </summary>
    public sealed class ActivatorUtilitiesKeyedParityTests
    {
        [Fact]
        public void CreateInstance_Resolves_FromKeyedServices()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => ActivatorUtilities.CreateInstance<FromKeyedCtorConsumer>(ctx.Provider)
                    .Dep.ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void GetServiceOrCreateInstance_With_Keyed_Dep()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => ActivatorUtilities.GetServiceOrCreateInstance<FromKeyedCtorConsumer>(ctx.Provider)
                    .Dep.ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void CreateInstance_Extra_Args_Plus_FromKeyedServices()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    var consumer = ActivatorUtilities.CreateInstance<MixedArgConsumer>(ctx.Provider, "hello");
                    consumer.Label.ShouldBe("hello");
                    consumer.Dep.ShouldBeOfType<KeyedFakeA>();
                });
        }

        [Fact]
        public void CreateInstance_With_ServiceKey_Param()
        {
            ParityRunner.RunOutcomeParity(
                services => { },
                ctx => Outcome.Result(
                    () => ActivatorUtilities.CreateInstance<StringKeyConsumer>(ctx.Provider)));
        }
    }
}
