using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed
{
    public class ScopedCollectionConcurrencyTests
    {
        [Theory]
        [InlineData(true, false)]
        [InlineData(false, false)]
        [InlineData(true, true)]
        [InlineData(false, true)]
        public void Shared_Child_Provider_Uses_Scope_Lock_Before_Collection_Initialization(bool microsoft, bool anyKey)
        {
            using var entered = new ManualResetEventSlim();
            using var proceed = new ManualResetEventSlim();
            using var collectionStarted = new ManualResetEventSlim();
            IServiceProvider shared = null;
            object key = anyKey ? KeyedService.AnyKey : "k";
            var services = new ServiceCollection();
            services.AddKeyedScoped<Resource>("k");
            services.AddScoped<Owner>(_ =>
            {
                entered.Set();
                if (!proceed.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException("Factory was not released.");
                }

                return new Owner(shared.GetKeyedServices<Resource>(key).ToArray());
            });
            var root = microsoft ? services.BuildServiceProvider()
                : WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);
            var child = root.CreateScope();
            shared = child.ServiceProvider;
            Owner owner = null;
            Resource[] items = null;
            Exception ownerError = null, collectionError = null;
            var ownerThread = new Thread(() =>
            {
                try
                {
                    owner = shared.GetRequiredService<Owner>();
                }
                catch (Exception error)
                {
                    ownerError = error;
                }
            })
            {
                IsBackground = true
            };
            var collectionThread = new Thread(() =>
            {
                collectionStarted.Set();
                try
                {
                    items = shared.GetKeyedServices<Resource>(key).ToArray();
                }
                catch (Exception error)
                {
                    collectionError = error;
                }
            })
            {
                IsBackground = true
            };
            try
            {
                ownerThread.Start();
                Assert.True(entered.Wait(TimeSpan.FromSeconds(5)));
                collectionThread.Start();
                Assert.True(collectionStarted.Wait(TimeSpan.FromSeconds(5)));
                Assert.True(SpinWait.SpinUntil(() => (collectionThread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
                proceed.Set();
                var ownerDone = ownerThread.Join(TimeSpan.FromSeconds(5));
                var collectionDone = collectionThread.Join(TimeSpan.FromSeconds(5));
                Assert.True(ownerDone && collectionDone, "Concurrent child-scope queries did not complete.");
                Assert.Null(ownerError);
                Assert.Null(collectionError);
                Assert.Same(Assert.Single(owner.Items), Assert.Single(items));
            }
            finally
            {
                proceed.Set();
                if (!ownerThread.IsAlive && !collectionThread.IsAlive)
                {
                    child.Dispose();
                    (root as IDisposable)?.Dispose();
                }
            }
        }

        public sealed class Resource;

        public sealed class Owner
        {
            public Resource[] Items { get; }

            public Owner(Resource[] items)
            {
                Items = items;
            }
        }
    }
}
