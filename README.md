# Castle Windsor ASP.NET Core / Microsoft.Extensions.DependencyInjection Adapter
This library is a Castle Windsor adapter for Microsoft.Extensions.DependencyInjection nuget package. It works for ASP.NET Core and other type of applications.

## How To Use?

1. Install Puget Package

`Install-Package Castle.Windsor.MsDependencyInjection`

2. Change Startup Class
For ASP.NET Core, open your Startup class and add these using statements:

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

## License
[MIT](https://github.com/volosoft/castle-windsor-ms-adapter/blob/master/LICENSE)

