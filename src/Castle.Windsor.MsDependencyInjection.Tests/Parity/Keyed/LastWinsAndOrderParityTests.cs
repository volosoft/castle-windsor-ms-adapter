using System.Collections.Generic;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// When several implementations share a key, a single resolve returns the last registration and a
    /// collection resolve returns them all in registration order.
    /// </summary>
    public sealed class LastWinsAndOrderParityTests
    {
        [Fact]
        public void Multiple_Same_Key_Single_Resolve_Is_Last()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => ctx.Provider.GetKeyedService<IKeyedFake>("k").ShouldBeOfType<KeyedFakeC>());
        }

        [Fact]
        public void Multiple_Same_Key_Collection_Is_All_In_Order()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k")));
        }

        [Fact]
        public void Interleaved_Keys_Collection_Filtered_And_Ordered()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeB>("j");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => Outcome.TypeNames(ctx.Provider.GetKeyedServices<IKeyedFake>("k")));
        }

        [Fact]
        public void Order_Preserved_Across_Mixed_Lifetimes_Collection()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeC>("k");
                },
                ctx =>
                {
                    IReadOnlyList<string> result = null;
                    ctx.InScope(sp => result = Outcome.TypeNames(sp.GetKeyedServices<IKeyedFake>("k")));
                    return result;
                });
        }

        [Fact]
        public void Single_Resolve_LastWins_Across_Mixed_Lifetimes()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeB>("k");
                    services.AddKeyedTransient<IKeyedFake, KeyedFakeC>("k");
                },
                ctx => ctx.InScope(sp => sp.GetKeyedService<IKeyedFake>("k").ShouldBeOfType<KeyedFakeC>()));
        }
    }
}
