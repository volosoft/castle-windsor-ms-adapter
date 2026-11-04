using System;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class MsSingletonLifestyleManager : SingletonLifestyleManager
    {
        private GlobalMsLifetimeScope _root;
        private RootLifetime _lifetime;

        public override object Resolve(CreationContext context, IReleasePolicy releasePolicy)
        {
            _root = Kernel.Resolve<GlobalMsLifetimeScope>();
            _lifetime = _root.GetLifetime(Model, () => base.Dispose());
            _root.RecordDependency(context, _lifetime);
            using (MsLifetimeScope.Using(_root))
            {
                var instance = base.Resolve(context, releasePolicy);
                if (_root.IsDisposed)
                {
                    MsAsyncDisposal.ReleaseLate(instance, () => base.Dispose());
                    throw new ObjectDisposedException(nameof(GlobalMsLifetimeScope));
                }
                return instance;
            }
        }

        protected override Burden CreateInstance(CreationContext context, bool trackedExternally)
        {
            var burden = base.CreateInstance(context, trackedExternally);
            _root.CompleteLifetime(_lifetime, Model);
            return burden;
        }

        public override void Dispose()
        {
            // The registered lifetime participates in Windsor's disposal graph independently
            // of the open-generic handler that owns this closed lifestyle manager.
            if (_lifetime == null)
            {
                base.Dispose();
            }
        }
    }
}
