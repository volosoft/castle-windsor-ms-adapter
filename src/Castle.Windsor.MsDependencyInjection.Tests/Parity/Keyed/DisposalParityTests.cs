using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Disposal of keyed services across the singleton / scoped / transient lifetimes, externally
    /// supplied instances, <see cref="System.IAsyncDisposable"/>, and dispose ordering. Counts and
    /// ordering are observed through the injected <see cref="DisposeTracker"/>.
    /// </summary>
    public sealed class DisposalParityTests
    {
        [Fact]
        public void Keyed_Singleton_Disposed_At_Root_Dispose()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    ctx.Provider.GetKeyedService<IKeyedFake>("k");
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(0);
                    ctx.DisposeProvider();
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(1);
                });
        }

        [Fact]
        public void Keyed_Singleton_Not_Disposed_At_Scope_Dispose()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("k");
                    }
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(0);
                    ctx.DisposeProvider();
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(1);
                });
        }

        [Fact]
        public void Keyed_Scoped_Disposed_At_Scope_Dispose()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("k");
                        ctx.Disposes.Count<KeyedFakeA>().ShouldBe(0);
                    }
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(1);
                });
        }

        [Fact]
        public void Keyed_Transient_Disposable_Captured_By_Resolving_Scope()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("k");
                    }
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(1);
                });
        }

        [Fact]
        public void Keyed_Transient_From_Root_Disposed_At_Root_Dispose()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    ctx.Provider.GetKeyedService<IKeyedFake>("k");
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(0);
                    ctx.DisposeProvider();
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(1);
                });
        }

        [Fact]
        public void External_Keyed_Instance_Not_Disposed()
        {
            var instance = new PreBuiltFake();
            ParityRunner.RunAssertParity(
                services => services.AddKeyedSingleton<IKeyedFake>("k", instance),
                ctx =>
                {
                    ctx.Provider.GetKeyedService<IKeyedFake>("k");
                    ctx.DisposeProvider();
                    instance.Disposed.ShouldBeFalse();
                });
        }

        [Fact]
        public void Dispose_Order_LIFO_Within_Scope()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("a");
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeB>("b");
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeC>("c");
                },
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("a");
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("b");
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("c");
                    }
                    return ctx.Disposes.Order;
                });
        }

        [Fact]
        public void Nested_Scopes_Child_Disposed_Before_Parent()
        {
            ParityRunner.RunOutcomeParity(
                services =>
                {
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeA>("outer");
                    services.AddKeyedScoped<IKeyedFake, KeyedFakeB>("inner");
                },
                ctx =>
                {
                    using (var outer = ctx.Provider.CreateScope())
                    {
                        outer.ServiceProvider.GetKeyedService<IKeyedFake>("outer");
                        using (var inner = outer.ServiceProvider.CreateScope())
                        {
                            inner.ServiceProvider.GetKeyedService<IKeyedFake>("inner");
                        }
                    }
                    return ctx.Disposes.Order;
                });
        }

        [Fact]
        public void Same_Keyed_Transient_Resolved_Twice_Disposed_Twice()
        {
            ParityRunner.RunAssertParity(
                services => services.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k"),
                ctx =>
                {
                    using (var scope = ctx.Provider.CreateScope())
                    {
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("k");
                        scope.ServiceProvider.GetKeyedService<IKeyedFake>("k");
                    }
                    ctx.Disposes.Count<KeyedFakeA>().ShouldBe(2);
                });
        }
    }
}
