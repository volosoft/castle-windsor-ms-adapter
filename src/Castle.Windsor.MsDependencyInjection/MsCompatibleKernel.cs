using Castle.Core;
using Castle.MicroKernel;

namespace Castle.Windsor.MsDependencyInjection
{
    public class MsCompatibleKernel : DefaultKernel
    {
        public override ILifestyleManager CreateLifestyleManager(ComponentModel model, IComponentActivator activator)
        {
            if (model.LifestyleType != LifestyleType.Transient)
            {
                return base.CreateLifestyleManager(model, activator);
            }

            var manager = new MsScopedTransientLifestyleManager();
            manager.Init(activator, this, model);
            return manager;
        }
    }
}