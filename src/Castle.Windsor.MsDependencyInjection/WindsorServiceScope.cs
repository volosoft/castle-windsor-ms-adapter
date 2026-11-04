using System;
using Microsoft.Extensions.DependencyInjection;
using Castle.MicroKernel;
using System.Threading.Tasks;
using System.Threading;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceScope"/>.
    /// </summary> 
    public class WindsorServiceScope : IServiceScope, IAsyncDisposable
    {
        public IServiceProvider ServiceProvider { get; }

        public MsLifetimeScope LifetimeScope { get; }

        private readonly IWindsorContainer _container;
        private readonly IMsLifetimeScope _parentLifetimeScope;
        private int _disposed;

        public WindsorServiceScope(IWindsorContainer container, IMsLifetimeScope currentMsLifetimeScope)
        {
            var parent = MsScopeState.Resolve(container.Kernel, currentMsLifetimeScope);
            ObjectDisposedException.ThrowIf(parent?.IsDisposed == true, parent);
            _container = container;
            _parentLifetimeScope = currentMsLifetimeScope;

            LifetimeScope = new MsLifetimeScope(container);
            LifetimeScope.InitializeReleasePolicy(parent);

            try
            {
                _parentLifetimeScope?.AddChild(LifetimeScope);
                using (MsLifetimeScope.Using(LifetimeScope))
                {
                    ServiceProvider = (IServiceProvider)((IKernelInternal)container.Kernel).Resolve(
                        typeof(IServiceProvider), null, LifetimeScope.ReleasePolicy, ignoreParentContext: true);
                }
            }
            catch
            {
                // Cleanup must not replace the provider creation error.
                try { _parentLifetimeScope?.RemoveChild(LifetimeScope); }
                catch { }
                try
                {
                    if (LifetimeScope.TryBeginDispose()) MsAsyncDisposal.ReleaseLate(LifetimeScope, LifetimeScope.DisposeStarted);
                }
                catch { }
                throw;
            }
        }
         
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                var started = LifetimeScope.TryBeginDispose();
                MsAsyncDisposal.RunSynchronous(() => DisposeCore(started ? LifetimeScope.DisposeStarted : static () => { }));
            }
        }

        private void DisposeCore(Action disposeLifetime)
        {
            (ServiceProvider as ScopedWindsorServiceProvider)?.ClearCache();
            MsAsyncDisposal.Complete(() => _parentLifetimeScope?.RemoveChild(LifetimeScope),
                () => MsAsyncDisposal.Complete(disposeLifetime, () => _container.Release(ServiceProvider)));
        }

        public ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return default;
            var started = LifetimeScope.TryBeginDispose();
            return MsAsyncDisposal.Run(() => DisposeCore(started ? LifetimeScope.DisposeStarted : static () => { }),
                LifetimeScope.RequiresAsyncDisposal);
        }
    }
}
