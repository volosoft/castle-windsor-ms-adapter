using System;
using System.Threading.Tasks;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Specification;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class WindsorKeyedSpecificationTests : KeyedDependencyInjectionSpecificationTests, IDisposable
    {
        public override bool SupportsIServiceProviderIsKeyedService => true;

        protected override IServiceProvider CreateServiceProvider(IServiceCollection serviceCollection)
        {
            var windsorContainer = new WindsorContainer();
            return WindsorRegistrationHelper.CreateServiceProvider(windsorContainer, serviceCollection);
        }

        public void Dispose()
        {
            // Ensure no scope leaks across tests.
            MsLifetimeScope.Current.ShouldBeNull();
        }

        // -----------------------------------------------------------------------------------------
        // Documented behavioral divergences from MS DI 10.0.0. Each test runs the SAME scenario
        // against both backends in-line (no parity-runner infrastructure) and asserts the differing
        // outcomes side-by-side, so the divergence is visible in one place.
        // -----------------------------------------------------------------------------------------

        /// <summary>
        /// MS DI with <c>ValidateScopes = true</c> rejects resolving a scoped service directly from
        /// the root provider and throws <see cref="InvalidOperationException"/>. The Windsor adapter
        /// has no scope-validation equivalent and resolves it without throwing.
        /// </summary>
        [Fact]
        public void Keyed_Scoped_At_Root_ValidateScopes()
        {
            // MS DI: scope validation fires.
            var msServices = new ServiceCollection();
            msServices.AddSingleton(new DisposeTracker());
            msServices.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k");
            using (var ms = msServices.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }))
            {
                Should.Throw<InvalidOperationException>(() => ms.GetKeyedService<IKeyedFake>("k"));
            }

            // Windsor: no scope validation — resolves at the root without throwing.
            var windsorServices = new ServiceCollection();
            windsorServices.AddSingleton(new DisposeTracker());
            windsorServices.AddKeyedScoped<IKeyedFake, KeyedFakeA>("k");
            var windsor = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), windsorServices);
            try
            {
                windsor.GetKeyedService<IKeyedFake>("k").ShouldNotBeNull();
            }
            finally
            {
                ((IDisposable)windsor).Dispose();
            }
        }

        /// <summary>
        /// MS DI prefers <see cref="IAsyncDisposable.DisposeAsync"/> over <see cref="IDisposable.Dispose"/>
        /// when an instance implements both, and the sync path is NOT taken. The Windsor adapter does
        /// not dispatch async disposal to tracked instances — the sync <see cref="IDisposable.Dispose"/>
        /// path is taken instead.
        /// </summary>
        [Fact]
        public async Task AsyncDisposable_Keyed_Singleton_Dispose_Preference()
        {
            // MS DI: DisposeAsync on the root provider routes to the instance's DisposeAsync.
            var msServices = new ServiceCollection();
            msServices.AddSingleton(new DisposeTracker());
            msServices.AddKeyedSingleton<IKeyedFake, KeyedFakeAsync>("k");
            var ms = msServices.BuildServiceProvider();
            var msInstance = (KeyedFakeAsync)ms.GetKeyedService<IKeyedFake>("k");
            await ms.DisposeAsync();
            msInstance.DisposedAsync.ShouldBeTrue();
            msInstance.DisposedSync.ShouldBeFalse();

            // Windsor: only the synchronous Dispose runs, regardless of whether the provider is
            // disposed sync or async.
            var windsorServices = new ServiceCollection();
            windsorServices.AddSingleton(new DisposeTracker());
            windsorServices.AddKeyedSingleton<IKeyedFake, KeyedFakeAsync>("k");
            var windsor = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), windsorServices);
            var windsorInstance = (KeyedFakeAsync)windsor.GetKeyedService<IKeyedFake>("k");
            if (windsor is IAsyncDisposable asyncWindsor)
            {
                await asyncWindsor.DisposeAsync();
            }
            else
            {
                ((IDisposable)windsor).Dispose();
            }
            windsorInstance.DisposedSync.ShouldBeTrue();
            windsorInstance.DisposedAsync.ShouldBeFalse();
        }
    }
}
