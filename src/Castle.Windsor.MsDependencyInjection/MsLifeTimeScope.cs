using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Castle.Core.Internal;
using Castle.MicroKernel.Lifestyle.Scoped;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Wrapper for Windsor lifetime infrastructure.
    /// </summary>
    public class MsLifetimeScope : IMsLifetimeScope
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

        public ILifetimeScope WindsorLifeTimeScope { get; }

        protected IWindsorContainer Container { get; }

        private readonly List<MsLifetimeScope> _children;

        private readonly List<object> _resolvedInstances;

        private ThreadSafeFlag _disposed;

        public MsLifetimeScope(IWindsorContainer container)
        {
            Container = container;

            WindsorLifeTimeScope = new DefaultLifetimeScope();

            _children = new List<MsLifetimeScope>();
            _resolvedInstances = new List<object>();
            _disposed = new ThreadSafeFlag();
        }

        public void AddInstance(object instance)
        {
            lock (_resolvedInstances)
            {
                _resolvedInstances.Add(instance);
            }
        }

        public void AddChild(MsLifetimeScope lifetimeScope)
        {
            lock (_children)
            {
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
            if (!_disposed.Signal())
            {
                return;
            }

            DisposeInternal();
        }

        protected virtual void DisposeInternal()
        {
            lock (_children)
            {
                _children.Reverse();

                foreach (var child in _children)
                {
                    child.Dispose();
                }

                _children.Clear();
            }

            lock (_resolvedInstances)
            {
                _resolvedInstances.Reverse();

                foreach (var instance in _resolvedInstances)
                {
                    Container.Release(instance);
                }

                _resolvedInstances.Clear();
            }

            WindsorLifeTimeScope.Dispose();
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