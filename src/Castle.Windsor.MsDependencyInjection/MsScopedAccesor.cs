using Castle.MicroKernel.Context;
using Castle.MicroKernel.Lifestyle.Scoped;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IScopeAccessor"/> to get <see cref="ILifetimeScope"/>
    /// from current scope.
    /// </summary>
    public class MsScopedAccesor : IScopeAccessor
    {
        public void Dispose()
        {
            
        }

        public ILifetimeScope GetScope(CreationContext context)
        {
            return ScopedWindsorServiceProvider.Current.Scope.LifeTimeScope;
        }
    }
}