using System;
using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// The non-keyed <c>IEnumerable&lt;T&gt;</c> branch of <c>KeyedServicesSubResolver</c>
    /// (<c>[FromKeyedServices(null)]</c> and inherited-null <c>[FromKeyedServices]</c> constructor
    /// parameters) must share the semantics of <see cref="MsCompatibleCollectionResolver"/>: skip the
    /// handler already in flight instead of reporting a false cycle, flow the caller's runtime
    /// <c>AdditionalArguments</c> to the items, attach each item's burden to the consumer so disposal
    /// follows the resolving scope, and raise <c>EmptyCollectionResolving</c> for an empty result.
    /// Before the paths were unified this branch re-resolved each item by name, which lost all of
    /// these. Re-entrancy / arguments / empty-event graphs are Windsor-only (MS DI has no equivalent:
    /// the re-entrant graph is a hard cycle there and it has no runtime-argument or empty-event API);
    /// the disposal scenario is asserted as parity.
    /// </summary>
    public sealed class FromKeyedNullEnumerableTests
    {
        public interface IPlugin { }

        // Non-keyed plugin whose construction re-resolves the same IEnumerable<IPlugin> (via Helper).
        public sealed class ReentrantPlugin : IPlugin
        {
            public ReentrantPlugin(Helper helper) => Helper = helper;

            public Helper Helper { get; }
        }

        // Pulls the collection through the [FromKeyedServices(null)] sub-resolver branch during
        // ReentrantPlugin's construction; the in-flight plugin must be skipped, leaving the
        // remaining (here: empty) set rather than throwing CircularDependencyException.
        public sealed class Helper
        {
            public Helper([FromKeyedServices(null)] IEnumerable<IPlugin> plugins) => Plugins = plugins.ToList();

            public IReadOnlyList<IPlugin> Plugins { get; }
        }

        public sealed class NullKeyTopConsumer
        {
            public NullKeyTopConsumer([FromKeyedServices(null)] IEnumerable<IPlugin> plugins) => Plugins = plugins.ToList();

            public IReadOnlyList<IPlugin> Plugins { get; }
        }

        // Parameterless [FromKeyedServices] on a non-keyed component inherits a null key, so it takes
        // the same non-keyed branch as [FromKeyedServices(null)].
        public sealed class InheritNullTopConsumer
        {
            public InheritNullTopConsumer([FromKeyedServices] IEnumerable<IPlugin> plugins) => Plugins = plugins.ToList();

            public IReadOnlyList<IPlugin> Plugins { get; }
        }

        private static IServiceProvider BuildWindsor(WindsorContainer container, Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            configure(services);
            return WindsorRegistrationHelper.CreateServiceProvider(container, services);
        }

        [Fact]
        public void FromKeyedNull_Reentrant_Collection_Skips_InFlight_Handler()
        {
            var provider = BuildWindsor(new WindsorContainer(), services =>
            {
                services.AddSingleton<Helper>();
                services.AddSingleton<IPlugin, ReentrantPlugin>();
                services.AddSingleton<NullKeyTopConsumer>();
            });

            var plugin = provider.GetRequiredService<NullKeyTopConsumer>().Plugins.Single().ShouldBeOfType<ReentrantPlugin>();
            // The re-entrant resolve inside Helper must have skipped the in-flight ReentrantPlugin,
            // not just avoided throwing - so Helper sees an empty collection.
            plugin.Helper.Plugins.ShouldBeEmpty();
        }

        [Fact]
        public void InheritNull_Reentrant_Collection_Skips_InFlight_Handler()
        {
            var provider = BuildWindsor(new WindsorContainer(), services =>
            {
                services.AddSingleton<Helper>();
                services.AddSingleton<IPlugin, ReentrantPlugin>();
                services.AddSingleton<InheritNullTopConsumer>();
            });

            var plugin = provider.GetRequiredService<InheritNullTopConsumer>().Plugins.Single().ShouldBeOfType<ReentrantPlugin>();
            plugin.Helper.Plugins.ShouldBeEmpty();
        }

        public interface IWorker
        {
            string Token { get; }
        }

        public sealed class Worker : IWorker
        {
            public Worker(string token) => Token = token;

            public string Token { get; }
        }

        public sealed class WorkerConsumer
        {
            public WorkerConsumer([FromKeyedServices(null)] IEnumerable<IWorker> workers) => Workers = workers.ToList();

            public IReadOnlyList<IWorker> Workers { get; }
        }

        [Fact]
        public void FromKeyedNull_Collection_Flows_Runtime_AdditionalArguments()
        {
            var container = new WindsorContainer();
            BuildWindsor(container, services =>
            {
                services.AddTransient<IWorker, Worker>();
                services.AddTransient<WorkerConsumer>();
            });

            var consumer = container.Resolve<WorkerConsumer>(new Arguments().AddNamed("token", "abc"));

            consumer.Workers.Single().Token.ShouldBe("abc");
        }

        public interface IDisposableItem { }

        public sealed class DisposableItem : IDisposableItem, IDisposable
        {
            private readonly DisposeTracker _tracker;

            public DisposableItem(DisposeTracker tracker) => _tracker = tracker;

            public void Dispose() => _tracker.Record(this);
        }

        public sealed class DisposableItemConsumer
        {
            public DisposableItemConsumer([FromKeyedServices(null)] IEnumerable<IDisposableItem> items) => Items = items.ToList();

            public IReadOnlyList<IDisposableItem> Items { get; }
        }

        [Fact]
        public void FromKeyedNull_Transient_Disposable_Item_Disposed_With_Resolving_Scope()
        {
            ParityRunner.RunAssertParity(
                services =>
                {
                    services.AddTransient<IDisposableItem, DisposableItem>();
                    services.AddTransient<DisposableItemConsumer>();
                },
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetRequiredService<DisposableItemConsumer>().Items.Count.ShouldBe(1);
                        ctx.Disposes.Count<DisposableItem>().ShouldBe(0);
                    }
                    // The item's ownership must follow the consumer it was injected into: disposed
                    // exactly once when the resolving scope goes, not leaked to the root container.
                    ctx.Disposes.Count<DisposableItem>().ShouldBe(1);
                    ctx.DisposeProvider();
                    ctx.Disposes.Count<DisposableItem>().ShouldBe(1);
                });
        }

        public interface INothing { }

        public sealed class NothingConsumer
        {
            public NothingConsumer([FromKeyedServices(null)] IEnumerable<INothing> items) => Items = items.ToList();

            public IReadOnlyList<INothing> Items { get; }
        }

        [Fact]
        public void FromKeyedNull_Empty_Collection_Raises_EmptyCollectionResolving()
        {
            var container = new WindsorContainer();
            var provider = BuildWindsor(container, services => services.AddTransient<NothingConsumer>());

            Type raisedFor = null;
            container.Kernel.EmptyCollectionResolving += type => raisedFor = type;

            provider.GetRequiredService<NothingConsumer>().Items.ShouldBeEmpty();

            raisedFor.ShouldBe(typeof(INothing));
        }
    }
}
