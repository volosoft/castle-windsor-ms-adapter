using System;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.ComponentActivator;
using Castle.MicroKernel.Context;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class MsValueTypeActivator : DefaultComponentActivator
    {
        public MsValueTypeActivator(ComponentModel model, IKernelInternal kernel,
            ComponentInstanceDelegate onCreation, ComponentInstanceDelegate onDestruction)
            : base(model, kernel, onCreation, onDestruction)
        {
        }

        protected override object CreateInstance(CreationContext context, ConstructorCandidate constructor, object[] arguments)
        {
            if (constructor == null)
            {
                throw new InvalidOperationException($"No public constructor is available for '{Model.Implementation}'.");
            }

            var instance = constructor.Constructor.Invoke(arguments);
            return Kernel.ProxyFactory.ShouldCreateProxy(Model)
                ? Kernel.ProxyFactory.Create(Kernel, instance, Model, context, arguments)
                : instance;
        }
    }
}
