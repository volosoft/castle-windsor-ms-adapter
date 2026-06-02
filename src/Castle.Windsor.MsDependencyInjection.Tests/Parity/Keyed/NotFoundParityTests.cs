using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Resolution of missing keyed services: the optional API returns null / an empty sequence, the
    /// required API throws, and the thrown exception type is compared against MS DI.
    /// </summary>
    public sealed class NotFoundParityTests
    {
        [Fact]
        public void GetRequiredKeyedService_None_Registered_Throws_Same_Type()
        {
            ParityRunner.RunOutcomeParity(
                services => { },
                ctx => Outcome.Result(() => ctx.Provider.GetRequiredKeyedService<IKeyedFake>("k")));
        }

        [Fact]
        public void GetRequiredKeyedService_Wrong_Key_Throws_Same_Type()
        {
            ParityRunner.RunOutcomeParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("y"),
                ctx => Outcome.Result(() => ctx.Provider.GetRequiredKeyedService<IKeyedFake>("x")));
        }

        [Fact]
        public void GetKeyedService_Missing_Returns_Null_No_Throw()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("y"),
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>("missing").ShouldBeNull());
        }

        [Fact]
        public void GetKeyedServices_Missing_Returns_Empty_Not_Null()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("y"),
                ctx =>
                {
                    var all = ctx.Provider.GetKeyedServices<IKeyedFake>("missing");
                    all.ShouldNotBeNull();
                    all.ShouldBeEmpty();
                });
        }

        [Fact]
        public void GetRequiredKeyedService_Null_Key_None_Registered_ThrowType()
        {
            ParityRunner.RunOutcomeParity(
                services => { },
                ctx => Outcome.Result(() => ctx.Provider.GetRequiredKeyedService<IKeyedFake>(null)));
        }
    }
}
