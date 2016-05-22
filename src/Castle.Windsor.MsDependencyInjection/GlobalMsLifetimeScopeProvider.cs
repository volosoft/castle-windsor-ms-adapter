namespace Castle.Windsor.MsDependencyInjection
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