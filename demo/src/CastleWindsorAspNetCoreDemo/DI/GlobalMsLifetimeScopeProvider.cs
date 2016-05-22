using Castle.Windsor;

namespace CastleWindsorAspNetCoreDemo.DI
{
    /// <summary>
    /// Singleton instance (per <see cref="IWindsorContainer"/>) of
    /// life time scope.
    /// </summary>
    public class GlobalMsLifetimeScopeProvider
    {
        public MsLifetimeScope LifetimeScope { get; private set; }

        public GlobalMsLifetimeScopeProvider()
        {
            LifetimeScope = new MsLifetimeScope();
        }
    }
}