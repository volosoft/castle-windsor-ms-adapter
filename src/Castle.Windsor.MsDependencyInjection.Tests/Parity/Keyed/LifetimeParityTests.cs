using System.Threading.Tasks;
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

        // Cross-thread within the same scope: two threads both pull from the SAME
        // IServiceProvider (the scope's). The scoped keyed dep injected via [FromKeyedServices]
        // must be the SAME instance on both threads. Probes the MsLifetimeScope AsyncLocal
        // behavior: the scope ServiceProvider's OwnMsLifetimeScope is shared, so even if each
        // worker thread starts with a different ambient Current, the scope's GetService path
        // pushes the right scope before resolving.
        [Fact]
        public void Keyed_Scoped_FromKeyedServices_SameInstanceAcrossThreadsInSameScope()
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
                        var t1 = Task.Run(() => a = sp.GetRequiredService<FromKeyedCtorConsumer>().Dep);
                        var t2 = Task.Run(() => b = sp.GetRequiredService<FromKeyedCtorConsumer>().Dep);
                        Task.WaitAll(t1, t2);
                    });
                    return Outcome.Order(a, b);
                });
        }

        // Nested scope: child scope created from a parent scope must produce a DIFFERENT scoped
        // keyed instance than the parent scope. Common ABP pattern: root -> tenant -> request.
        [Fact]
        public void Keyed_Scoped_FromKeyedServices_NestedScope_DistinctInstances()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k");
                    services.AddTransient<FromKeyedCtorConsumer>();
                },
                ctx =>
                {
                    IKeyedFake outer = null, inner = null, outerAgain = null;
                    using (var s1 = ctx.Provider.CreateScope())
                    {
                        outer = s1.ServiceProvider.GetRequiredService<FromKeyedCtorConsumer>().Dep;
                        using (var s2 = s1.ServiceProvider.CreateScope())
                        {
                            inner = s2.ServiceProvider.GetRequiredService<FromKeyedCtorConsumer>().Dep;
                        }
                        outerAgain = s1.ServiceProvider.GetRequiredService<FromKeyedCtorConsumer>().Dep;
                    }
                    // Expect: outer == outerAgain (same parent scope), inner != outer (child scope is its own).
                    return Outcome.Order(outer, inner, outerAgain);
                });
        }

        // Complex key: records and other reference types with value-based equality must work as
        // keys. KeyedServiceRegistry compares with Equals(...), so record value equality should
        // let two distinct instances of the same record value resolve to the same registration.
        [Fact]
        public void Keyed_ComplexKey_Record_ValueEquality_Works()
        {
            var keyA1 = new TenantKey(1);
            var keyA2 = new TenantKey(1);
            var keyB = new TenantKey(2);

            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>(keyA1);
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>(keyB);
                },
                ctx => Outcome.TypeNames(new object[]
                {
                    ctx.Provider.GetKeyedService<IKeyedFake>(keyA1),
                    ctx.Provider.GetKeyedService<IKeyedFake>(keyA2),
                    ctx.Provider.GetKeyedService<IKeyedFake>(keyB),
                    ctx.Provider.GetKeyedService<IKeyedFake>(new TenantKey(3)),
                }));
        }

        public sealed record TenantKey(int Id);
    }
}
