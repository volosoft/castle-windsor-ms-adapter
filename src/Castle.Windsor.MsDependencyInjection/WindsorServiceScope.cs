using System;
using System.Collections.Generic;
using System.Linq;
using Castle.Core.Internal;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IWindsorServiceScope"/> (and <see cref="IServiceScope"/>)
    /// to 
    /// </summary>
    public class WindsorServiceScope : IWindsorServiceScope
    {
        public IServiceProvider ServiceProvider { get { return _serviceProvider; } }

        public ILifetimeScope LifeTimeScope { get; private set; }

        private readonly HashSet<Burden> _burdens;
        private readonly IWindsorContainer _container;
        private readonly IScopedWindsorServiceProvider _serviceProvider;

        private ThreadSafeFlag _disposed;

        public WindsorServiceScope(IWindsorContainer container)
        {
            _container = container;
            _serviceProvider = new ScopedWindsorServiceProvider(container, this);

            _burdens = new HashSet<Burden>();
            _disposed = new ThreadSafeFlag();

            LifeTimeScope = new DefaultLifetimeScope();
        }

        public void Track(Burden burden)
        {
            _burdens.Add(burden);
            burden.Releasing += Burden_Releasing;
        }

        private void Burden_Releasing(Burden burden)
        {
            _burdens.Remove(burden);
        }

        public void Dispose()
        {
            if (!_disposed.Signal())
            {
                return;
            }

            LifeTimeScope.Dispose();
            _burdens.Reverse().ToList().ForEach(b => b.Release());

            _container.Release(this);
        }
    }
}