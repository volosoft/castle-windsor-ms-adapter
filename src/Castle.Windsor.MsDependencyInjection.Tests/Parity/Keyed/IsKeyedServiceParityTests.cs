using System.Collections.Generic;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// <see cref="IServiceProviderIsKeyedService.IsKeyedService"/> across registered/unregistered
    /// keys, null and AnyKey arguments, closed <c>IEnumerable&lt;T&gt;</c>, open generics, and a null
    /// service type.
    /// </summary>
    public sealed class IsKeyedServiceParityTests
    {
        [Fact]
        public void Registered_Specific_Key_Is_True()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), "k").ShouldBeTrue());
        }

        [Fact]
        public void Unregistered_Key_Is_False()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), "x").ShouldBeFalse());
        }

        [Fact]
        public void Null_Key_NonKeyed_Registered_Equals_IsService()
        {
            ParityRunner.RunAssertParity(
                services => services.AddSingleton<IKeyedFake, KeyedFakeA>(),
                ctx =>
                {
                    var isService = ctx.IsService.IsService(typeof(IKeyedFake));
                    ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), null).ShouldBe(isService);
                });
        }

        [Fact]
        public void Null_Key_Only_Keyed_Registered()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Result(() => ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), null)));
        }

        [Fact]
        public void AnyKey_Arg()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Result(
                    () => ctx.IsKeyedService.IsKeyedService(typeof(IKeyedFake), KeyedService.AnyKey)));
        }

        [Fact]
        public void Closed_IEnumerable_Always_Keyed()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Result(() => ctx.IsKeyedService.IsKeyedService(typeof(IEnumerable<IKeyedFake>), "k")));
        }

        [Fact]
        public void OpenGeneric_Definition()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(Repo<>)),
                ctx => Outcome.Result(() => ctx.IsKeyedService.IsKeyedService(typeof(IRepo<>), "k")));
        }

        [Fact]
        public void OpenGeneric_Definition_AnyKey_Backed()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton(typeof(IRepo<>), KeyedService.AnyKey, typeof(Repo<>)),
                ctx => Outcome.Result(() => ctx.IsKeyedService.IsKeyedService(typeof(IRepo<>), "anything")));
        }

        [Fact]
        public void Closed_From_OpenGeneric_Keyed_Reg()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton(typeof(IRepo<>), "k", typeof(Repo<>)),
                ctx => Outcome.Result(() => ctx.IsKeyedService.IsKeyedService(typeof(IRepo<int>), "k")));
        }

        [Fact]
        public void Null_ServiceType_Throws_Same_Type()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx => Outcome.Result(() => ctx.IsKeyedService.IsKeyedService(null, "k")));
        }
    }
}
