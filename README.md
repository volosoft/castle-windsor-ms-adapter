# Castle Windsor Microsoft.Extensions.DependencyInjection Adapter
This library is a Castle Windsor adapter for Microsoft.Extensions.DependencyInjection nuget package.
# How To Use?
1. Install Puget Package

`Install-Package Castle.Windsor.MsDependencyInjection`

2. Change Startup Class
Open your Startup class and add these using statements:

````C#
using Castle.Windsor;
using Castle.Windsor.MsDependencyInjection;
````

Find ConfigureServices method:

````C#
public void ConfigureServices(IServiceCollection services)
{
    ...
}
````

Change it like that:

````C#
public IServiceProvider ConfigureServices(IServiceCollection services)
{
    ...

    return WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);
}
````

Changed return type  from void to IServiceProvider and used WindsorRegistrationHelper.
