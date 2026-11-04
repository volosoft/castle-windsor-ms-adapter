using System;

namespace Castle.Windsor.MsDependencyInjection
{
    public class GlobalScopedWindsorServiceProvider : ScopedWindsorServiceProvider, IDisposable, System.IAsyncDisposable
    {
        public GlobalScopedWindsorServiceProvider(IWindsorContainer container,
            MsLifetimeScopeProvider msLifetimeScopeProvider) : base(container, msLifetimeScopeProvider)
        {
        }

        public void Dispose()
        {
            MsAsyncDisposal.RunSynchronous(OwnMsLifetimeScope.Dispose);
        }

        public System.Threading.Tasks.ValueTask DisposeAsync() => OwnMsLifetimeScope is IAsyncDisposable asynchronous
            ? asynchronous.DisposeAsync() : MsAsyncDisposal.Run(OwnMsLifetimeScope.Dispose);
    }
}
