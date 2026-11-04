using System;
using System.Threading;
using System.Threading.Tasks;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    public class RootScopeConcurrencyTests
    {
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void Root_Scoped_And_Child_Singleton_Resolve_Without_Lock_Inversion(bool microsoft, bool keyed)
        {
            using var enteredA = new ManualResetEventSlim();
            using var enteredB = new ManualResetEventSlim();
            var services = new ServiceCollection();
            services.AddScoped<ScopedA>(provider =>
            {
                enteredA.Set();
                if (!enteredB.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Singleton factory did not enter.");
                }

                return new ScopedA(keyed ? provider.GetRequiredKeyedService<SingletonB>("k") : provider.GetRequiredService<SingletonB>());
            });
            SingletonB CreateSingleton(IServiceProvider provider)
            {
                enteredB.Set();
                if (!enteredA.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Scoped factory did not enter.");
                }

                return new SingletonB(keyed ? provider.GetRequiredKeyedService<ScopedC>("k") : provider.GetRequiredService<ScopedC>());
            }
            if (keyed)
            {
                services.AddKeyedScoped<ScopedC>("k");
                services.AddKeyedSingleton<SingletonB>("k", (provider, _) => CreateSingleton(provider));
            }
            else
            {
                services.AddScoped<ScopedC>();
                services.AddSingleton(CreateSingleton);
            }
            var root = Build(services, microsoft);
            var child = root.CreateScope();
            ScopedA scoped = null;
            SingletonB singleton = null;
            RunConcurrent(root,
                () => scoped = root.GetRequiredService<ScopedA>(),
                () => singleton = keyed ? child.ServiceProvider.GetRequiredKeyedService<SingletonB>("k") : child.ServiceProvider.GetRequiredService<SingletonB>(),
                () =>
                {
                    Assert.Same(singleton, scoped.Dependency);
                    Assert.Same(singleton.Dependency, keyed ? root.GetRequiredKeyedService<ScopedC>("k") : root.GetRequiredService<ScopedC>());
                }, enteredA, child);
        }

        [Fact]
        public void Different_Root_Scoped_Components_Can_Initialize_Concurrently()
        {
            foreach (var microsoft in new[] { true, false })
            {
                using var enteredFirst = new ManualResetEventSlim();
                using var enteredSecond = new ManualResetEventSlim();
                var services = new ServiceCollection();
                services.AddScoped<First>(_ =>
                {
                    enteredFirst.Set();
                    if (!enteredSecond.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("Second factory was blocked.");
                    }

                    return new First();
                });
                services.AddScoped<Second>(_ =>
                {
                    enteredSecond.Set();
                    if (!enteredFirst.Wait(TimeSpan.FromSeconds(5)))
                    {
                        throw new TimeoutException("First factory was blocked.");
                    }

                    return new Second();
                });
                var root = Build(services, microsoft);
                RunConcurrent(root, () => root.GetRequiredService<First>(), () => root.GetRequiredService<Second>(), () =>
                {
                });
            }
        }

        [Fact]
        public void Concurrent_Root_Scoped_Resolution_Creates_And_Disposes_One_Instance()
        {
            var created = 0;
            var disposed = 0;
            ParityRunner.RunAssertParity(services =>
            {
                created = disposed = 0;
                services.AddKeyedScoped<DisposableResource>("k", (_, _) =>
                {
                    Interlocked.Increment(ref created);
                    return new DisposableResource(() => Interlocked.Increment(ref disposed));
                });
            }, context =>
            {
                var instances = new DisposableResource[32];
                Parallel.For(0, instances.Length, index => instances[index] = context.Provider.GetRequiredKeyedService<DisposableResource>("k"));
                Assert.Equal(1, created);
                Assert.All(instances, instance => Assert.Same(instances[0], instance));
                context.DisposeProvider();
                Assert.Equal(1, disposed);
            });
        }

        private static IServiceProvider Build(IServiceCollection services, bool microsoft)
        {
            return microsoft
            ? services.BuildServiceProvider()
            : WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);
        }

        private static void RunConcurrent(IServiceProvider root, Action first, Action second, Action assert,
            ManualResetEventSlim firstEntered = null, IServiceScope child = null)
        {
            Exception firstError = null, secondError = null;
            var firstThread = new Thread(() =>
            {
                try
                {
                    first();
                }
                catch (Exception error)
                {
                    firstError = error;
                }
            })
            {
                IsBackground = true
            };
            var secondThread = new Thread(() =>
            {
                try
                {
                    second();
                }
                catch (Exception error)
                {
                    secondError = error;
                }
            })
            {
                IsBackground = true
            };
            try
            {
                firstThread.Start();
                if (firstEntered != null)
                {
                    Assert.True(firstEntered.Wait(TimeSpan.FromSeconds(5)), "First factory did not enter.");
                }

                secondThread.Start();
                var firstDone = firstThread.Join(TimeSpan.FromSeconds(5));
                var secondDone = secondThread.Join(TimeSpan.FromSeconds(5));
                Assert.True(firstDone && secondDone, "Concurrent resolution did not complete.");
                Assert.Null(firstError);
                Assert.Null(secondError);
                assert();
            }
            finally
            {
                // Disposing a container with blocked resolver threads would hide the failed assertion.
                if (!firstThread.IsAlive && !secondThread.IsAlive)
                {
                    child?.Dispose();
                    (root as IDisposable)?.Dispose();
                }
            }
        }

        public sealed class ScopedA
        {
            public SingletonB Dependency { get; }

            public ScopedA(SingletonB dependency)
            {
                Dependency = dependency;
            }
        }

        public sealed class SingletonB
        {
            public ScopedC Dependency { get; }

            public SingletonB(ScopedC dependency)
            {
                Dependency = dependency;
            }
        }

        public sealed class ScopedC;

        public sealed class First;

        public sealed class Second;

        public sealed class DisposableResource : IDisposable
        {
            private readonly Action _onDispose;

            public DisposableResource(Action onDispose)
            {
                _onDispose = onDispose;
            }

            public void Dispose()
            {
                _onDispose();
            }
        }
    }
}
