using System;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.SubSystems.Naming;
using Castle.MicroKernel.Registration;
using System.Linq;
using Castle.MicroKernel.SubSystems.Configuration;
using Castle.Windsor.MsDependencyInjection.Keyed;
using Microsoft.Extensions.DependencyInjection;
using ServiceDescriptor = Microsoft.Extensions.DependencyInjection.ServiceDescriptor;

namespace Castle.Windsor.MsDependencyInjection
{
    public static class WindsorRegistrationHelper
    {
        internal const string ServiceTypeProperty = "MsServiceType";
        internal const string InfrastructureProperty = "MsInfrastructure";

        internal static bool RequiresBoxing(Type type)
        {
            return type.IsValueType || type == typeof(string);
        }

        private static bool HasLocalComponent(IWindsorContainer container, Type type)
        {
            return ((INamingSubSystem)container.Kernel.GetSubSystem(SubSystemConstants.NamingKey)).Contains(type);
        }

        /// <summary>
        /// Adds all given services from <see cref="Microsoft.Extensions.DependencyInjection"/>
        /// to the Castle Windsor container.
        /// </summary>
        /// <returns>A Castle Windsor service provider.</returns>
        public static IServiceProvider CreateServiceProvider(IWindsorContainer container, IServiceCollection services)
        {
            return CreateServiceProvider(container, services, validateScopes: false);
        }

        public static IServiceProvider CreateServiceProvider(IWindsorContainer container, IServiceCollection services, bool validateScopes)
        {
            using var origin = WindsorServiceScopeFactory.UsingFactoryRoot(null);
            AddServices(container, services);
            var root = container.Resolve<GlobalMsLifetimeScope>();
            root.ValidateScopes = validateScopes;
            using (MsLifetimeScope.Using(root))
            {
                return container.Resolve<GlobalScopedWindsorServiceProvider>();
            }
        }

        /// <summary>
        /// Used to add services to an existing container, without creating a <see cref="IServiceProvider"/>.
        /// </summary>
        public static void AddServices(this IWindsorContainer container, IServiceCollection services)
        {
            container.Install(new ServiceCollectionInstaller(services));
        }

        internal static void AddServicesInternal(this IWindsorContainer container, IServiceCollection services)
        {
            AddBaseServices(container);
            AddSubResolvers(container);
            AddServicesCollection(container, services);
        }

        private static void AddBaseServices(IWindsorContainer container)
        {
            if (!HasLocalComponent(container, typeof(MsScopeState)))
            {
                container.Register(Component.For<MsScopeState>().LifestyleScoped<MsScopedAccesor>());
            }

            if (!HasLocalComponent(container, typeof(NativeDecommissionScheduler)))
            {
                container.Register(Component.For<NativeDecommissionScheduler>().Instance(new NativeDecommissionScheduler(container.Kernel)));
            }

            if (!HasLocalComponent(container, typeof(ScopedResolution)))
            {
                container.Register(Component.For<ScopedResolution>()
                .UsingFactoryMethod((_, _, context) => new ScopedResolution((Func<object>)context.AdditionalArguments["resolve"]), managedExternally: true)
                .LifestyleTransient());
            }

            if (!HasLocalComponent(container, typeof(IWindsorContainer)))
            {
                container.Register(
                    Component.For<IWindsorContainer>()
                        .Instance(container)
                        .LifestyleSingleton()
                );
            }

            if (!HasLocalComponent(container, typeof(KeyedServiceRegistry)))
            {
                container.Register(
                    Component.For<KeyedServiceRegistry>()
                        .LifestyleSingleton()
                );
            }

            if (!HasLocalComponent(container, typeof(TypeKeyedMetadataRegistry)))
            {
                container.Register(
                    Component.For<TypeKeyedMetadataRegistry>()
                        .LifestyleSingleton()
                );
            }

            if (!HasLocalComponent(container, typeof(GlobalMsLifetimeScope)))
            {
                container.Register(
                    Component.For<GlobalMsLifetimeScope>()
                        .LifestyleSingleton()
                );
            }

            if (!HasLocalComponent(container, typeof(MsLifetimeScopeProvider)))
            {
                container.Register(
                    Component.For<MsLifetimeScopeProvider>()
                        .UsingFactoryMethod((kernel, context) => new MsLifetimeScopeProvider(MsLifetimeScopeProvider.SelectScope(kernel, context)))
                        .LifestyleTransient()
                );
            }

            if (!container.Kernel.HasComponent(typeof(IServiceScopeFactory)) || !HasLocalComponent(container, typeof(IServiceScopeFactory)) &&
                container.Kernel.GetHandler(typeof(IServiceScopeFactory)).ComponentModel.Implementation == typeof(WindsorServiceScopeFactory))
            {
                container.Register(
                    Component.For<IServiceScopeFactory>()
                        .ImplementedBy<WindsorServiceScopeFactory>()
                        .AddDescriptor(new ScopeFactoryEntryResolution(container))
                        .LifestyleSingleton()
                );
            }

            if (!container.Kernel.HasComponent(typeof(IServiceProvider)) || !HasLocalComponent(container, typeof(IServiceProvider)) &&
                container.Kernel.GetHandler(typeof(IServiceProvider)).ComponentModel.ExtendedProperties[InfrastructureProperty] is true)
            {
                container.Register(
                    Component.For<IServiceProvider, IServiceProviderIsService, IKeyedServiceProvider, IServiceProviderIsKeyedService>()
                        .Forward<ScopedWindsorServiceProvider>()
                        .ImplementedBy<ScopedWindsorServiceProvider>()
                        .ExtendedProperties(Property.ForKey(InfrastructureProperty).Eq(true))
                        .LifestyleTransient()
                );
            }

            if (!HasLocalComponent(container, typeof(GlobalScopedWindsorServiceProvider)))
            {
                container.Register(
                    Component.For<GlobalScopedWindsorServiceProvider>()
                        .LifestyleTransient()
                );
            }
        }

        private static void AddSubResolvers(IWindsorContainer container)
        {
            if (container.Kernel.GetSubSystem(SubSystemConstants.NamingKey) is KeyedServicesNamingSubSystem)
            {
                return;
            }

            var naming = (INamingSubSystem)container.Kernel.GetSubSystem(SubSystemConstants.NamingKey);
            container.Kernel.AddSubSystem(SubSystemConstants.NamingKey,
                new KeyedServicesNamingSubSystem(naming, container.Resolve<KeyedServiceRegistry>()));
            var constructorDependencies = new KeyedConstructorDependencyContributor(
                container.Resolve<TypeKeyedMetadataRegistry>(), container.Resolve<KeyedServiceRegistry>());
            container.Kernel.ComponentModelBuilder.AddContributor(constructorDependencies);
            foreach (var handler in naming.GetAllHandlers())
            {
                constructorDependencies.ProcessModel(container.Kernel, handler.ComponentModel);
            }
            // [FromKeyedServices] / [ServiceKey] constructor parameter injection.
            // Has to be the first one, so it handles keyed collections.
            // The sub-resolver takes the container directly: routing through ScopedWindsorServiceProvider
            // would push that provider's own MsLifetimeScope onto the AsyncLocal, and the captured-at-
            // registration provider holds a null scope, downgrading scoped keyed services to transient.
            container.Kernel.Resolver.AddSubResolver(
                new KeyedServicesSubResolver(
                    container.Resolve<TypeKeyedMetadataRegistry>(),
                    container.Resolve<KeyedServiceRegistry>(),
                    container));

            // ASP.NET Core uses IEnumerable<T> to resolve a list of types.
            // Since some of these types are optional, Windsor must also return empty collections.
            container.Kernel.Resolver.AddSubResolver(new MsCompatibleCollectionResolver(container.Kernel));

            // Workaround for Options resolve problem. See https://github.com/aspnetboilerplate/aspnetboilerplate/issues/1563#issuecomment-261654317
            container.Kernel.Resolver.AddSubResolver(new MsOptionsSubResolver(container.Kernel));
        }

        private static void AddServicesCollection(IWindsorContainer container, IServiceCollection services)
        {
            var keyedServiceRegistry = container.Resolve<KeyedServiceRegistry>();

            foreach (var serviceDescriptor in services)
            {
                if (!serviceDescriptor.IsKeyedService)
                {
                    if (serviceDescriptor.ImplementationInstance == container)
                    {
                        // Already registered before
                        continue;
                    }

                    RegisterNonKeyedServiceDescriptor(container, serviceDescriptor);
                }
                else
                {
                    var keyedServiceId = new KeyedServiceId(serviceDescriptor.ServiceType, serviceDescriptor.ServiceKey);

                    if (serviceDescriptor.ServiceKey != KeyedService.AnyKey)
                    {
                        var windsorKey = keyedServiceRegistry.RegisterExplicitKeyService(keyedServiceId);
                        RegisterKeyedServiceDescriptor(container, serviceDescriptor, windsorKey, serviceDescriptor.ServiceKey);
                    }
                    else
                    {
                        keyedServiceRegistry.RegisterAnyKeyService(
                            keyedServiceId,
                            registerExpansion: (windsorKey, serviceKey) => RegisterKeyedServiceDescriptor(container, serviceDescriptor, windsorKey, serviceKey));
                    }
                }
            }
        }

        private static void RegisterNonKeyedServiceDescriptor(IWindsorContainer container, ServiceDescriptor serviceDescriptor)
        {
            // MS allows the same type to be registered multiple times.
            // Castle Windsor throws an exception in that case - it requires an unique name.
            // For that reason, we use Guids to ensure uniqueness.
            var serviceType = serviceDescriptor.ServiceType;
            var registrationType = RequiresBoxing(serviceType) ? typeof(object) : serviceType;
            string uniqueName = RequiresBoxing(serviceType)
                ? container.Resolve<KeyedServiceRegistry>().RegisterExplicitKeyService(new KeyedServiceId(serviceType, null))
                : serviceType.FullName + "_" + Guid.NewGuid();

            // The IsDefault() calls are important because this makes sure that the last service
            // is returned in case of multiple registrations. This is by design in the MS library:
            // https://github.com/aspnet/DependencyInjection/blob/dev/src/Microsoft.Extensions.DependencyInjection.Specification.Tests/DependencyInjectionSpecificationTests.cs#L254

            if (serviceDescriptor.ImplementationType != null)
            {
                container.Register(
                    Component.For(registrationType)
                        .Named(uniqueName)
                        .IsDefault()
                        .ImplementedBy(serviceDescriptor.ImplementationType)
                        .ConfigureActivator(serviceDescriptor.ImplementationType)
                        .ConfigureLifecycle(serviceDescriptor.Lifetime, serviceType));
            }
            else if (serviceDescriptor.ImplementationFactory != null)
            {
                var serviceDescriptorRef = serviceDescriptor;
                container.Register(
                    Component.For(registrationType)
                        .Named(uniqueName)
                        .IsDefault()
                        .UsingFactoryMethod(c => serviceDescriptorRef.ImplementationFactory(c.Resolve<IServiceProvider>()))
                        .ConfigureLifecycle(serviceDescriptor.Lifetime, serviceType, factory: true)
                    );
            }
            else
            {
                container.Register(
                    Component.For(registrationType)
                        .Named(uniqueName)
                        .IsDefault()
                        .Instance(serviceDescriptor.ImplementationInstance)
                        .ConfigureLifecycle(serviceDescriptor.Lifetime, serviceType, owned: false)
                    );
            }
        }

        private static void RegisterKeyedServiceDescriptor(IWindsorContainer container, ServiceDescriptor serviceDescriptor, string windsorKey, object serviceKey)
        {
            var serviceType = serviceDescriptor.ServiceType;
            var registrationType = RequiresBoxing(serviceType) ? typeof(object) : serviceType;
            if (serviceDescriptor.KeyedImplementationType != null)
            {
                container.Register(
                    Component.For(registrationType)
                        .Named(windsorKey)
                        .ImplementedBy(serviceDescriptor.KeyedImplementationType)
                        .ConfigureActivator(serviceDescriptor.KeyedImplementationType)
                        .ConfigureLifecycle(serviceDescriptor.Lifetime, serviceType));
            }
            else if (serviceDescriptor.KeyedImplementationFactory != null)
            {
                container.Register(
                    Component.For(registrationType)
                        .Named(windsorKey)
                        .UsingFactoryMethod((c, context) => serviceDescriptor.KeyedImplementationFactory!(c.Resolve<IServiceProvider>(),
                            c.Resolve<KeyedServiceRegistry>().TryGetLookupKey(context, windsorKey, out var lookupKey) ? lookupKey : serviceKey))
                        .ConfigureLifecycle(serviceDescriptor.Lifetime, serviceType, factory: true)
                    );
            }
            else
            {
                container.Register(
                    Component.For(registrationType)
                        .Named(windsorKey)
                        .Instance(serviceDescriptor.KeyedImplementationInstance)
                        .ConfigureLifecycle(serviceDescriptor.Lifetime, serviceType, owned: false)
                    );
            }
        }

        private static ComponentRegistration<object> ConfigureActivator(this ComponentRegistration<object> registration, Type implementation)
        {
            return implementation.IsValueType ? registration.Activator<MsValueTypeActivator>() : registration;
        }

        private static ComponentRegistration<object> ConfigureLifecycle(this ComponentRegistration<object> registrationBuilder, ServiceLifetime serviceLifetime, Type serviceType, bool factory = false, bool owned = true)
        {
            registrationBuilder.ExtendedProperties(Property.ForKey(ServiceTypeProperty).Eq(serviceType));
            if (owned)
            {
                registrationBuilder.AddDescriptor(new MsOwnedLifecycleContributor(factory));
            }

            switch (serviceLifetime)
            {
                case ServiceLifetime.Transient:
                    if (factory)
                    {
                        registrationBuilder.LifestyleCustom<MsFactoryTransientLifestyleManager>();
                    }
                    else
                    {
                        registrationBuilder.LifestyleTransient();
                    }

                    break;
                case ServiceLifetime.Scoped:
                    registrationBuilder.LifestyleCustom<MsScopedLifestyleManager>();
                    break;
                case ServiceLifetime.Singleton:
                    registrationBuilder.LifestyleCustom<MsSingletonLifestyleManager>();
                    break;
                default:
                    throw new NotImplementedException("Unknown ServiceLifetime: " + serviceLifetime);
            }

            return registrationBuilder;
        }
    }

    internal sealed class ServiceCollectionInstaller : IWindsorInstaller
    {
        private readonly IServiceCollection _services;

        public ServiceCollectionInstaller(IServiceCollection services)
        {
            _services = services;
        }

        public void Install(IWindsorContainer container, IConfigurationStore store)
        {
            container.AddServicesInternal(_services);
        }
    }
}
