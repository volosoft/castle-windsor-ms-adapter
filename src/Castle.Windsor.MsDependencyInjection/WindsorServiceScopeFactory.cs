using System;
using System.Threading;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Handlers;
using Castle.MicroKernel.ModelBuilder;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IServiceScopeFactory"/>.
    /// </summary>
    public class WindsorServiceScopeFactory : IServiceScopeFactory
    {
        internal static readonly AsyncLocal<IWindsorContainer> FactoryRootContext = new();

        internal static bool IsFactoryEntry(IWindsorContainer container)
        {
            var target = container.Kernel;
            for (var current = FactoryRootContext.Value?.Kernel; current != null; current = current.Parent)
            {
                if (ReferenceEquals(current, target))
                {
                    return true;
                }
            }

            return false;
        }

        internal static IDisposable UsingFactoryRoot(IWindsorContainer container)
        {
            var previous = FactoryRootContext.Value;
            FactoryRootContext.Value = container;
            return new DisposeAction(() => FactoryRootContext.Value = previous);
        }

        private readonly IWindsorContainer _container;
        private readonly IMsLifetimeScope _msLifetimeScope;
        private readonly MsLifetimeScope _parentOwner;
        private readonly GlobalMsLifetimeScope _root;

        public WindsorServiceScopeFactory(IWindsorContainer container, MsLifetimeScopeProvider msLifetimeScopeProvider)
        {
            _container = container;
            _parentOwner = MsScopeState.Resolve(container.Kernel, msLifetimeScopeProvider.LifetimeScope);
            _msLifetimeScope = msLifetimeScopeProvider.LifetimeScope;
            _root = container.Resolve<GlobalMsLifetimeScope>();
        }

        public IServiceScope CreateScope()
        {
            ObjectDisposedException.ThrowIf(_root.IsDisposed, _root);
            return new WindsorServiceScope(_container, IsFactoryEntry(_container) ? null
                : _parentOwner is { IsDisposed: true } ? _root : _msLifetimeScope);
        }
    }

    internal sealed class ScopeFactoryEntryResolution : IComponentModelDescriptor, IResolveExtension
    {
        private readonly IWindsorContainer _container;
        private IServiceScopeFactory _factory;
        private IServiceScopeFactory _helper;

        public ScopeFactoryEntryResolution(IWindsorContainer container)
        {
            _container = container;
        }

        public void BuildComponentModel(IKernel kernel, ComponentModel model)
        {
        }

        public void ConfigureComponentModel(IKernel kernel, ComponentModel model)
        {
            model.ResolveExtensions(true).Add(this);
        }

        public void Init(IKernel kernel, IHandler handler)
        {
        }

        public void Intercept(ResolveInvocation invocation)
        {
            invocation.Proceed();
            if (invocation.ResolvedInstance is not IServiceScopeFactory target)
            {
                return;
            }

            invocation.ResolvedInstance = WindsorServiceScopeFactory.IsFactoryEntry(_container)
                            ? Bind(ref _factory, target, true) : Bind(ref _helper, target, false);
        }

        private IServiceScopeFactory Bind(ref IServiceScopeFactory cached, IServiceScopeFactory target, bool factory)
        {
            var existing = Volatile.Read(ref cached);
            if (existing != null)
            {
                return existing;
            }

            var entry = new Entry(target, _container, factory);
            return Interlocked.CompareExchange(ref cached, entry, null) ?? entry;
        }

        private sealed class Entry : IServiceScopeFactory
        {
            private readonly IServiceScopeFactory _target;
            private readonly IWindsorContainer _container;
            private readonly bool _factory;

            public Entry(IServiceScopeFactory target, IWindsorContainer container, bool factory)
            {
                _target = target;
                _container = container;
                _factory = factory;
            }

            public IServiceScope CreateScope()
            {
                using var origin = WindsorServiceScopeFactory.UsingFactoryRoot(_factory ? _container : null);
                return _target.CreateScope();
            }
        }
    }
}
