using System;
using System.Linq;
using System.Reflection;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    /// <summary>
    /// The runtime materializes <see cref="ParameterInfo"/> arrays lazily and without
    /// synchronization (<c>m_parameters ??= ...</c> in <c>RuntimeConstructorInfo</c>), so threads
    /// racing the first <see cref="MethodBase.GetParameters"/> call can observe different
    /// ParameterInfo generations. Keyed-parameter metadata must therefore not rely on
    /// ParameterInfo reference identity. These tests force a fresh generation deterministically
    /// instead of racing threads.
    /// </summary>
    public sealed class ParameterInfoGenerationTests
    {
        [Fact]
        public void ServiceKey_Injection_Survives_ParameterInfo_Regeneration()
        {
            var services = new ServiceCollection();
            services.AddKeyedSingleton<StringKeyConsumer>("k1");
            services.AddKeyedSingleton<StringKeyConsumer>("k2");

            var sp = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);

            // Primes the adapter's keyed-parameter metadata cache with the current
            // ParameterInfo generation.
            sp.GetRequiredKeyedService<StringKeyConsumer>("k1").Key.ShouldBe("k1");

            ForceFreshParameterInfoGeneration(typeof(StringKeyConsumer));

            // The cached metadata must still match parameters of the new generation.
            sp.GetRequiredKeyedService<StringKeyConsumer>("k2").Key.ShouldBe("k2");

            (sp as IDisposable)?.Dispose();
        }

        /// <summary>
        /// With a reference-identity metadata cache, a fresh ParameterInfo generation makes the
        /// keyed sub-resolver decline and Windsor silently injects the non-keyed
        /// <see cref="KeyedFakeA"/> instead of the requested keyed <see cref="KeyedFakeB"/>.
        /// </summary>
        [Fact]
        public void FromKeyedServices_Injection_Survives_ParameterInfo_Regeneration()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Parity.Infrastructure.DisposeTracker>();
            services.AddSingleton<IKeyedFake, KeyedFakeA>();
            services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
            services.AddTransient<FromKeyedCtorConsumer>();

            var sp = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);

            sp.GetRequiredService<FromKeyedCtorConsumer>().Dep.ShouldBeOfType<KeyedFakeB>();

            ForceFreshParameterInfoGeneration(typeof(FromKeyedCtorConsumer));

            sp.GetRequiredService<FromKeyedCtorConsumer>().Dep.ShouldBeOfType<KeyedFakeB>();

            (sp as IDisposable)?.Dispose();
        }

        /// <summary>
        /// The keyed parameter here sits at constructor position 1, not 0, so this exercises the
        /// position-indexed slot mapping directly: an incorrect position-to-slot mapping (an
        /// off-by-one, or always reading slot 0) would still pass the position-0 tests above but
        /// fail here. The consumer and its plain dependency are types nested in this test class, so
        /// the reflection reset of the runtime parameter cache cannot disturb fakes shared with
        /// other (parallel) test classes.
        /// </summary>
        [Fact]
        public void FromKeyedServices_At_NonZero_Position_Survives_ParameterInfo_Regeneration()
        {
            var services = new ServiceCollection();
            services.AddSingleton<Parity.Infrastructure.DisposeTracker>();
            services.AddSingleton<PlainDependency>();
            services.AddSingleton<IKeyedFake, KeyedFakeA>();
            services.AddKeyedSingleton<IKeyedFake, KeyedFakeB>("k");
            services.AddTransient<PositionOneKeyedConsumer>();

            var sp = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);

            sp.GetRequiredService<PositionOneKeyedConsumer>().Dep.ShouldBeOfType<KeyedFakeB>();

            ForceFreshParameterInfoGeneration(typeof(PositionOneKeyedConsumer));

            sp.GetRequiredService<PositionOneKeyedConsumer>().Dep.ShouldBeOfType<KeyedFakeB>();

            (sp as IDisposable)?.Dispose();
        }

        /// <summary>
        /// Simulates losing the GetParameters first-touch race: resets the runtime's lazy
        /// parameter cache so the next call mints fresh <see cref="ParameterInfo"/> instances.
        /// </summary>
        private static void ForceFreshParameterInfoGeneration(Type type)
        {
            foreach (var ctor in type.GetConstructors(BindingFlags.Public | BindingFlags.Instance))
            {
                var oldGeneration = ctor.GetParameters();
                if (oldGeneration.Length == 0)
                {
                    continue;
                }

                var parametersField = ctor.GetType().GetField("m_parameters", BindingFlags.Instance | BindingFlags.NonPublic);
                parametersField.ShouldNotBeNull(
                    "RuntimeConstructorInfo no longer has an 'm_parameters' field; " +
                    "re-verify how this runtime caches ParameterInfo and update this test.");

                parametersField.SetValue(ctor, null);

                // Sanity-check the premise: the next call must mint a fresh instance for every
                // position, so a test asserting on a non-zero position relies on a real regeneration.
                var newGeneration = ctor.GetParameters();
                for (var i = 0; i < oldGeneration.Length; i++)
                {
                    newGeneration[i].ShouldNotBeSameAs(oldGeneration[i]);
                }
            }
        }

        public sealed class PlainDependency
        {
        }

        public sealed class PositionOneKeyedConsumer
        {
            public PositionOneKeyedConsumer(PlainDependency plain, [FromKeyedServices("k")] IKeyedFake dep)
            {
                Plain = plain;
                Dep = dep;
            }

            public PlainDependency Plain { get; }

            public IKeyedFake Dep { get; }
        }
    }
}
