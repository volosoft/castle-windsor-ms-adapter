using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Used to adapt MS style Singletons to Windsor's Singleton with <see cref="MsLifetimeScope"/>.
    /// </summary>
    public class MyScopedSingletonLifestyleManager : SingletonLifestyleManager
    {
        private GlobalMsLifetimeScopeProvider _globalMsLifetimeScopeProvider;

        public override void Init(IComponentActivator componentActivator, IKernel kernel, ComponentModel model)
        {
            _globalMsLifetimeScopeProvider = kernel.Resolve<GlobalMsLifetimeScopeProvider>();

            base.Init(componentActivator, kernel, model);
        }

        public override object Resolve(CreationContext context, IReleasePolicy releasePolicy)
        {
            if (MsLifetimeScope.Current == null)
            {
                return base.Resolve(context, releasePolicy);
            }

            using (MsLifetimeScope.Using(_globalMsLifetimeScopeProvider.LifetimeScope))
            {
                return base.Resolve(context, releasePolicy);
            }
        }
    }
}