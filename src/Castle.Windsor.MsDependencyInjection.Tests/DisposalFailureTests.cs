using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Castle.MicroKernel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Castle.MicroKernel.Registration;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class DisposalFailureTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Native_Disposal_Failure_Is_Not_Retried_By_The_Closing_Scope(bool rootScope, bool registerFirst)
        {
            var container = new WindsorContainer();
            if (registerFirst)
            {
                container.Register(Component.For<NativeResource>().LifestyleTransient());
            }

            var root = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            if (!registerFirst)
            {
                container.Register(Component.For<NativeResource>().LifestyleTransient());
            }

            var scope = rootScope ? null : root.CreateScope();
            var resource = (scope?.ServiceProvider ?? root).GetRequiredService<NativeResource>();
            try
            {
                Assert.Same(resource.Error, await Close((object)scope ?? root, false));
                Assert.False(container.Kernel.ReleasePolicy.HasTrack(resource));
                Assert.Null(await Close(root, false));
                Assert.Equal(1, resource.Calls);
            }
            finally
            {
                Record.Exception(() => container.Dispose());
            }
        }

        public sealed class NativeResource : IDisposable
        {
            public readonly Exception Error = new ApplicationException("Native disposal failed.");
            public int Calls;

            public void Dispose()
            {
                Calls++;
                throw Error;
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task Failed_Disposal_Stops_User_Cleanup_Without_Retaining_Or_Retrying_Resources(bool asynchronous, bool rootScope)
        {
            foreach (var microsoft in new[] { true, false })
            {
                var first = new Resource(false);
                var last = new Resource(true);
                var instances = new Queue<Resource>(new[] { first, last });
                var services = new ServiceCollection();
                services.AddTransient(_ => instances.Dequeue());
                using var container = new WindsorContainer();
                var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
                var scope = rootScope ? null : root.CreateScope();
                var provider = scope?.ServiceProvider ?? root;
                Assert.Same(first, provider.GetRequiredService<Resource>());
                Assert.Same(last, provider.GetRequiredService<Resource>());
                var owner = (object)scope ?? root;
                Assert.Same(last.Error, await Close(owner, asynchronous));
                Assert.Equal(asynchronous ? 1 : 0, last.AsyncCalls);
                Assert.Equal(asynchronous ? 0 : 1, last.SyncCalls);
                Assert.Equal(0, first.SyncCalls + first.AsyncCalls);
                Assert.Null(await Close(owner, asynchronous));
                if (!microsoft)
                {
                    Assert.False(container.Kernel.ReleasePolicy.HasTrack(last));
                    Assert.False(container.Kernel.ReleasePolicy.HasTrack(first));
                    container.Release(last);
                }
                Assert.Null(await Close(root, asynchronous));
                Assert.Equal(1, last.SyncCalls + last.AsyncCalls);
                Assert.Equal(0, first.SyncCalls + first.AsyncCalls);
            }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task A_Pre_Destruction_Callback_Failure_Preserves_The_Exception_And_Allows_Release(bool asynchronous, bool rootScope)
        {
            using var container = new WindsorContainer();
            var policy = new RecordingPolicy(container.Kernel.ReleasePolicy);
            container.Kernel.ReleasePolicy = policy;
            var services = new ServiceCollection();
            services.AddTransient(_ => new Resource(false));
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = rootScope ? null : root.CreateScope();
            var resource = (scope?.ServiceProvider ?? root).GetRequiredService<Resource>();
            var failure = new ApplicationException("Release callback failed.");
            var calls = 0;
            policy.Burdens[resource].Releasing += _ =>
            {
                if (++calls == 1)
                {
                    throw failure;
                }
            };
            Assert.Same(failure, await Close((object)scope ?? root, asynchronous));
            if (!rootScope)
            {
                Assert.Equal(0, resource.SyncCalls + resource.AsyncCalls);
                Assert.True(policy.HasTrack(resource));
                container.Release(resource);
            }
            Assert.False(policy.HasTrack(resource));
            Assert.Equal(2, calls);
            Assert.Equal(rootScope && asynchronous ? 1 : 0, resource.AsyncCalls);
            Assert.Equal(rootScope && asynchronous ? 0 : 1, resource.SyncCalls);
            Assert.Null(await Close(root, asynchronous));
            Assert.Equal(1, resource.SyncCalls + resource.AsyncCalls);
        }

        private static Task<Exception> Close(object owner, bool asynchronous)
        {
            return asynchronous
            ? Record.ExceptionAsync(() => ((IAsyncDisposable)owner).DisposeAsync().AsTask())
            : Task.FromResult(Record.Exception(() => ((IDisposable)owner).Dispose()));
        }

        public sealed class Resource : IDisposable, IAsyncDisposable
        {
            private readonly bool _fails;
            public readonly Exception Error = new ApplicationException("Resource disposal failed.");
            public int SyncCalls;
            public int AsyncCalls;

            public Resource(bool fails)
            {
                _fails = fails;
            }

            public void Dispose()
            {
                SyncCalls++;
                if (_fails)
                {
                    throw Error;
                }
            }

            public async ValueTask DisposeAsync()
            {
                AsyncCalls++;
                await Task.Yield();
                if (_fails)
                {
                    throw Error;
                }
            }
        }

        private sealed class RecordingPolicy : IReleasePolicy
        {
            private readonly IReleasePolicy _inner;
            public readonly Dictionary<object, Burden> Burdens = new(ReferenceEqualityComparer.Instance);

            public RecordingPolicy(IReleasePolicy inner)
            {
                _inner = inner;
            }

            public void Track(object instance, Burden burden)
            {
                Burdens[instance] = burden;
                _inner.Track(instance, burden);
            }

            public bool HasTrack(object instance)
            {
                return _inner.HasTrack(instance);
            }

            public void Release(object instance)
            {
                _inner.Release(instance);
            }

            public IReleasePolicy CreateSubPolicy()
            {
                return _inner.CreateSubPolicy();
            }

            public void Dispose()
            {
                Burdens.Clear();
                _inner.Dispose();
            }
        }
    }
}
