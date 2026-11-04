using System.Linq;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Used to obtain true lifetime scope for dependent service.
    /// </summary>
    public class MsLifetimeScopeProvider
    {
        public IMsLifetimeScope LifetimeScope { get; }

        public MsLifetimeScopeProvider()
            : this(MsLifetimeScope.Current)
        {
        }

        internal MsLifetimeScopeProvider(IMsLifetimeScope scope)
        {
            LifetimeScope = scope;
        }

        internal static IMsLifetimeScope SelectScope(IKernel kernel, CreationContext context)
        {
            var scope = MsLifetimeScope.Current;
            if (scope is GlobalMsLifetimeScope || !kernel.HasComponent(typeof(GlobalMsLifetimeScope)))
            {
                return scope;
            }

            var cachedOwner = false;
            context.SelectScopeRoot(handlers =>
            {
                cachedOwner = handlers.Any(handler =>
                {
                    var model = handler.ComponentModel;
                    return model.Implementation != typeof(WindsorServiceScopeFactory) &&
                        model.Implementation != typeof(GlobalMsLifetimeScope) &&
                        (model.LifestyleType is LifestyleType.Singleton or LifestyleType.Undefined or LifestyleType.Thread or LifestyleType.Pooled ||
                         model.CustomLifestyle != null && typeof(SingletonLifestyleManager).IsAssignableFrom(model.CustomLifestyle));
                });
                return null;
            });
            return cachedOwner ? kernel.Resolve<GlobalMsLifetimeScope>() : scope;
        }
    }
}
