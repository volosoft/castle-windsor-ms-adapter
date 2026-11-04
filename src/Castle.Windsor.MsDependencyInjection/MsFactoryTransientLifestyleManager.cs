using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class MsFactoryTransientLifestyleManager : TransientLifestyleManager
{
    private sealed class Tracking { internal int Active; }
    private static readonly ConditionalWeakTable<object, ConditionalWeakTable<IReleasePolicy, Tracking>> TrackingInstances = new();

    protected override void Track(Burden burden, IReleasePolicy releasePolicy)
    {
        if (!burden.RequiresPolicyRelease || releasePolicy.HasTrack(burden.Instance)) return;
        var tracking = TrackingInstances.GetValue(burden.Instance, _ => new()).GetValue(releasePolicy, _ => new());
        // Each activation already has its own scope record. Only the native instance
        // lookup needs a single publisher; policy callbacks must remain reentrant.
        if (Interlocked.Exchange(ref tracking.Active, 1) != 0) return;
        try
        {
            if (!releasePolicy.HasTrack(burden.Instance)) base.Track(burden, releasePolicy);
        }
        finally { Volatile.Write(ref tracking.Active, 0); }
    }
}
