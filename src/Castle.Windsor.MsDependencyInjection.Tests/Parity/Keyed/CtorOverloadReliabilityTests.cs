using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// The reason this design exists: keyed-parameter detection must follow the EXACT
    /// constructor the container selected, never a name-merged scan across overloads, and the
    /// registration-time metadata gate must also cover non-keyed implementation types (a
    /// non-keyed component may take a <c>[FromKeyedServices]</c> dependency). Every scenario is
    /// asserted for MS-DI parity.
    /// </summary>
    public sealed class CtorOverloadReliabilityTests
    {
        [Fact]
        public void ServiceKey_Injected_Into_Selected_Overload_Not_Name_Merged()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, OverloadedServiceKeyConsumer>("the-key"),
                ctx =>
                {
                    var consumer = (OverloadedServiceKeyConsumer)ctx.Provider.GetRequiredKeyedService<IKeyedFake>("the-key");
                    consumer.SelectedCtor.ShouldBe("service-key");
                    consumer.BoxedKey.ShouldBe("the-key");
                });
        }

        [Fact]
        public void FromKeyedServices_Injected_Into_Selected_Overload_Not_Name_Merged()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<OverloadedFromKeyedConsumer>();
                },
                ctx =>
                {
                    var consumer = ctx.Provider.GetRequiredService<OverloadedFromKeyedConsumer>();
                    consumer.SelectedCtor.ShouldBe("keyed");
                    consumer.Dep.ShouldBeOfType<KeyedFakeA>();
                });
        }

        [Fact]
        public void NonKeyed_Component_With_FromKeyedServices_Param_Resolves()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddSingleton<NonKeyedConsumerOfKeyedDep>();
                },
                ctx => ctx.Provider.GetRequiredService<NonKeyedConsumerOfKeyedDep>()
                    .Dep.ShouldBeOfType<KeyedFakeA>());
        }
    }
}
