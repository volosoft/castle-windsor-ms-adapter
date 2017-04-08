namespace Castle.Windsor.MsDependencyInjection
{
    public class GlobalMsLifetimeScope : MsLifetimeScope
    {
        public GlobalMsLifetimeScope(IWindsorContainer container) 
            : base(container)
        {
        }
    }
}