using System;
using System.Collections.Generic;
using System.Linq;
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
        [ThreadStatic]
        public static MsLifetimeScope Current;

        public ILifetimeScope WindsorLifeTimeScope { get; private set; }

        public List<MsLifetimeScope> Children { get; set; }

        private readonly HashSet<Burden> _transientBurdens;

        private ThreadSafeFlag _disposed;

        public MsLifetimeScope()
        {
            WindsorLifeTimeScope = new DefaultLifetimeScope();
            Children = new List<MsLifetimeScope>();

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

        public void Dispose()
        {
            if (!_disposed.Signal())
            {
                return;
            }

            foreach (var child in Children)
            {
                child.Dispose();
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