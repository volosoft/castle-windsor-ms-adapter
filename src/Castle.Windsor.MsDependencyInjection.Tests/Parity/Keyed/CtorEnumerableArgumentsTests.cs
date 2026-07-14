using System.Collections.Generic;
using System.Linq;
using Castle.MicroKernel;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Windsor-only: a runtime argument passed to <c>container.Resolve</c> must flow to the items of a
    /// constructor-injected <c>IEnumerable&lt;T&gt;</c>, matching the base collection behaviour
    /// (<c>kernel.ResolveAll(itemType, context.AdditionalArguments)</c>). MS DI has no runtime-argument
    /// API, so this is asserted against Windsor only. Before the fix the resolver re-resolved each item
    /// by name without the arguments, so the runtime value was dropped and the resolution failed.
    /// </summary>
    public sealed class CtorEnumerableArgumentsTests
    {
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
            public WorkerConsumer(IEnumerable<IWorker> workers) => Workers = workers.ToList();

            public IReadOnlyList<IWorker> Workers { get; }
        }

        [Fact]
        public void CtorInjected_Collection_Flows_Runtime_AdditionalArguments()
        {
            var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddTransient<IWorker, Worker>();
            // Unrelated keyed registration of the same service type forces the resolver's manual path
            // (the one that used to drop the runtime arguments), so this pins the arguments flow there.
            services.AddKeyedTransient<IWorker, Worker>("unrelated");
            services.AddTransient<WorkerConsumer>();
            WindsorRegistrationHelper.CreateServiceProvider(container, services);

            var consumer = container.Resolve<WorkerConsumer>(new Arguments().AddNamed("token", "abc"));

            consumer.Workers.Single().Token.ShouldBe("abc");
        }
    }
}
