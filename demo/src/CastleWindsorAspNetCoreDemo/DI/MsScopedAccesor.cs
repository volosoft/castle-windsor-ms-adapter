using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle.Scoped;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IScopeAccessor"/> to get <see cref="ILifetimeScope"/>
    /// from <see cref="MsLifetimeScope.Current"/>.
    /// </summary>
    public class MsScopedAccesor : IScopeAccessor
    {
        public void Dispose()
        {
            
        }

        public ILifetimeScope GetScope(CreationContext context)
        {
            return MsLifetimeScope.Current.WindsorLifeTimeScope;
        }
    }
}