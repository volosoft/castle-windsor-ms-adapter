using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel;
using Castle.MicroKernel.Registration;
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class ReentrantReleasePolicyTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_Valid_Nested_Resolution_From_Track_Does_Not_Block(bool adapter)
    {
        using var container = new WindsorContainer();
        var policy = new ReentrantPolicy(container.Kernel.ReleasePolicy);
        container.Kernel.ReleasePolicy = policy;
        IServiceProvider root = null;
        if (adapter)
        {
            var services = new ServiceCollection();
            services.AddTransient(_ => new First());
            services.AddTransient(_ => new Second());
            root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        }
        else container.Register(Component.For<First>().UsingFactoryMethod(() => new First()).LifestyleTransient(),
            Component.For<Second>().UsingFactoryMethod(() => new Second()).LifestyleTransient());
        policy.ResolveSecond = () => container.Resolve<Second>();
        var error = Record.Exception(() => container.Resolve<First>());
        try
        {
            if (policy.Nested != null) await policy.Nested.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Null(error);
            Assert.True(policy.CompletedInsideTrack);
        }
        finally { if (root != null) ((IDisposable)root).Dispose(); }
    }

    public sealed class First : IDisposable { public void Dispose() { } }
    public sealed class Second : IDisposable { public void Dispose() { } }
    private sealed class ReentrantPolicy(IReleasePolicy inner) : IReleasePolicy
    {
        public Func<Second> ResolveSecond;
        public Task<Second> Nested;
        public bool CompletedInsideTrack;
        private int _entered;
        public bool HasTrack(object instance) => inner.HasTrack(instance);
        public void Track(object instance, Burden burden)
        {
            inner.Track(instance, burden);
            if (instance is First && Interlocked.Exchange(ref _entered, 1) == 0)
            {
                Nested = Task.Run(ResolveSecond);
                CompletedInsideTrack = Nested.Wait(TimeSpan.FromSeconds(1));
                if (!CompletedInsideTrack) throw new TimeoutException("Nested tracking was blocked by the outer track call.");
            }
        }
        public void Release(object instance) => inner.Release(instance);
        public IReleasePolicy CreateSubPolicy() => inner.CreateSubPolicy();
        public void Dispose() => inner.Dispose();
    }
}
