using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// <c>[ServiceKey]</c> constructor injection: the resolution key is injected for string, int and
    /// enum keys, via AnyKey registrations, per distinct key, and when the parameter type mismatches.
    /// </summary>
    public sealed class ServiceKeyInjectionParityTests
    {
        [Fact]
        public void ServiceKey_String_Injected()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("mykey"),
                ctx => ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("mykey")).Key.ShouldBe("mykey"));
        }

        [Fact]
        public void ServiceKey_Int_Injected()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, IntKeyConsumer>(42),
                ctx => ((IntKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>(42)).Key.ShouldBe(42));
        }

        [Fact]
        public void ServiceKey_Enum_Injected()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, EnumKeyConsumer>(FakeEnumKey.Beta),
                ctx => ((EnumKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>(FakeEnumKey.Beta)).Key.ShouldBe(FakeEnumKey.Beta));
        }

        [Fact]
        public void ServiceKey_Via_AnyKey_Gets_Actual_Key()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>(KeyedService.AnyKey),
                ctx => ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("z")).Key.ShouldBe("z"));
        }

        [Fact]
        public void ServiceKey_Wrong_Param_Type()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, WrongTypeKeyConsumer>("s"),
                ctx => Outcome.Result(() => ctx.Provider.GetKeyedService<IKeyedFake>("s")));
        }

        [Fact]
        public void ServiceKey_Consumer_Resolved_Per_Distinct_Key()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("a");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("b");
                },
                ctx =>
                {
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("a")).Key.ShouldBe("a");
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("b")).Key.ShouldBe("b");
                });
        }

        // A type that declares a [ServiceKey] constructor must still be usable without a key:
        // a non-keyed resolve falls back to a keyless constructor (MS DI behavior) instead of
        // failing, while a keyed resolve of the same type still gets the key injected.

        [Fact]
        public void ServiceKey_Type_NonKeyed_Falls_Back_To_Keyless_Ctor()
        {
            ParityRunner.RunAssertParity(
                services => services.AddTransient<IKeyedFake, OptionalServiceKeyConsumer>(),
                ctx =>
                {
                    var instance = (OptionalServiceKeyConsumer)ctx.Provider.GetService<IKeyedFake>();
                    instance.ShouldNotBeNull();
                    instance.UsedKeylessCtor.ShouldBeTrue();
                    instance.Key.ShouldBeNull();
                });
        }

        [Fact]
        public void ServiceKey_Type_Keyed_Injects_Key_Despite_Keyless_Ctor()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, OptionalServiceKeyConsumer>("the-key"),
                ctx =>
                {
                    var instance = (OptionalServiceKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("the-key");
                    instance.UsedKeylessCtor.ShouldBeFalse();
                    instance.Key.ShouldBe("the-key");
                });
        }

        [Fact]
        public void ServiceKey_Type_NonKeyed_And_Keyed_Coexist()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddTransient<IKeyedFake, OptionalServiceKeyConsumer>();
                    services.AddKeyedTransient<IKeyedFake, OptionalServiceKeyConsumer>("k");
                },
                ctx =>
                {
                    ((OptionalServiceKeyConsumer)ctx.Provider.GetService<IKeyedFake>())
                        .UsedKeylessCtor.ShouldBeTrue();

                    var keyed = (OptionalServiceKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("k");
                    keyed.UsedKeylessCtor.ShouldBeFalse();
                    keyed.Key.ShouldBe("k");
                });
        }

        [Fact]
        public void ServiceKey_Type_NonKeyed_Resolved_Through_Collection()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddTransient<IKeyedFake, OptionalServiceKeyConsumer>(),
                ctx => Outcome.TypeNames(ctx.Provider.GetServices<IKeyedFake>()));
        }
    }
}
