using System.Linq;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.LifecycleConcerns;
using Castle.MicroKernel.ModelBuilder.Inspectors;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class NativeDecommissionScheduler
{
    internal NativeDecommissionScheduler(IKernel kernel)
    {
        var contributors = kernel.ComponentModelBuilder.Contributors;
        foreach (var contributor in contributors) kernel.ComponentModelBuilder.RemoveContributor(contributor);
        foreach (var contributor in contributors)
        {
            kernel.ComponentModelBuilder.AddContributor(contributor);
            if (contributor.GetType() == typeof(LifecycleModelInspector))
                kernel.ComponentModelBuilder.AddContributor(new MsOwnedDefaults());
        }
        kernel.ComponentModelCreated += Install;
        foreach (var model in kernel.GetAssignableHandlers(typeof(object)).Select(handler => handler.ComponentModel).Distinct())
            Install(model);
    }

    private static void Install(ComponentModel model)
    {
        var concerns = model.Lifecycle.DecommissionConcerns.ToArray();
        if (concerns.Length == 0 || concerns.Any(concern => concern is DeferredNativeConcern or DeferredNativeLateBoundConcern or MsDecommissionConcern)) return;
        foreach (var concern in concerns) model.Lifecycle.Remove(concern);
        // Factory tracking depends on whether all original concerns are late-bound.
        model.Lifecycle.Add(concerns.All(concern => concern is LateBoundConcerns<IDecommissionConcern>)
            ? new DeferredNativeLateBoundConcern(concerns) : new DeferredNativeConcern(concerns));
    }
}

internal sealed class DeferredNativeConcern(IDecommissionConcern[] concerns) : IDecommissionConcern
{
    public void Apply(ComponentModel model, object component) => Apply(model, component, null);

    internal void Apply(ComponentModel model, object component, System.Action additional)
    {
        if (component is GlobalMsLifetimeScope root)
        {
            root.Decommission(() => ApplyOriginal(model, component, additional));
            return;
        }
        if (component is MsLifetimeScope or ScopedWindsorServiceProvider or RootLifetime)
        {
            ApplyOriginal(model, component, additional);
            return;
        }
        if (MsAsyncDisposal.Current is { } synchronous)
        {
            if (synchronous.StopDecommission) return;
            try { ApplyOriginal(model, component, additional); }
            catch (System.Exception exception)
            {
                synchronous.Capture(exception);
                synchronous.StopDecommission = true;
            }
            return;
        }
        ApplyOriginal(model, component, additional);
    }

    private void ApplyOriginal(ComponentModel model, object component, System.Action additional)
    {
        foreach (var concern in concerns) concern.Apply(model, component);
        additional?.Invoke();
    }
}

internal sealed class DeferredNativeLateBoundConcern(IDecommissionConcern[] concerns) : LateBoundDecommissionConcerns
{
    private readonly DeferredNativeConcern _concern = new(concerns);
    public override void Apply(ComponentModel model, object component)
        => _concern.Apply(model, component, () => { if (HasConcerns) base.Apply(model, component); });
}
