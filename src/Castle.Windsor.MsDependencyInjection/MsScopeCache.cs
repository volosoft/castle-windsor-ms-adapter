using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle.Scoped;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class MsScopeCache : ILifetimeScope
    {
        private readonly MsLifetimeScope _owner;

        public MsScopeCache(MsLifetimeScope owner)
        {
            _owner = owner;
        }

        private sealed class Slot
        {
            internal readonly DefaultLifetimeScope Scope = new();
            internal long Order;
        }

        private readonly DefaultLifetimeScope _scope = new();
        private readonly ConcurrentDictionary<ComponentModel, Slot> _rootScopes = new();
        private long _order;
        private int _disposed;

        internal bool IsDisposed
        {
            get
            {
                return Volatile.Read(ref _disposed) != 0;
            }
        }

        internal void RecordRootDependency(ComponentModel model, CreationContext context)
        {
            if (_owner is not GlobalMsLifetimeScope root)
            {
                return;
            }

            var slot = _rootScopes.GetOrAdd(model, _ => new Slot());
            root.RecordDependency(context, root.GetLifetime(model, slot.Scope.Dispose));
        }

        public Burden GetCachedInstance(ComponentModel model, ScopedInstanceActivationCallback createInstance)
        {
            if (model.Implementation == typeof(MsScopeState))
            {
                var state = createInstance(_ =>
                {
                });
                ((MsScopeState)state.Instance).Owner = _owner;
                return state;
            }
            if (typeof(IAsyncDisposable).IsAssignableFrom(model.Implementation) || model.Implementation == typeof(Castle.Core.Internal.LateBoundComponent))
            {
                _owner.RequireAsyncDisposal();
            }

            ObjectDisposedException.ThrowIf(IsDisposed || _owner.IsDisposed, this);
            var slot = _owner is GlobalMsLifetimeScope ? _rootScopes.GetOrAdd(model, _ => new Slot()) : null;
            var cache = slot?.Scope ?? _scope;
            lock (cache)
            {
                return GetCachedInstance(cache, slot, model, createInstance);
            }
        }

        private Burden GetCachedInstance(DefaultLifetimeScope cache, Slot slot, ComponentModel model, ScopedInstanceActivationCallback createInstance)
        {
            ObjectDisposedException.ThrowIf(IsDisposed || _owner.IsDisposed, this);
            var root = _owner as GlobalMsLifetimeScope;
            var lifetime = slot == null ? null : root?.GetLifetime(model, cache.Dispose);
            Burden created = null;
            var released = false;
            void Released(Burden _) => released = true;
            try
            {
                var result = cache.GetCachedInstance(model, afterCreated => createInstance(value =>
                {
                    created = value;
                    value.Released += Released;
                    afterCreated(value);
                }));
                ObjectDisposedException.ThrowIf(IsDisposed || _owner.IsDisposed, this);
                if (created != null && slot != null)
                {
                    slot.Order = Interlocked.Increment(ref _order);
                    root.CompleteLifetime(lifetime, model);
                }
                return result;
            }
            catch
            {
                if (created != null && !released)
                {
                    try
                    {
                        MsAsyncDisposal.ReleaseLate(created.Instance, () => created.Release());
                    }
                    catch
                    {
                    }
                }
                throw;
            }
            finally
            {
                if (created != null)
                {
                    created.Released -= Released;
                }
            }
        }

        public void Dispose()
        {
            MsAsyncDisposal.RunSynchronous(DisposeCore);
        }

        internal void DisposeWithinContext()
        {
            MsAsyncDisposal.RunInCurrentContext(DisposeCore);
        }

        private void DisposeCore()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (_owner is GlobalMsLifetimeScope)
            {
                foreach (var slot in _rootScopes.Values.OrderByDescending(value => value.Order))
                {
                    slot.Scope.Dispose();
                }
            }
            else
            {
                _scope.Dispose();
            }
        }
    }
}
