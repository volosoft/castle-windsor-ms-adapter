using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.Core.Internal;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Wrapper for Windsor lifetime infrastructure.
    /// </summary>
    public class MsLifetimeScope : IMsLifetimeScope, IAsyncDisposable
    {

#if NET452
        public static IMsLifetimeScope Current
        {
            get { return _current; }
            set { _current = value; }
        }

        [ThreadStatic]
        private static IMsLifetimeScope _current;
#else
        public static IMsLifetimeScope Current
        {
            get { return _current.Value; }
            set { _current.Value = value; }
        }

        private static readonly AsyncLocal<IMsLifetimeScope> _current = new AsyncLocal<IMsLifetimeScope>();
#endif

        public ILifetimeScope WindsorLifeTimeScope => _cache;

        private readonly MsScopeCache _cache;

        protected IWindsorContainer Container { get; }

        private readonly List<MsLifetimeScope> _children;

        private readonly List<object> _resolvedInstances;
        private readonly HashSet<object> _ownedInstances = new(ReferenceEqualityComparer.Instance);

        private int _disposed;
        private int _requiresAsyncDisposal;
        internal bool RequiresAsyncDisposal => Volatile.Read(ref _requiresAsyncDisposal) != 0;
        internal void RequireAsyncDisposal() => Volatile.Write(ref _requiresAsyncDisposal, 1);
        internal bool TryBeginDispose() => Interlocked.Exchange(ref _disposed, 1) == 0;
        internal void DisposeStarted() => MsAsyncDisposal.RunInCurrentContext(DisposeInternal);
        private IReleasePolicy _releasePolicy;
        internal bool IsDisposed => Volatile.Read(ref _disposed) != 0;
        internal IReleasePolicy ReleasePolicy => _releasePolicy ?? Container.Kernel.ReleasePolicy;

        internal void InitializeReleasePolicy(MsLifetimeScope parent)
        {
            if (parent == null || parent._releasePolicy != null)
                _releasePolicy = (parent?.ReleasePolicy ?? Container.Kernel.ReleasePolicy).CreateSubPolicy();
        }

        private void Release(object instance)
        {
            if (instance is ScopeActivation activation) activation.Release();
            else if (instance is NativeAsyncRelease native) native.Dispose();
            else if (_releasePolicy?.HasTrack(instance) == true) _releasePolicy.Release(instance);
            else Container.Release(instance);
        }

        internal void AddNativeAsync(object instance, IAsyncDisposable target)
        {
            RequireAsyncDisposal();
            var release = new NativeAsyncRelease(target, () => Container.Release(instance));
            lock (_resolvedInstances)
            {
                if (!IsDisposed)
                {
                    _resolvedInstances.Add(release);
                    return;
                }
            }
            MsAsyncDisposal.ReleaseLate(instance, release.Dispose);
            throw new ObjectDisposedException(nameof(MsLifetimeScope));
        }

        internal void AddActivation(ScopeActivation activation)
        {
            if (this is GlobalMsLifetimeScope root && activation.Owner == null && Container.Kernel.ReleasePolicy.HasTrack(activation.Instance))
                root.TrackReleaseDependency(Container.Kernel.GetHandler(activation.Model.Name).ComponentModel);
            lock (_resolvedInstances)
            {
                if (!IsDisposed)
                {
                    _ownedInstances.Add(activation.Instance);
                    _resolvedInstances.Add(activation);
                    return;
                }
            }
            MsAsyncDisposal.ReleaseLate(activation.Instance, activation.Release);
            throw new ObjectDisposedException(nameof(MsLifetimeScope));
        }

        public MsLifetimeScope(IWindsorContainer container)
        {
            Container = container;

            _cache = new MsScopeCache(this);

            _children = new List<MsLifetimeScope>();
            _resolvedInstances = new List<object>();
        }

        public void AddInstance(object instance)
        {
            lock (_resolvedInstances)
            {
                if (Volatile.Read(ref _disposed) == 0)
                {
                    if (_ownedInstances.Contains(instance)) return;
                    _resolvedInstances.Add(instance);
                    return;
                }
            }
            MsAsyncDisposal.ReleaseLate(instance, () => Release(instance));
            throw new ObjectDisposedException(nameof(MsLifetimeScope));
        }

        public void AddChild(MsLifetimeScope lifetimeScope)
        {
            RequireAsyncDisposal();
            lock (_children)
            {
                ObjectDisposedException.ThrowIf(Volatile.Read(ref _disposed) != 0, this);
                _children.Add(lifetimeScope);
            }
        }

        public void RemoveChild(MsLifetimeScope lifetimeScope)
        {
            lock (_children)
            {
                _children.Remove(lifetimeScope);
            }
        }

        public void Dispose()
        {
            if (TryBeginDispose()) MsAsyncDisposal.RunSynchronous(DisposeStarted);
        }

        public ValueTask DisposeAsync() => TryBeginDispose() ? MsAsyncDisposal.Run(DisposeStarted) : default;

        protected virtual void DisposeInternal()
        {
            DisposeWithTrackedInstances(() => MsAsyncDisposal.Complete(_cache.DisposeWithinContext, () => _releasePolicy?.Dispose()));
        }

        private protected void DisposeWithTrackedInstances(Action cleanup)
        {
            var deferred = new List<ScopeActivation>();
            MsAsyncDisposal.Complete(() => DisposeTrackedInstances(deferred),
                () => MsAsyncDisposal.Complete(cleanup,
                    () => { foreach (var activation in deferred) activation.Release(); }));
        }

        private void DisposeTrackedInstances(List<ScopeActivation> deferred)
        {
            MsLifetimeScope[] children;
            lock (_children)
            {
                children = _children.AsEnumerable().Reverse().ToArray();
                _children.Clear();
            }
            foreach (var child in children)
                if (child.TryBeginDispose()) child.DisposeStarted();
            object[] instances;
            lock (_resolvedInstances)
            {
                instances = _resolvedInstances.AsEnumerable().Reverse().ToArray();
                _resolvedInstances.Clear();
                _ownedInstances.Clear();
            }
            var owned = instances.OfType<ScopeActivation>()
                .Where(activation => activation.Owner?.Instance != null)
                .ToLookup(activation => activation.Instance, ReferenceEqualityComparer.Instance);
            foreach (var instance in instances)
            {
                if (instance is ScopeActivation { Owner: null, IsReleased: false } activation)
                {
                    var owners = owned[activation.Instance].Where(value => !value.IsReleased).ToArray();
                    if (owners.Length != 0)
                    {
                        deferred.Add(activation);
                        // Burden.Released fires before its dependencies close.
                        activation.ReleaseAfter(owners);
                        continue;
                    }
                }
                Release(instance);
            }
        }

        public static IDisposable Using(IMsLifetimeScope newLifetimeScope)
        {
            var previous = Current;
            Current = newLifetimeScope;
            return new DisposeAction(() =>
            {
                Current = previous;
            });
        }
    }
}
