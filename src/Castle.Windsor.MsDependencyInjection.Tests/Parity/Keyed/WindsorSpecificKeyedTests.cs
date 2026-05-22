using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Castle.Windsor.MsDependencyInjection.Tests.TestClasses;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    /// <summary>
    /// Windsor-specific keyed behavior that has no Microsoft.Extensions.DependencyInjection
    /// equivalent: interceptor coexistence, concurrent AnyKey expansion, container <c>Release</c>,
    /// resolving a scoped keyed service from the root, and scope cleanup after disposal.
    /// </summary>
    public sealed class WindsorSpecificKeyedTests
    {
        private static (IServiceProvider sp, DisposeTracker tracker) BuildWindsor(Action<IServiceCollection> configure)
        {
            var services = new ServiceCollection();
            var tracker = new DisposeTracker();
            services.AddSingleton(tracker);
            configure(services);
            return (WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services), tracker);
        }

        [Fact]
        public void Keyed_Coexists_With_Castle_Interceptor_And_Disposes()
        {
            var (sp, tracker) = BuildWindsor(s => s.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k"));
            var windsorContainer = sp.GetService<IWindsorContainer>();

            windsorContainer.Register(
                Component.For<MyTestInterceptor>().LifestyleTransient(),
                Component.For<INavigationFixer>().ImplementedBy<NavigationFixer>()
                    .Interceptors<MyTestInterceptor>().LifestyleTransient());

            var proxied = windsorContainer.Resolve<INavigationFixer>();
            proxied.GetType().Namespace.ShouldBe("Castle.Proxies");

            using (var scope = sp.CreateScope())
            {
                scope.ServiceProvider.GetKeyedService<IKeyedFake>("k").ShouldBeOfType<KeyedFakeA>();
            }

            tracker.Count<KeyedFakeA>().ShouldBe(1);
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void Concurrent_First_Resolution_AnyKey_Expansion_Is_ThreadSafe()
        {
            var (sp, _) = BuildWindsor(s => s.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey));

            var results = new ConcurrentBag<IKeyedFake>();
            var errors = new ConcurrentBag<Exception>();

            Parallel.For(0, 200, _ =>
            {
                try
                {
                    results.Add(sp.GetKeyedService<IKeyedFake>("k"));
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            errors.ShouldBeEmpty();
            results.Count.ShouldBe(200);
            results.ShouldAllBe(r => r is KeyedFakeA);

            // The AnyKey template must expand to exactly one Windsor component for key "k".
            var windsorContainer = sp.GetService<IWindsorContainer>();
            windsorContainer.Kernel.GetHandlers(typeof(IKeyedFake)).Length.ShouldBe(1);
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void Concurrent_AnyKey_Distinct_Keys_Expand_Once_Each()
        {
            var (sp, _) = BuildWindsor(s => s.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey));

            var errors = new ConcurrentBag<Exception>();
            Parallel.For(0, 50, i =>
            {
                try
                {
                    sp.GetKeyedService<IKeyedFake>("k" + i).ShouldNotBeNull();
                }
                catch (Exception ex)
                {
                    errors.Add(ex);
                }
            });

            errors.ShouldBeEmpty();

            var windsorContainer = sp.GetService<IWindsorContainer>();
            windsorContainer.Kernel.GetHandlers(typeof(IKeyedFake)).Length.ShouldBe(50);

            // Re-resolving an already-expanded key reuses the same component (no new handler).
            sp.GetKeyedService<IKeyedFake>("k0").ShouldNotBeNull();
            windsorContainer.Kernel.GetHandlers(typeof(IKeyedFake)).Length.ShouldBe(50);
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void Container_Release_On_Keyed_Transient_Disposes_It()
        {
            var (sp, tracker) = BuildWindsor(s => s.AddKeyedTransient<IKeyedFake, KeyedFakeA>("k"));
            var windsorContainer = sp.GetService<IWindsorContainer>();

            var instance = (KeyedFakeA)sp.GetKeyedService<IKeyedFake>("k");
            instance.IsDisposed.ShouldBeFalse();

            windsorContainer.Release(instance);

            instance.IsDisposed.ShouldBeTrue();
            tracker.Count<KeyedFakeA>().ShouldBe(1);
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void Keyed_Scoped_At_Root_Resolves_Without_Scope_Validation()
        {
            // Windsor has no scope-validation equivalent: a scoped keyed service resolves from the
            // root without throwing.
            var (sp, _) = BuildWindsor(s => s.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k"));

            sp.GetKeyedService<IKeyedFake>("k").ShouldNotBeNull();
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void MsLifetimeScope_Current_Null_After_Disposal_With_Keyed_Regs()
        {
            var (sp, _) = BuildWindsor(s =>
            {
                s.AddKeyedSingleton<IKeyedFake, KeyedFakeA>("k");
                s.AddKeyedTransient<IKeyedFake, KeyedFakeB>(KeyedService.AnyKey);
            });

            using (var scope = sp.CreateScope())
            {
                scope.ServiceProvider.GetKeyedService<IKeyedFake>("k").ShouldNotBeNull();
                scope.ServiceProvider.GetKeyedService<IKeyedFake>("any").ShouldNotBeNull();
            }

            (sp as IDisposable)?.Dispose();
            MsLifetimeScope.Current.ShouldBeNull();
        }

        // Lock-order regression: ExpandTemplateOrGetExistingExpansion used to call
        // container.Register while still holding KeyedServiceRegistry._sync. That ordering
        // (registry _sync -> Windsor internal lock) combined with the inverse path on another
        // thread (Windsor internal lock -> registry _sync, via the sub-resolver) made a
        // cross-lock deadlock possible. We probe it by spinning many parallel expansions while
        // a separate thread hammers IsKeyedService / GetHandlers on the kernel, and requiring
        // the workload to finish within a generous timeout.
        [Fact]
        public async Task Concurrent_AnyKey_Expansion_With_Kernel_Queries_DoesNotDeadlock()
        {
            var (sp, _) = BuildWindsor(s => s.AddKeyedTransient<IKeyedFake, KeyedFakeA>(KeyedService.AnyKey));
            var windsorContainer = sp.GetService<IWindsorContainer>();

            using var stop = new CancellationTokenSource();
            var workloadErrors = new ConcurrentBag<Exception>();

            // The hammer just keeps the kernel busy on another thread while the workload
            // triggers many concurrent expansions. We deliberately ignore exceptions inside
            // it: in flight, the container can transiently report not-yet-published names.
            // Only the workload's deadlock-or-not signal matters.
            var hammer = Task.Run(() =>
            {
                while (!stop.IsCancellationRequested)
                {
                    try
                    {
                        _ = windsorContainer.Kernel.GetHandlers(typeof(IKeyedFake));
                        _ = sp.GetService<IServiceProviderIsKeyedService>().IsKeyedService(typeof(IKeyedFake), "probe");
                    }
                    catch
                    {
                    }
                }
            });

            var workload = Task.Run(() => Parallel.For(0, 200, i =>
            {
                try { sp.GetKeyedService<IKeyedFake>("k" + i).ShouldNotBeNull(); }
                catch (Exception ex) { workloadErrors.Add(ex); }
            }));

            // 30s is well beyond the work; if we hit it we're deadlocked.
            var completed = await Task.WhenAny(workload, Task.Delay(TimeSpan.FromSeconds(30)));
            completed.ShouldBe(workload, "expansion workload deadlocked or timed out");
            stop.Cancel();
            await hammer;

            workloadErrors.ShouldBeEmpty();
            (sp as IDisposable)?.Dispose();
        }
    }
}
