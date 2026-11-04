using System;
using Castle.MicroKernel;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class ScopedResolution(Func<object> resolve)
{
    internal object Value { get; } = resolve();

    internal static object Run(IWindsorContainer container, Func<object> resolve)
        => ((ScopedResolution)((IKernelInternal)container.Kernel).Resolve(typeof(ScopedResolution),
            new Arguments { { "resolve", resolve } }, container.Kernel.ReleasePolicy, ignoreParentContext: true)).Value;
}
