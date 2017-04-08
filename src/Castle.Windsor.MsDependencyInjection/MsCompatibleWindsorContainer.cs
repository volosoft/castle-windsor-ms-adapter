using Castle.Windsor.Installer;

namespace Castle.Windsor.MsDependencyInjection
{
    public class MsCompatibleWindsorContainer : WindsorContainer
    {
        public MsCompatibleWindsorContainer()
            : base(new MsCompatibleKernel(), new DefaultComponentInstaller())
        {

        }
    }
}