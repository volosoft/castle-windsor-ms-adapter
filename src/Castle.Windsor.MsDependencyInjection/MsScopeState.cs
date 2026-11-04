using Castle.MicroKernel;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class MsScopeState
{
    internal MsLifetimeScope Owner { get; set; }

    internal static MsLifetimeScope Resolve(IKernel kernel, IMsLifetimeScope scope)
    {
        if (scope is MsLifetimeScope owner) return owner;
        if (scope == null || !kernel.HasComponent(typeof(MsScopeState))) return null;
        using (MsLifetimeScope.Using(scope)) return kernel.Resolve<MsScopeState>().Owner;
    }
}
