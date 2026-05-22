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

        // Regression: scoped keyed services injected via [FromKeyedServices] used to be silently
        // downgraded to transient because the sub-resolver routed through a ScopedWindsorServiceProvider
        // captured with a null MsLifetimeScope, which then overwrote AsyncLocal.Current to null
        // during keyed lookup. MsScopedLifestyleManager.Resolve falls back to transient when
        // Current is null, so two consumers inside the same scope each received a fresh instance.
        [Fact]
        public void Keyed_Scoped_FromKeyedServices_SameInstanceWithinScope()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedCtorConsumer>();
                },
                ctx =>
                {
                    IKeyedFake a = null, b = null;
                    ctx.InScope(sp =>
                    {
                        a = sp.GetRequiredService<FromKeyedCtorConsumer>().Dep;
                        b = sp.GetRequiredService<FromKeyedCtorConsumer>().Dep;
                    });
                    return Outcome.Order(a, b);
                });
        }

        [Fact]
        public void Keyed_Scoped_FromKeyedServices_DistinctAcrossScopes()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedCtorConsumer>();
                },
                ctx =>
                {
                    IKeyedFake a = null, b = null;
                    ctx.InScope(sp => a = sp.GetRequiredService<FromKeyedCtorConsumer>().Dep);
                    ctx.InScope(sp => b = sp.GetRequiredService<FromKeyedCtorConsumer>().Dep);
                    return Outcome.Order(a, b);
                });
        }

        // Inherit-key path: parent and child are both scoped keyed; child uses parameterless
        // [FromKeyedServices] to inherit the parent's key. Within one scope two distinct parents
        // must produce two distinct children that are still scope-local.
        [Fact]
        public void Keyed_Scoped_FromKeyedServices_Inherit_SameInstanceWithinScope()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedScoped<IInheritChild, InheritChild>("k");
                    services.AddKeyedScoped<IInheritParent, InheritKeyParent>("k");
                },
                ctx =>
                {
                    IInheritChild a = null, b = null;
                    ctx.InScope(sp =>
                    {
                        a = sp.GetRequiredKeyedService<IInheritParent>("k").Child;
                        b = sp.GetRequiredKeyedService<IInheritParent>("k").Child;
                    });
                    return Outcome.Order(a, b);
                });
        }
    }
}
