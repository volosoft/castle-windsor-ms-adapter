namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public interface INavigationListener
    {

    }

    public interface IEntityStateListener
    {

    }

    public interface INavigationFixer : IEntityStateListener, INavigationListener
    {

    }

    public class NavigationFixer : INavigationFixer
    {
    }
}
