using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Castle.Core.Internal;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Wrapper for Windsor lifetime infrastructure.
    /// </summary>
    public class MsLifetimeScope
    {

#if NET452
        public static MsLifetimeScope Current
        {
            get { return _current; }
            set { _current = value; }
        }

        [ThreadStatic]
        private static MsLifetimeScope _current;
#else
        public static MsLifetimeScope Current
        {
            get { return _current.Value; }
            set { _current.Value = value; }
        }

        private static readonly AsyncLocal<MsLifetimeScope> _current = new AsyncLocal<MsLifetimeScope>();
#endif

        public ILifetimeScope WindsorLifeTimeScope { get; }

        private readonly List<MsLifetimeScope> _children;

        private readonly HashSet<Burden> _transientBurdens;

        private ThreadSafeFlag _disposed;

        public MsLifetimeScope()
        {
            WindsorLifeTimeScope = new DefaultLifetimeScope();
            _children = new List<MsLifetimeScope>();

            _transientBurdens = new HashSet<Burden>();
            _disposed = new ThreadSafeFlag();
        }

        public void Track(Burden transientBurden)
        {
            _transientBurdens.Add(transientBurden);
            transientBurden.Releasing += TransientBurden_Releasing;
        }

        private void TransientBurden_Releasing(Burden burden)
        {
            _transientBurdens.Remove(burden);
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

            lock (_children)
            {
                foreach (var child in _children)
                {
                    child.Dispose();
                }

                _children.Clear();
            }

            WindsorLifeTimeScope.Dispose();
            _transientBurdens.Reverse().ToList().ForEach(b => b.Release());
        }

        public static IDisposable Using(MsLifetimeScope newLifetimeScope)
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