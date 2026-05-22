using System;
using System.Collections.Concurrent;
using System.Linq;
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

        // Regression for https://github.com/volosoft/castle-windsor-ms-adapter/issues/48 :
        // before this PR, any IServiceCollection containing a keyed descriptor (added by the user
        // OR a third-party library such as AddOpenApi / AddHttpClient().AddStandardResilienceHandler
        // / Scalar) crashed the adapter at registration time with
        //   InvalidOperationException: This service descriptor is keyed. Your service provider may
        //                              not support keyed services.
        // The crash came from WindsorRegistrationHelper.RegisterNonKeyedServiceDescriptor reading
        // ImplementationType / ImplementationInstance / ImplementationFactory on a keyed
        // descriptor (those getters throw on keyed). The fix routes keyed descriptors through a
        // separate branch and uses KeyedImplementationType / KeyedImplementationInstance /
        // KeyedImplementationFactory. These tests pin all three descriptor shapes by simulating
        // a "third-party library added keyed registrations, user code only touches non-keyed".

        [Fact]
        public void Issue48_KeyedTypeDescriptor_DoesNotBreakProviderBuild()
        {
            // build must not throw, and the non-keyed service must remain usable.
            var (sp, _) = BuildWindsor(s =>
            {
                s.AddKeyedTransient<IKeyedFake, KeyedFakeA>("third-party-key");
                s.AddTransient<IKeyedFake, KeyedFakeB>();
            });

            sp.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>();
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void Issue48_KeyedFactoryDescriptor_DoesNotBreakProviderBuild()
        {
            var (sp, _) = BuildWindsor(s =>
            {
                s.AddKeyedTransient<IKeyedFake>("third-party-key", (_, _) => new KeyedFakeA(new DisposeTracker()));
                s.AddTransient<IKeyedFake, KeyedFakeB>();
            });

            sp.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>();
            (sp as IDisposable)?.Dispose();
        }

        [Fact]
        public void Issue48_KeyedInstanceDescriptor_DoesNotBreakProviderBuild()
        {
            var prebuilt = new PreBuiltFake();
            var (sp, _) = BuildWindsor(s =>
            {
                s.AddKeyedSingleton<IKeyedFake>("third-party-key", prebuilt);
                s.AddTransient<IKeyedFake, KeyedFakeB>();
            });

            sp.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>();
            (sp as IDisposable)?.Dispose();
        }

        // Combined "kitchen-sink" smoke: a single service collection contains all three keyed
        // descriptor shapes plus a non-keyed registration, mimicking a real app that pulls in
        // multiple third-party libraries each adding keyed services. The build must succeed and
        // both the non-keyed and the keyed services must be resolvable.
        [Fact]
        public void Issue48_AllKeyedDescriptorShapes_Coexist_With_NonKeyed()
        {
            var prebuilt = new PreBuiltFake();
            var (sp, _) = BuildWindsor(s =>
            {
                s.AddKeyedTransient<IKeyedFake, KeyedFakeA>("type-key");
                s.AddKeyedSingleton<IKeyedFake>("factory-key", (_, _) => new KeyedFakeA(new DisposeTracker()));
                s.AddKeyedSingleton<IKeyedFake>("instance-key", prebuilt);
                s.AddTransient<IKeyedFake, KeyedFakeB>();
            });

            sp.GetRequiredService<IKeyedFake>().ShouldBeOfType<KeyedFakeB>();
            sp.GetRequiredKeyedService<IKeyedFake>("type-key").ShouldBeOfType<KeyedFakeA>();
            sp.GetRequiredKeyedService<IKeyedFake>("factory-key").ShouldBeOfType<KeyedFakeA>();
            sp.GetRequiredKeyedService<IKeyedFake>("instance-key").ShouldBeSameAs(prebuilt);
            (sp as IDisposable)?.Dispose();
        }

        // Regression: MsCompatibleCollectionResolver must be constructed with
        // allowEmptyCollections: true (the pre-PR 4.x adapter did this; the PR rewrite
        // dropped it). The MS DI contract says an IEnumerable<T> dependency is satisfied
        // with an empty sequence when no T is registered. Without
        // allowEmptyCollections: true, CollectionResolver.CanResolve returns false in that
        // case and the constructor injection falls through to a hard failure.
        //
        // This surfaced as a HandlerException ("Can't create component <X> as it has
        // dependencies to be satisfied") when running a real ASP.NET Zero / Abp 11.2.0
        // Web.Mvc host: an ABP/Castle component took a ctor IEnumerable<T> for which no
        // implementation was registered in that host configuration.
        [Fact]
        public void Empty_EnumerableDependency_Injected_As_Empty_Sequence()
        {
            var (sp, _) = BuildWindsor(s =>
            {
                // EmptyCollectionConsumer ctor takes IEnumerable<INeverRegisteredService>,
                // but INeverRegisteredService is never registered.
                s.AddTransient<EmptyCollectionConsumer>();
            });

            var consumer = sp.GetRequiredService<EmptyCollectionConsumer>();
            consumer.Items.ShouldBeEmpty();
            (sp as IDisposable)?.Dispose();
        }
    }

    public interface INeverRegisteredService { }

    public sealed class EmptyCollectionConsumer
    {
        public EmptyCollectionConsumer(System.Collections.Generic.IEnumerable<INeverRegisteredService> items)
        {
            Items = items.ToList();
        }

        public System.Collections.Generic.IReadOnlyList<INeverRegisteredService> Items { get; }
    }
}
