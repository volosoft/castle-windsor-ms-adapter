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

## Lifetimes and disposal

Both `WindsorRegistrationHelper.CreateServiceProvider` and `WindsorServiceProviderFactory` create a disposable root provider. Dispose the root provider, or the host that owns it, to release the container. Dispose child scopes when their work completes. With the default configuration, scopes from the factory root retain ownership of their `IServiceCollection` registrations until the scopes close; the static helper retains its cascading parent lifetime. Native Windsor registrations retain Windsor's release policy and container lifetime.

Release `IServiceCollection` instances resolved from factory scopes by disposing their scope. `IWindsorContainer.Release` does not release those instances.

Native Windsor registrations, interceptors, and named dependencies remain available. Microsoft DI factory registrations may return null; required-service lookups reject a null result.

Use `CreateAsyncScope()` and `DisposeAsync()` when services implement `IAsyncDisposable`. For services registered through `IServiceCollection`, asynchronous disposal prefers `DisposeAsync()` when both disposal interfaces are implemented. Synchronous disposal rejects asynchronous-only services.

DisposeAsync can use a dedicated thread to preserve Windsor disposal order, including scopes with asynchronous services or factory registrations. Allow for this thread cost when many scopes close concurrently.

Scope validation is disabled by default. Enable it with `new WindsorServiceProviderFactory(validateScopes: true)` or `WindsorRegistrationHelper.CreateServiceProvider(container, services, validateScopes: true)` to reject scoped services resolved from the root or captured by a singleton.

The validation setting belongs to the shared Windsor container. Creating another provider for that container reapplies the setting, including the default `false`.

## License
[MIT](https://github.com/volosoft/castle-windsor-ms-adapter/blob/master/LICENSE)

