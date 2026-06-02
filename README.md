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

## Keyed Services

The adapter natively supports [keyed services](https://learn.microsoft.com/en-us/dotnet/core/extensions/dependency-injection#keyed-services) without requiring any changes to Castle Windsor. Keyed and non-keyed registrations are fully isolated: resolving `IEnumerable<T>` returns only non-keyed services, while keyed lookups return only services registered under the requested key.

You register and resolve keyed services exactly as you would with the default Microsoft DI container:

````C#
services.AddKeyedSingleton<INotifier, EmailNotifier>("email");
services.AddKeyedSingleton<INotifier, SmsNotifier>("sms");

var provider = WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);

var email = provider.GetRequiredKeyedService<INotifier>("email");
var sms = provider.GetKeyedService<INotifier>("sms");
````

The `[FromKeyedServices]` and `[ServiceKey]` constructor attributes are also supported:

````C#
public class OrderProcessor
{
    public OrderProcessor([FromKeyedServices("email")] INotifier notifier)
    {
        ...
    }
}
````

The `KeyedService.AnyKey` registration moniker is supported as well — a service registered with `AnyKey` is resolved for any requested key.

> **Note:** This release targets the .NET 10 keyed-services specification. Because of [breaking changes in .NET 10](https://learn.microsoft.com/en-us/dotnet/core/compatibility/extensions/10.0/getkeyedservice-anykey) around `KeyedService.AnyKey`, the adapter implements the latest spec only.

## License
[MIT](https://github.com/volosoft/castle-windsor-ms-adapter/blob/master/LICENSE)

