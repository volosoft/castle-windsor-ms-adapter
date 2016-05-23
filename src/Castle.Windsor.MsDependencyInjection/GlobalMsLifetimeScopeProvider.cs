using System;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Singleton instance (per <see cref="IWindsorContainer"/>) of
    /// life time scope.
    /// </summary>
    public class GlobalMsLifetimeScopeProvider : IDisposable
    {
        public MsLifetimeScope LifetimeScope { get; private set; }

        public GlobalMsLifetimeScopeProvider()
        {
            LifetimeScope = new MsLifetimeScope();
        }

        public void Dispose()
        {
            LifetimeScope.Dispose();
        }
    }
}