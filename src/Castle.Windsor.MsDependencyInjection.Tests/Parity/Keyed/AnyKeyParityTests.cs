using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// <see cref="KeyedService.AnyKey"/> behavior: a specific resolve hitting an AnyKey registration,
    /// AnyKey as the query key for single and collection resolves, factory AnyKey receiving the actual
    /// key, wrong-type keys, transient AnyKey, and the IsKeyedService query.
    /// </summary>
    public sealed class AnyKeyParityTests
    {
        [Fact]
        public void GetKeyedService_Specific_Hits_AnyKey_Registration()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>("anything").ShouldBeOfType<KeyedFakeA>());
        }

        [Fact]
        public void GetKeyedServices_Specific_Includes_AnyKey_Expanded()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>(KeyedService.AnyKey);
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k")));
        }

        [Fact]
        public void GetKeyedService_AnyKey_With_AnyKey_Reg()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx => Outcome.Result(() => ctx.Provider.GetKeyedService<IKeyedFake>(KeyedService.AnyKey)));
        }

        [Fact]
        public void GetKeyedServices_AnyKey_Query()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("j");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>(KeyedService.AnyKey);
                },
                ctx => Outcome.ResultMany(() => ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey)));
        }

        [Fact]
        public void Factory_AnyKey_Receives_Actual_Key()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake>(
                    KeyedService.AnyKey, (sp, key) => new KeyedFakeKeyCapture(key)),
                ctx => ((KeyedFakeKeyCapture)ctx.Provider.GetKeyedService<IKeyedFake>("abc"))
                    .CapturedKey.ShouldBe("abc"));
        }

        [Fact]
        public void AnyKey_WrongType_Vs_ServiceKey_String()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, StringKeyConsumer>(KeyedService.AnyKey),
                ctx => Outcome.Result(() => ctx.Provider.GetKeyedService<IKeyedFake>(5)));
        }

        [Fact]
        public void AnyKey_Transient_Same_Key_Twice_Distinct()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k")));
        }

        [Fact]
        public void IsKeyedService_AnyKey()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Result(
                    () => ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), KeyedService.AnyKey)));
        }

        [Fact]
        public void GetKeyedService_AnyKey_Nothing_Registered()
        {
            ParityRunner.RunOutcomeParity(
                services => { },
                ctx => Outcome.Result(() => ctx.Provider.GetKeyedService<IKeyedFake>(KeyedService.AnyKey)));
        }

        // An AnyKey registration (type, factory or instance based) must be reachable by a
        // later specific-key resolve, with the registered lifetime honored per actual key.

        [Fact]
        public void AnyKey_Instance_Same_For_Every_Key()
        {
            var instance = new PreBuiltFake();
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake>(KeyedService.AnyKey, instance),
                ctx =>
                {
                    ctx.Provider.GetKeyedService<IKeyedFake>("x").ShouldBeSameAs(instance);
                    ctx.Provider.GetKeyedService<IKeyedFake>("y").ShouldBeSameAs(instance);
                });
        }

        [Fact]
        public void AnyKey_Factory_Singleton_Cached_Per_Key()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake>(
                    KeyedService.AnyKey, (sp, key) => new KeyedFakeKeyCapture(key)),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("x"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("x"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("y"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("y")));
        }

        [Fact]
        public void AnyKey_Type_Singleton_Cached_Per_Key()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("x"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("x"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("y")));
        }

        [Fact]
        public void AnyKey_Type_Scoped_Per_Scope()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedScoped<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx =>
                {
                    IKeyedFake a1 = null, a2 = null, b = null;
                    ctx.InScope(sp =>
                    {
                        a1 = sp.GetKeyedService<IKeyedFake>("k");
                        a2 = sp.GetKeyedService<IKeyedFake>("k");
                    });
                    ctx.InScope(sp => b = sp.GetKeyedService<IKeyedFake>("k"));
                    return Outcome.Order(a1, a2, b);
                });
        }

        [Fact]
        public void AnyKey_Type_Transient_Distinct_Keys_All_Resolve()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("k1"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k2"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k3")));
        }

        // GetKeyedServices<T>(AnyKey): every explicitly-keyed registration of T, in registration
        // order, excluding non-keyed, the AnyKey registration itself and its lazy expansions; the
        // single-service AnyKey resolve still throws; the collection is cached per provider.

        [Fact]
        public void GetKeyedServices_AnyKey_Registration_Order_Not_Grouped_By_Key()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("A");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("B");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>("A");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey)));
        }

        [Fact]
        public void GetKeyedServices_AnyKey_Excludes_NonKeyed_And_NullKey()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>(null);
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey)));
        }

        [Fact]
        public void GetKeyedServices_AnyKey_Excludes_Lazy_Expansion()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("explicit");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeB>(KeyedService.AnyKey);
                },
                ctx =>
                {
                    // Single resolves materialize the AnyKey template for ad-hoc keys; those
                    // expansions must never surface in the AnyKey collection.
                    ctx.Provider.GetKeyedService<IKeyedFake>("ad-hoc-1");
                    ctx.Provider.GetKeyedService<IKeyedFake>("ad-hoc-2");
                    return Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey));
                });
        }

        [Fact]
        public void GetKeyedServices_AnyKey_Nothing_Keyed_Returns_Empty()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddSingleton<IKeyedFake, KeyedFakeA>(),
                ctx => Outcome.ResultMany(() => ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey)));
        }

        [Fact]
        public void GetKeyedServices_AnyKey_Duplicate_Same_Key_All_In_Order()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey)));
        }

        [Fact]
        public void GetKeyedServices_AnyKey_Cached_Same_Instance()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("a");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("b");
                },
                ctx =>
                {
                    var first = ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey);
                    var second = ctx.Provider.GetKeyedServices<IKeyedFake>(KeyedService.AnyKey);
                    first.ShouldBeSameAs(second);
                });
        }

        [Fact]
        public void GetKeyedServices_SpecificKey_Excludes_AnyKey_Reg_Regardless_Of_Order()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeB>(KeyedService.AnyKey);
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k")));
        }

        [Fact]
        public void IsKeyedService_AnyKey_True_When_AnyKey_Registered()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey),
                ctx => Outcome.Result(
                    () => ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), KeyedService.AnyKey)));
        }
    }
}
