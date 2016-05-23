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
        public MsScopedLifestyleManager()
            : base(new MsScopedAccesor())
        {

        }

        public override object Resolve(CreationContext context, IReleasePolicy releasePolicy)
        {
            if (MsLifetimeScope.Current == null)
            {
                var burden = CreateInstance(context, false);
                Track(burden, releasePolicy);
                return burden.Instance;
            }

            return base.Resolve(context, releasePolicy);
        }
    }
}