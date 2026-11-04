using System;
using Castle.MicroKernel;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class ScopedResolution
    {
        internal object Value { get; }

        public ScopedResolution(Func<object> resolve)
        {
            Value = resolve();
        }

        internal static object Run(IWindsorContainer container, Func<object> resolve)
        {
            return ((ScopedResolution)((IKernelInternal)container.Kernel).Resolve(typeof(ScopedResolution),
                new Arguments { { "resolve", resolve } }, container.Kernel.ReleasePolicy, ignoreParentContext: true)).Value;
        }
    }
}
