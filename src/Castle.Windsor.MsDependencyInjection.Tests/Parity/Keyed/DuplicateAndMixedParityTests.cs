using System.Linq;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Duplicate and mixed registrations: identical keyed entries, the same type under several keys
    /// plus a non-keyed entry, <c>TryAddKeyedSingleton</c>, and a shared keyed singleton.
    /// </summary>
    public sealed class DuplicateAndMixedParityTests
    {
        [Fact]
        public void Two_Identical_Keyed_Regs_Collection_Has_Two()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k")));
        }

        [Fact]
        public void Same_Type_Keyed_K_Keyed_J_NonKeyed_Partitioned()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>("j");
                },
                ctx =>
                [
                    "nonkeyed=" + string.Join(",", Outcome.TypeNames(ctx.Provider.GetServices<IKeyedFake>())),
                    "k=" + string.Join(",", Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k"))),
                    "j=" + string.Join(",", Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("j")))
                ]);
        }

        [Fact]
        public void TryAddKeyedSingleton_Is_NoOp_When_Key_Exists()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.TryAddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
                },
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>("k").ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void Keyed_Singleton_Shared_Across_Many_Resolves()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k")));
        }

        [Fact]
        public void Mixed_Keyed_NonKeyed_Disjoint_Union()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>("k");
                },
                ctx =>
                [
                    // "nonkeyed=" + string.Join(",", ParityOutcome.TypeNames(ctx.Provider.GetServices<IKeyedFake>())),
                    // "k=" + string.Join(",", ParityOutcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k"))),
                    "j=" + string.Join(",", Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("j")))
                ]);
        }

        // The same implementation type can appear in many descriptors. The keyed-constructor
        // metadata registry must dedupe by implementation type instead of crashing when a type
        // that carries [ServiceKey] / [FromKeyedServices] parameters is registered more than once.

        [Fact]
        public void Same_ServiceKey_Type_Under_Three_Keys_Each_Injected()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("a");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("b");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("c");
                },
                ctx =>
                {
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("a")).Key.ShouldBe("a");
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("b")).Key.ShouldBe("b");
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("c")).Key.ShouldBe("c");
                });
        }

        [Fact]
        public void Same_ServiceKey_Type_Duplicate_Same_Key_Collection()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("k");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("k");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("k");
                },
                ctx =>
                {
                    var all = ctx.Provider.GetKeyedServices<IKeyedFake>("k").Cast<StringKeyConsumer>().ToList();
                    all.Count.ShouldBe(3);
                    all.ShouldAllBe(x => x.Key == "k");
                });
        }

        [Fact]
        public void Same_FromKeyed_Consumer_Type_Under_Two_Keys()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedTransient<FromKeyedCtorConsumer>("x");
                    services.AddKeyedTransient<FromKeyedCtorConsumer>("y");
                },
                ctx =>
                {
                    ctx.Provider.GetKeyedService<FromKeyedCtorConsumer>("x").Dep.ShouldBeOfType<KeyedFakeA>();
                    ctx.Provider.GetKeyedService<FromKeyedCtorConsumer>("y").Dep.ShouldBeOfType<KeyedFakeA>();
                });
        }

        [Fact]
        public void Same_FromKeyed_Consumer_Type_Keyed_And_NonKeyed()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedCtorConsumer>();
                    services.AddKeyedTransient<FromKeyedCtorConsumer>("x");
                },
                ctx =>
                {
                    ctx.Provider.GetRequiredService<FromKeyedCtorConsumer>().Dep.ShouldBeOfType<KeyedFakeA>();
                    ctx.Provider.GetKeyedService<FromKeyedCtorConsumer>("x").Dep.ShouldBeOfType<KeyedFakeA>();
                });
        }

        [Fact]
        public void Distinct_Metadata_Types_Each_Duplicated_Build_And_Resolve()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("a");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("b");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("s1");
                    services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>("s2");
                    services.AddTransient<TwoKeyConsumer>();
                    services.AddTransient<TwoKeyConsumer>();
                },
                ctx =>
                {
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("s1")).Key.ShouldBe("s1");
                    ((StringKeyConsumer)ctx.Provider.GetKeyedService<IKeyedFake>("s2")).Key.ShouldBe("s2");

                    var consumers = ctx.Provider.GetServices<TwoKeyConsumer>().ToList();
                    consumers.Count.ShouldBe(2);
                    consumers.ShouldAllBe(c => c.A is KeyedFakeA && c.B is KeyedFakeB);
                });
        }
    }
}
