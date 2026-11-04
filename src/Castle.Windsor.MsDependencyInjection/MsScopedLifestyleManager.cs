using System;
using Castle.MicroKernel;
using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Extends Windsor's <see cref="ScopedLifestyleManager"/> to work as 
    /// MS style scope using <see cref="MsScopedAccesor"/>.
    /// </summary>
    public class MsScopedLifestyleManager : ScopedLifestyleManager
    {
        public MsScopedLifestyleManager()
            : base(new MsScopedAccesor())
        {

        }

        public override object Resolve(CreationContext context, IReleasePolicy releasePolicy)
        {
            var currentScope = MsLifetimeScopeProvider.SelectScope(Kernel, context);
            using var scope = MsLifetimeScope.Using(currentScope);
            var owner = MsScopeState.Resolve(Kernel, currentScope);
            var root = Kernel.HasComponent(typeof(GlobalMsLifetimeScope)) ? Kernel.Resolve<GlobalMsLifetimeScope>() : null;
            if (root?.ValidateScopes == true && (currentScope == null || owner is GlobalMsLifetimeScope ||
                ReferenceEquals(currentScope.WindsorLifeTimeScope, root.WindsorLifeTimeScope)))
            {
                throw new InvalidOperationException($"Cannot resolve scoped service '{Model.ExtendedProperties[WindsorRegistrationHelper.ServiceTypeProperty] ?? Model.Implementation}' from the root provider or a singleton.");
            }

            if (currentScope == null)
            {
                //Act as transient!
                var burden = CreateInstance(context, false);
                if (!releasePolicy.HasTrack(burden.Instance))
                {
                    Track(burden, releasePolicy);
                }

                return burden.Instance;
            }

            if (owner is GlobalMsLifetimeScope global && global.WindsorLifeTimeScope is MsScopeCache cache)
            {
                cache.RecordRootDependency(Model, context);
            }

            if (owner != null)
            {
                return base.Resolve(context, releasePolicy);
            }

            lock (currentScope)
            {
                return base.Resolve(context, releasePolicy);
            }
        }
    }
}
