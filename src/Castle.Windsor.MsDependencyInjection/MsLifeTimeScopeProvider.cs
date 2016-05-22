namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Used to obtain true lifetime scope for dependent service.
    /// </summary>
    public class MsLifetimeScopeProvider 
    {
        public MsLifetimeScope LifetimeScope { get; }

        public MsLifetimeScopeProvider(GlobalMsLifetimeScopeProvider globalMsLifetimeScopeProvider)
        {
            LifetimeScope = MsLifetimeScope.Current ??
                            globalMsLifetimeScopeProvider.LifetimeScope;
        }
    }
}