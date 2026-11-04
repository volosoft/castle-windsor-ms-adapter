using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Castle.Core;
using Castle.Core.Internal;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Registration;

namespace Castle.Windsor.MsDependencyInjection
{
    public class GlobalMsLifetimeScope : MsLifetimeScope, IDisposable
    {
        private readonly ConcurrentDictionary<ComponentModel, Lazy<RootLifetime>> _lifetimes = new();
        private readonly object _completionLock = new();
        private ComponentModel _previousLifetime;
        private volatile bool _containerDisposing;

        internal bool ValidateScopes { get; set; }

        public GlobalMsLifetimeScope(IWindsorContainer container)
            : base(container)
        {
        }

        internal RootLifetime GetLifetime(ComponentModel model, Action release)
        {
            return _lifetimes.GetOrAdd(model, _ => new Lazy<RootLifetime>(() =>
            {
                var name = "MsRootLifetime_" + Guid.NewGuid().ToString("N");
                var lifetime = new RootLifetime(release);
                Container.Register(Component.For<RootLifetime>().Named(name)
                    .UsingFactoryMethod(() => lifetime).LifestyleSingleton());
                lifetime.Model = Container.Kernel.GetHandler(name).ComponentModel;
                Container.Resolve<RootLifetime>(name);
                return lifetime;
            })).Value;
        }

        internal void RecordDependency(CreationContext context, RootLifetime dependency)
        {
            RecordNativeDependency(context, dependency.Model);
        }

        internal void RecordNativeDependency(CreationContext context, ComponentModel dependency)
        {
            context.SelectScopeRoot(handlers =>
            {
                foreach (var handler in handlers)
                {
                    if (ServiceResolveHelper.IsScopeBoundary(handler))
                    {
                        continue;
                    }

                    var owner = _lifetimes.TryGetValue(handler.ComponentModel, out var lifetime) && lifetime.IsValueCreated
                                            ? lifetime.Value.Model
                                            : Container.Kernel.GetHandler(handler.ComponentModel.Name)?.ComponentModel ?? handler.ComponentModel;
                    AddDependency(owner, dependency);
                }
                return null;
            });
        }

        internal void CompleteLifetime(RootLifetime lifetime, ComponentModel component)
        {
            foreach (var dependency in component.Dependents.OfType<ComponentModel>())
            {
                if (dependency.CustomLifestyle != typeof(MsSingletonLifestyleManager) &&
                    dependency.CustomLifestyle != typeof(MsScopedLifestyleManager) &&
                    dependency.Implementation != typeof(RootLifetime))
                {
                    AddDependency(lifetime.Model, dependency);
                }
            }

            lock (_completionLock)
            {
                // Closed native generics share a handler, so its dependencies take
                // precedence over a creation-order tie between root lifetimes.
                if (_previousLifetime != null && !DependsOn(_previousLifetime, lifetime.Model))
                {
                    AddDependency(lifetime.Model, _previousLifetime);
                }

                _previousLifetime = lifetime.Model;
            }
        }

        internal void TrackReleaseDependency(ComponentModel component)
        {
            var model = Container.Kernel.GetHandler(typeof(GlobalMsLifetimeScope)).ComponentModel;
            AddDependency(model, component);
        }

        private static void AddDependency(ComponentModel owner, ComponentModel dependency)
        {
            if (!ReferenceEquals(owner, dependency) && !owner.Dependents.Contains(dependency))
            {
                owner.AddDependent(dependency);
            }
        }

        private static bool DependsOn(GraphNode owner, GraphNode dependency)
        {
            var pending = new Stack<GraphNode>();
            var visited = new HashSet<GraphNode>();
            pending.Push(owner);
            while (pending.Count != 0)
            {
                var current = pending.Pop();
                if (ReferenceEquals(current, dependency))
                {
                    return true;
                }

                if (!visited.Add(current))
                {
                    continue;
                }

                foreach (var next in current.Dependents)
                {
                    pending.Push(next);
                }
            }
            return false;
        }

        internal void Decommission(Action decommission)
        {
            _containerDisposing = true;
            try
            {
                decommission();
            }
            finally
            {
                _containerDisposing = false;
            }
        }

        void IDisposable.Dispose()
        {
            if (!_containerDisposing)
            {
                Dispose();
            }
            else if (TryBeginDispose())
            {
                DisposeStarted();
            }
        }

        protected override void DisposeInternal()
        {
            try
            {
                // A kernel decommission must not detach this container while its parent is enumerating children.
                DisposeWithTrackedInstances(_containerDisposing ? static () =>
                {
                }
                : Container.Dispose);
            }
            finally
            {
                _lifetimes.Clear();
                _previousLifetime = null;
            }
        }
    }
}
