using Castle.MicroKernel.Handlers;
using Castle.MicroKernel;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class NullFactoryResult
{
    internal const string FactoryMarker = "MsNullableFactory";
    internal static object Unwrap(object value) => value is NullFactoryResult ? null : value;
}

internal sealed class NullFactoryResolution : IResolveExtension
{
    private IHandler _handler;
    public void Init(IKernel kernel, IHandler handler) => _handler = handler;
    public void Intercept(ResolveInvocation invocation)
    {
        invocation.Proceed();
        if (invocation.ResolvedInstance is not NullFactoryResult result) return;
        if (invocation.Burden?.RequiresPolicyRelease == true)
            MsLifetimeScope.Current?.AddInstance(result);
        if (invocation.Context.Handler != null && !ReferenceEquals(invocation.Context.Handler.ComponentModel, _handler.ComponentModel))
            invocation.ResolvedInstance = null;
    }
}
