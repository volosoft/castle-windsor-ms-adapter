namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Used to obtain true lifetime scope for dependent service.
    /// </summary>
    public class MsLifetimeScopeProvider 
    {
        public IMsLifetimeScope LifetimeScope { get; }

        public MsLifetimeScopeProvider()
        {
            LifetimeScope = MsLifetimeScope.Current;
        }
    }
}