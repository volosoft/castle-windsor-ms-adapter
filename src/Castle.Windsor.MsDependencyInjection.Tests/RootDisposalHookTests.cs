using System;
using System.Threading.Tasks;
using Castle.MicroKernel.Registration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class RootDisposalHookTests
    {
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public async Task A_Failing_Virtual_Hook_Does_Not_Block_A_Later_Container_Close(bool afterBase, bool asynchronous)
        {
            using var container = new WindsorContainer();
            container.Register(Component.For<GlobalMsLifetimeScope>().ImplementedBy<FailingRoot>().LifestyleSingleton());
            var services = new ServiceCollection();
            services.AddSingleton<Resource>();
            var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var resource = provider.GetRequiredService<Resource>();
            var root = (FailingRoot)container.Resolve<GlobalMsLifetimeScope>();
            root.AfterBase = afterBase;
            var failure = asynchronous
                ? await Record.ExceptionAsync(() => ((IAsyncDisposable)provider).DisposeAsync().AsTask())
                : Record.Exception(() => ((IDisposable)provider).Dispose());
            Assert.Same(root.Error, failure);
            await Task.Run(() => container.Dispose()).WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, root.Calls);
            Assert.Equal(1, resource.Disposals);
        }

        public sealed class FailingRoot : GlobalMsLifetimeScope
        {
            public readonly Exception Error = new ApplicationException("Root hook failed.");
            public bool AfterBase;
            public int Calls;

            public FailingRoot(IWindsorContainer container) : base(container)
            {
            }

            protected override void DisposeInternal()
            {
                Calls++;
                if (AfterBase)
                {
                    base.DisposeInternal();
                }

                throw Error;
            }
        }

        public sealed class Resource : IDisposable
        {
            public int Disposals;

            public void Dispose()
            {
                Disposals++;
            }
        }
    }
}
