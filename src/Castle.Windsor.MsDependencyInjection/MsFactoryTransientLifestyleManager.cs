using System.Runtime.CompilerServices;
using System.Threading;
using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class MsFactoryTransientLifestyleManager : TransientLifestyleManager
    {
        private static readonly ConditionalWeakTable<object, ConditionalWeakTable<IReleasePolicy, Tracking>> _trackingInstances = new();

        protected override void Track(Burden burden, IReleasePolicy releasePolicy)
        {
            if (!burden.RequiresPolicyRelease || releasePolicy.HasTrack(burden.Instance))
            {
                return;
            }

            var tracking = _trackingInstances.GetValue(burden.Instance, _ => new()).GetValue(releasePolicy, _ => new());
            // Each activation already has its own scope record. Only the native instance
            // lookup needs a single publisher; policy callbacks must remain reentrant.
            if (Interlocked.Exchange(ref tracking.Active, 1) != 0)
            {
                return;
            }

            try
            {
                if (!releasePolicy.HasTrack(burden.Instance))
                {
                    base.Track(burden, releasePolicy);
                }
            }
            finally
            {
                Volatile.Write(ref tracking.Active, 0);
            }
        }

        private sealed class Tracking
        {
            internal int Active;
        }
    }
}
