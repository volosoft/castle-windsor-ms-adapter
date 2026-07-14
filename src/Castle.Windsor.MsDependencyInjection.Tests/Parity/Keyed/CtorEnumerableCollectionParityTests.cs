using System.Collections.Generic;
using System.Linq;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Covers <see cref="MsCompatibleCollectionResolver"/> through the path it actually serves:
    /// a component that receives <c>IEnumerable&lt;T&gt;</c> by constructor injection. The existing
    /// isolation tests exercise the direct <c>GetServices&lt;T&gt;()</c> provider call, which does
    /// not go through this sub-dependency resolver, so the ctor-injection contract was uncovered.
    /// <para>
    /// The resolver mirrors <c>DefaultKernel.ResolveAll</c> (registration order, in-flight handler
    /// skip, open-generic constraint skip) while dropping keyed components, so these tests assert the
    /// ctor-injected collection stays in parity with MS DI for order, keyed isolation, empty results
    /// and constraint-mismatched open generics.
    /// </para>
    /// </summary>
    public sealed class CtorEnumerableCollectionParityTests
    {
        /// <summary>Consumes the collection via constructor injection, which is what triggers the resolver.</summary>
        public sealed class CollectionConsumer
        {
            public CollectionConsumer(IEnumerable<IKeyedFake> items) => Items = items.ToList();

            public IReadOnlyList<IKeyedFake> Items { get; }
        }

        /// <summary>Ctor consumer for a closed generic service, to exercise open-generic handlers.</summary>
        public sealed class RepoConsumer
        {
            public RepoConsumer(IEnumerable<IRepo<int>> repos) => Repos = repos.ToList();

            public IReadOnlyList<IRepo<int>> Repos { get; }
        }

        [Fact]
        public void CtorInjected_NonKeyed_Enumerable_Preserves_RegistrationOrder()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddSingleton<IKeyedFake, KeyedFakeB>();
                    services.AddSingleton<IKeyedFake, KeyedFakeC>();
                    services.AddSingleton<CollectionConsumer>();
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetRequiredService<CollectionConsumer>().Items));
        }

        [Fact]
        public void CtorInjected_NonKeyed_Enumerable_Excludes_Keyed_And_Preserves_Order()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<IKeyedFake, KeyedFakeA>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
                    services.AddSingleton<IKeyedFake, KeyedFakeC>();
                    services.AddSingleton<CollectionConsumer>();
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetRequiredService<CollectionConsumer>().Items));
        }

        [Fact]
        public void CtorInjected_NonKeyed_Enumerable_Uses_Exact_ServiceType_Not_Assignable()
        {
            // KeyedFakeA is registered under its concrete type only. MS DI contributes a descriptor to
            // IEnumerable<T> only when its ServiceType is exactly T, so KeyedFakeA is NOT part of
            // IEnumerable<IKeyedFake>; only the descriptor registered as IKeyedFake (KeyedFakeB) is.
            // The unrelated keyed registration forces the resolver's manual (keyed-filtering) path,
            // where the old GetAssignableHandlers set would have wrongly pulled in the concrete KeyedFakeA.
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton<KeyedFakeA>();
                    services.AddSingleton<IKeyedFake, KeyedFakeB>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeC>("unrelated");
                    services.AddSingleton<CollectionConsumer>();
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetRequiredService<CollectionConsumer>().Items));
        }

        [Fact]
        public void CtorInjected_OnlyKeyed_Registrations_Yield_Empty_NonKeyed_Enumerable()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("a");
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("b");
                    services.AddSingleton<CollectionConsumer>();
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetRequiredService<CollectionConsumer>().Items));
        }

        [Fact]
        public void CtorInjected_Collection_Transient_Disposables_Owned_By_Resolving_Scope()
        {
            // A transient disposable collected through a ctor IEnumerable<T> must be owned by the scope
            // that built the consumer and disposed with it - the burden ownership the manual path must
            // preserve. The unrelated keyed registration forces that manual path.
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddTransient<IKeyedFake, KeyedFakeA>();
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("unrelated");
                    services.AddScoped<CollectionConsumer>();
                },
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetRequiredService<CollectionConsumer>().Items.Count.ShouldBe(1);
                        ctx.Disposes.Count<KeyedFakeA>().ShouldBe(0);
                    }

                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(1);
                });
        }

        [Fact]
        public void CtorInjected_NonKeyed_Enumerable_Skips_ConstraintMismatched_OpenGeneric()
        {
            // ClassConstrainedRepo<T> requires T : class, so it cannot close over int and must be
            // skipped (GenericHandlerTypeMismatchException), exactly as MS DI skips it. A keyed
            // registration is present so the resolver runs its keyed-filtering path as well.
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddSingleton(typeof(IRepo<>), typeof(Repo<>));
                    services.AddSingleton(typeof(IRepo<>), typeof(ClassConstrainedRepo<>));
                    services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(OtherRepo<>));
                    services.AddSingleton<RepoConsumer>();
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetRequiredService<RepoConsumer>().Repos));
        }
    }
}
