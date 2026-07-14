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
    /// Windsor-only: a ctor-injected <c>IEnumerable&lt;T&gt;</c> that resolves empty must still raise
    /// Castle's <c>EmptyCollectionResolving</c> event, the way the base collection resolver does - some
    /// native Windsor apps subscribe to it to lazily register a fallback for an empty collection. The
    /// manual keyed-filtering path defers to the base resolver when the result is empty and no keyed
    /// handler is present, so the event is preserved.
    /// </summary>
    public sealed class CtorEnumerableEmptyEventTests
    {
        public interface INothing { }

        public sealed class NothingConsumer
        {
            public NothingConsumer(IEnumerable<INothing> items) => Items = items.ToList();

            public IReadOnlyList<INothing> Items { get; }
        }

        [Fact]
        public void CtorInjected_Empty_NonKeyed_Enumerable_Raises_EmptyCollectionResolving()
        {
            var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddTransient<NothingConsumer>();
            WindsorRegistrationHelper.CreateServiceProvider(container, services);

            Type raisedFor = null;
            container.Kernel.EmptyCollectionResolving += type => raisedFor = type;

            container.Resolve<NothingConsumer>().Items.ShouldBeEmpty();

            raisedFor.ShouldBe(typeof(INothing));
        }
    }
}
