using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Open-generic keyed registrations: closed resolution, collections, lifetimes,
    /// <c>[ServiceKey]</c> on a generic implementation, and a closed-specific registration alongside
    /// an open-generic one.
    /// </summary>
    public sealed class OpenGenericKeyedParityTests
    {
        [Fact]
        public void Resolve_Closed_From_OpenGeneric_Keyed()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(Repo<>)),
                ctx => ctx.Provider.GetKeyedService<IRepo<int>>("k").ShouldBeOfType<Repo<int>>());
        }

        [Fact]
        public void OpenGeneric_Keyed_Collection_Ordered()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(Repo<>));
                    services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(OtherRepo<>));
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IRepo<int>>("k")));
        }

        [Fact]
        public void OpenGeneric_Keyed_Scoped_Scoping()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedScoped(typeof(IRepo<>), "k", typeof(Repo<>)),
                ctx =>
                {
                    IRepo<int> a1 = null, a2 = null, b = null;
                    ctx.InScope(sp =>
                    {
                        a1 = sp.GetKeyedService<IRepo<int>>("k");
                        a2 = sp.GetKeyedService<IRepo<int>>("k");
                    });
                    ctx.InScope(sp => b = sp.GetKeyedService<IRepo<int>>("k"));
                    return Outcome.Order(a1, a2, b);
                });
        }

        [Fact]
        public void OpenGeneric_Keyed_Transient_Always_New()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient(typeof(IRepo<>), "k", typeof(Repo<>)),
                ctx => Outcome.Order(
                    ctx.Provider.GetKeyedService<IRepo<int>>("k"),
                    ctx.Provider.GetKeyedService<IRepo<int>>("k"),
                    ctx.Provider.GetKeyedService<IRepo<int>>("k")));
        }

        [Fact]
        public void OpenGeneric_Keyed_With_ServiceKey()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedTransient(typeof(IRepo<>), "k", typeof(KeyAwareRepo<>)),
                ctx => Outcome.Result(
                    () => ((KeyAwareRepo<int>)ctx.Provider.GetKeyedService<IRepo<int>>("k")).Key));
        }

        [Fact]
        public void Closed_Specific_Overrides_OpenGeneric_LastWins()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(Repo<>));
                    services.AddKeyedSingleton<IRepo<int>, SpecialIntRepo>("k");
                },
                ctx => Outcome.Result(() => ctx.Provider.GetKeyedService<IRepo<int>>("k")));
        }

        [Fact]
        public void OpenGeneric_Keyed_Collection_Skips_Constraint_Mismatch()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(Repo<>));
                    services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(ClassConstrainedRepo<>));
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IRepo<int>>("k")));
        }
    }
}
