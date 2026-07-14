using System;
using System.Collections.Generic;
using System.Linq;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Windsor-only regression for the circular-dependency fix in <see cref="MsCompatibleCollectionResolver"/>.
    /// <para>
    /// When a non-keyed component is injected with <c>IEnumerable&lt;T&gt;</c> and, while being
    /// constructed, that same collection is resolved again (as Microsoft.Identity.Web's
    /// OpenIdConnectOptions configuration chain does), the resolver must skip the handler that is
    /// already in flight - the same way <c>DefaultKernel.ResolveAll</c> does - instead of issuing a
    /// fresh named resolve that re-enters the handler and throws <c>CircularDependencyException</c>.
    /// </para>
    /// <para>
    /// The key isolation guarantee: adding an <b>unrelated keyed</b> registration of the same service
    /// type must not change whether the non-keyed collection resolves. Before the fix, any keyed
    /// registration flipped the whole collection onto a manual path that lacked the in-flight skip, so
    /// the second case below threw. These graphs are asserted only against Windsor: the equivalent MS
    /// DI graph is a hard cycle there, so they are intentionally not parity scenarios.
    /// </para>
    /// </summary>
    public sealed class CtorEnumerableReentrancyTests
    {
        public interface IPlugin { }

        // Non-keyed plugin whose construction re-resolves the same IEnumerable<IPlugin> (via Helper).
        public sealed class ReentrantPlugin : IPlugin
        {
            public ReentrantPlugin(Helper helper) => Helper = helper;

            public Helper Helper { get; }
        }

        // Pulls IEnumerable<IPlugin> during ReentrantPlugin's construction; the in-flight plugin must
        // be skipped, leaving Helper with the remaining (here: empty) set rather than dead-locking.
        public sealed class Helper
        {
            public Helper(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToList();

            public IReadOnlyList<IPlugin> Plugins { get; }
        }

        // Top-level consumer that triggers the collection resolver.
        public sealed class TopConsumer
        {
            public TopConsumer(IEnumerable<IPlugin> plugins) => Plugins = plugins.ToList();

            public IReadOnlyList<IPlugin> Plugins { get; }
        }

        private static IServiceProvider BuildWindsor(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            configure(services);
            return WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);
        }

        [Fact]
        public void Reentrant_NonKeyed_Collection_Skips_InFlight_Handler()
        {
            var provider = BuildWindsor(services =>
            {
                services.AddSingleton<Helper>();
                services.AddSingleton<IPlugin, ReentrantPlugin>();
                services.AddSingleton<TopConsumer>();
            });

            var plugin = provider.GetRequiredService<TopConsumer>().Plugins.Single().ShouldBeOfType<ReentrantPlugin>();
            // The re-entrant resolve inside Helper must have skipped the in-flight ReentrantPlugin,
            // not just avoided throwing - so Helper sees an empty collection.
            plugin.Helper.Plugins.ShouldBeEmpty();
        }

        [Fact]
        public void Reentrant_NonKeyed_Collection_With_Unrelated_Keyed_Still_Resolves()
        {
            var provider = BuildWindsor(services =>
            {
                services.AddSingleton<Helper>();
                services.AddSingleton<IPlugin, ReentrantPlugin>();
                // An unrelated keyed registration of the same service type: it never belongs to the
                // non-keyed collection, so its mere presence must not reintroduce the cycle.
                services.AddKeyedSingleton<IPlugin, ReentrantPlugin>("unrelated");
                services.AddSingleton<TopConsumer>();
            });

            var plugin = provider.GetRequiredService<TopConsumer>().Plugins.Single().ShouldBeOfType<ReentrantPlugin>();
            plugin.Helper.Plugins.ShouldBeEmpty();
        }
    }
}
