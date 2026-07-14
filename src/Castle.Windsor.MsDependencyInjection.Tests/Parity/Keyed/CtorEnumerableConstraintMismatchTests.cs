using System;
using System.Collections.Generic;
using System.Linq;
using Castle.Core;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Handlers;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Windsor-only: when every non-keyed handler for a ctor-injected <c>IEnumerable&lt;T&gt;</c> is an
    /// open generic whose constraints reject the closed item type, each handler must be attempted
    /// exactly once - like <c>DefaultKernel.ResolveAll</c>. The empty result must not defer to
    /// <c>kernel.ResolveAll</c> in that case, because the deferred call would run the same handlers
    /// (and their <see cref="IGenericImplementationMatchingStrategy"/>) a second time. The counting
    /// strategy below observes every close attempt, so it directly detects a double invocation.
    /// (Trade-off: skipping the defer also skips <c>EmptyCollectionResolving</c> here; see the notes
    /// on <c>ServiceResolveHelper.ResolveNonKeyedCollectionInContext</c>.)
    /// </summary>
    public sealed class CtorEnumerableConstraintMismatchTests
    {
        public interface IRepo<T> { }

        public sealed class ClassConstrainedRepo<T> : IRepo<T> where T : class { }

        public sealed class RepoConsumer
        {
            public RepoConsumer(IEnumerable<IRepo<int>> repos) => Repos = repos.ToList();

            public IReadOnlyList<IRepo<int>> Repos { get; }
        }

        public sealed class FromKeyedNullRepoConsumer
        {
            public FromKeyedNullRepoConsumer([FromKeyedServices(null)] IEnumerable<IRepo<int>> repos) => Repos = repos.ToList();

            public IReadOnlyList<IRepo<int>> Repos { get; }
        }

        /// <summary>
        /// Counts the close attempts on the open-generic handler. Returning the requested closed
        /// type's arguments (here: int) makes MakeGenericType fail the T : class constraint, which
        /// Castle surfaces as GenericHandlerTypeMismatchException - the "skip this handler" signal.
        /// </summary>
        private sealed class CountingMatchingStrategy : IGenericImplementationMatchingStrategy
        {
            public int Calls { get; private set; }

            public Type[] GetGenericArguments(ComponentModel model, CreationContext context)
            {
                Calls++;
                return context.GenericArguments;
            }
        }

        [Fact]
        public void CtorInjected_AllMismatch_NonKeyed_Enumerable_Attempts_Each_Handler_Once()
        {
            var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<RepoConsumer>();
            WindsorRegistrationHelper.CreateServiceProvider(container, services);

            var strategy = new CountingMatchingStrategy();
            container.Register(
                Component.For(typeof(IRepo<>))
                    .ImplementedBy(typeof(ClassConstrainedRepo<>), strategy));

            var raised = false;
            container.Kernel.EmptyCollectionResolving += _ => raised = true;

            container.Resolve<RepoConsumer>().Repos.ShouldBeEmpty();

            strategy.Calls.ShouldBe(1);
            // No defer means no EmptyCollectionResolving here - the deliberate cost of not invoking
            // the mismatched handler twice. The event still fires when nothing was attempted at all
            // (no handlers / all in flight) - covered by CtorEnumerableEmptyEventTests.
            raised.ShouldBeFalse();
        }

        [Fact]
        public void FromKeyedNull_AllMismatch_Enumerable_Attempts_Each_Handler_Once()
        {
            var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddSingleton<FromKeyedNullRepoConsumer>();
            WindsorRegistrationHelper.CreateServiceProvider(container, services);

            var strategy = new CountingMatchingStrategy();
            container.Register(
                Component.For(typeof(IRepo<>))
                    .ImplementedBy(typeof(ClassConstrainedRepo<>), strategy));

            container.Resolve<FromKeyedNullRepoConsumer>().Repos.ShouldBeEmpty();

            strategy.Calls.ShouldBe(1);
        }
    }
}
