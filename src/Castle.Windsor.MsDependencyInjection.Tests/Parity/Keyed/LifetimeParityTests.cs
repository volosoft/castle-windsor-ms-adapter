using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Keyed lifetime semantics (singleton / scoped / transient) for type-, factory- and
    /// instance-based registrations, including behavior when a scoped service is resolved at the root.
    /// </summary>
    public sealed class LifetimeParityTests
    {
        [Fact]
        public void Keyed_Singleton_Type_Same_Across_Scopes()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    IKeyedFake a = null, b = null;
                    ctx.InScope(sp => a = sp.GetKeyedService<IKeyedFake>("k"));
                    ctx.InScope(sp => b = sp.GetKeyedService<IKeyedFake>("k"));
                    return Outcome.Order(a, b);
                });
        }

        [Fact]
        public void Keyed_Scoped_Type_Same_In_Differ_Across_Scopes()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k"),
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
        public void Keyed_Transient_Type_Always_New()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k")));
        }

        [Fact]
        public void Keyed_Singleton_Factory_Same_Across_Scopes()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake>("k", (sp, key) => new KeyedFakeA(sp.GetRequiredService<DisposeTracker>())),
                ctx =>
                {
                    IKeyedFake a = null, b = null;
                    ctx.InScope(sp => a = sp.GetKeyedService<IKeyedFake>("k"));
                    ctx.InScope(sp => b = sp.GetKeyedService<IKeyedFake>("k"));
                    return Outcome.Order(a, b);
                });
        }

        [Fact]
        public void Keyed_Scoped_Factory_Scoping()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedScoped<IKeyedFake>("k", (sp, key) => new KeyedFakeA(sp.GetRequiredService<DisposeTracker>())),
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
        public void Keyed_Transient_Factory_Always_New()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient<IKeyedFake>("k", (sp, key) => new KeyedFakeA(sp.GetRequiredService<DisposeTracker>())),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k")));
        }

        [Fact]
        public void Keyed_Instance_Always_Same()
        {
            var instance = new PreBuiltFake();
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake>("k", instance),
                ctx =>
                {
                    ctx.Provider.GetKeyedService<IKeyedFake>("k").ShouldBeSameAs(instance);
                    ctx.Provider.GetKeyedService<IKeyedFake>("k").ShouldBeSameAs(instance);
                });
        }

        [Fact]
        public void Keyed_Scoped_Resolved_At_Root_Identity()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IKeyedFake>("k"),
                    ctx.Provider.GetKeyedService<IKeyedFake>("k")));
        }
    }
}
