using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Extends Windsor's <see cref="ScopedLifestyleManager"/> to work as 
    /// MS style scope using <see cref="MsScopedAccesor"/>.
    /// </summary>
    public class MsScopedLifestyleManager : ScopedLifestyleManager
    {
        private GlobalMsLifetimeScopeProvider _globalMsLifetimeScopeProvider;

        public MsScopedLifestyleManager()
            : base(new MsScopedAccesor())
        {

        }

        public override void Init(IComponentActivator componentActivator, IKernel kernel, ComponentModel model)
        {
            _globalMsLifetimeScopeProvider = kernel.Resolve<GlobalMsLifetimeScopeProvider>();

            base.Init(componentActivator, kernel, model);
        }

        public override object Resolve(CreationContext context, IReleasePolicy releasePolicy)
        {
            if (MsLifetimeScope.Current == null)
            {
                using (MsLifetimeScope.Using(_globalMsLifetimeScopeProvider.LifetimeScope))
                {
                    return base.Resolve(context, releasePolicy);
                }
            }

            return base.Resolve(context, releasePolicy);
        }
    }
}