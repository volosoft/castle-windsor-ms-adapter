using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Extends <see cref="TransientLifestyleManager"/> to track resolved instances
    /// and release all when related scope is disposed.
    /// </summary>
    public class MsScopedTransientLifestyleManager : TransientLifestyleManager
    {
        protected override Burden CreateInstance(CreationContext context, bool trackedExternally)
        {
            var burden = base.CreateInstance(context, trackedExternally);

            if (MsLifetimeScope.Current != null)
            {
                MsLifetimeScope.Current.Track(burden);
            }

            return burden;
        }

        public override void Dispose()
        {

        }
    }
}
