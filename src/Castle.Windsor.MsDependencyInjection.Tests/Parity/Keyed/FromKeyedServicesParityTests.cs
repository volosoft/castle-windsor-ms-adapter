using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// <c>[FromKeyedServices]</c> constructor injection: a single keyed dependency, into a positional
    /// record, into an <c>IEnumerable&lt;T&gt;</c>, with a null key, AnyKey-backed, a missing key, and
    /// two distinct keyed parameters.
    /// </summary>
    public sealed class FromKeyedServicesParityTests
    {
        [Fact]
        public void FromKeyedServices_Ctor_Param()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedCtorConsumer>();
                },
                ctx => ctx.Provider.GetRequiredService<FromKeyedCtorConsumer>().Dep.ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void FromKeyedServices_Into_Positional_Record()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedRecord>();
                },
                ctx => ctx.Provider.GetRequiredService<FromKeyedRecord>().Dep.ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void FromKeyedServices_Into_IEnumerable()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
                    services.AddTransient<FromKeyedEnumerableConsumer>();
                },
                ctx => Outcome.ResultMany(
                    () => ctx.Provider.GetRequiredService<FromKeyedEnumerableConsumer>().Deps));
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
        public void FromKeyedServices_AnyKey_Backed()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey);
                    services.AddTransient<FromKeyedCtorConsumer>();
                },
                ctx => ctx.Provider.GetRequiredService<FromKeyedCtorConsumer>().Dep.ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void FromKeyedServices_Missing_Key_ThrowType()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddTransient<FromKeyedMissingConsumer>(),
                ctx => Outcome.Result(() => ctx.Provider.GetRequiredService<FromKeyedMissingConsumer>()));
        }

        [Fact]
        public void FromKeyedServices_Two_Params_Distinct()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("a");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("b");
                    services.AddTransient<TwoKeyConsumer>();
                },
                ctx =>
                {
                    var consumer = ctx.Provider.GetRequiredService<TwoKeyConsumer>();
                    consumer.A.ShouldBeOfType<KeyedFakeA>();
                    consumer.B.ShouldBeOfType<KeyedFakeB>();
                });
        }

        [Fact]
        public void FromKeyedServices_Parameterless_Inherits_Parent_Key()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedTransient<IInheritChild, InheritChild>(KeyedService.AnyKey);
                    services.AddKeyedTransient<IInheritParent, InheritKeyParent>("k");
                },
                ctx => Outcome.Result(
                    () => ctx.Provider.GetKeyedService<IInheritParent>("k").Child.CapturedKey));
        }

        [Fact]
        public void FromKeyedServices_Parameterless_Propagates_Distinct_Parent_Keys()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedTransient<IInheritChild, InheritChild>(KeyedService.AnyKey);
                    services.AddKeyedTransient<IInheritParent, InheritKeyParent>("k");
                    services.AddKeyedTransient<IInheritParent, InheritKeyParent>("k2");
                },
                ctx =>
                [
                    "k:" + Outcome.Result(
                        () => ctx.Provider.GetKeyedService<IInheritParent>("k").Child.CapturedKey)[0],
                    "k2:" + Outcome.Result(
                        () => ctx.Provider.GetKeyedService<IInheritParent>("k2").Child.CapturedKey)[0]
                ]);
        }

        [Fact]
        public void FromKeyedServices_Parameterless_NonKeyed_Parent_Resolves_NonKeyed_Service()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddTransient<FromKeyedInheritConsumer>();
                },
                ctx => ctx.Provider
                    .GetRequiredService<FromKeyedInheritConsumer>()
                    .Dep.ShouldBeOfType<KeyedFakeA>());
        }

        // A missing [FromKeyedServices] dependency whose constructor parameter has a default
        // value falls back to that default (MS DI) rather than throwing. The fallback applies
        // uniformly to explicit-key, null-key and inherit-key lookups; a present dependency
        // still wins over the default.

        [Fact]
        public void FromKeyedServices_Missing_ExplicitKey_With_Default_Uses_Default()
        {
            ParityRunner.RunAssertParity(
                services => services.AddTransient<FromKeyedDefaultConsumer>(),
                ctx => ctx.Provider.GetRequiredService<FromKeyedDefaultConsumer>().Dep.ShouldBeNull());
        }

        [Fact]
        public void FromKeyedServices_Present_ExplicitKey_Wins_Over_Default()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedDefaultConsumer>();
                },
                ctx => ctx.Provider.GetRequiredService<FromKeyedDefaultConsumer>().Dep.ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void FromKeyedServices_Two_Defaults_One_Present_One_Missing()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("a");
                    services.AddTransient<FromKeyedTwoDefaultsConsumer>();
                },
                ctx =>
                {
                    var consumer = ctx.Provider.GetRequiredService<FromKeyedTwoDefaultsConsumer>();
                    consumer.A.ShouldBeOfType<KeyedFakeA>();
                    consumer.B.ShouldBeNull();
                });
        }

        [Fact]
        public void FromKeyedServices_Missing_NullKey_With_Default_Uses_Default()
        {
            ParityRunner.RunAssertParity(
                services => services.AddTransient<FromKeyedNullDefaultConsumer>(),
                ctx => ctx.Provider.GetRequiredService<FromKeyedNullDefaultConsumer>().Dep.ShouldBeNull());
        }

        [Fact]
        public void FromKeyedServices_Missing_InheritKey_With_Default_Uses_Default()
        {
            ParityRunner.RunAssertParity(
                services => services.AddTransient<FromKeyedInheritDefaultConsumer>(),
                ctx => ctx.Provider.GetRequiredService<FromKeyedInheritDefaultConsumer>().Dep.ShouldBeNull());
        }
    }
}
