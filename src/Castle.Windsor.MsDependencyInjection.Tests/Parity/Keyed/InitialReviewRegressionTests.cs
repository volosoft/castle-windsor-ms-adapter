using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Keyed;

public class InitialReviewRegressionTests
{
    [Fact]
    public void Ordinary_Constructor_Does_Not_Consume_Keyed_Registration()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedTransient<Resource>("dep");
            services.AddTransient<PlainConsumer>();
            services.AddTransient<OptionalConsumer>();
        }, context =>
        {
            Assert.ThrowsAny<Exception>(() => context.Provider.GetRequiredService<PlainConsumer>());
            Assert.Null(context.Provider.GetRequiredService<OptionalConsumer>().Dependency);
        });
    }

    [Fact]
    public void Missing_Keyed_Parameter_Does_Not_Select_Long_Constructor_Or_Plain_Service()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddSingleton<Resource>();
            services.AddTransient<FallbackConsumer>();
        }, context => Assert.False(context.Provider.GetRequiredService<FallbackConsumer>().UsedLongConstructor));
    }

    [Fact]
    public void Missing_Keyed_Options_Do_Not_Fall_Back_To_Ordinary_Options()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddOptions();
            services.AddTransient<OptionsFallbackConsumer>();
        }, context => Assert.False(context.Provider.GetRequiredService<OptionsFallbackConsumer>().UsedLongConstructor));
    }

    [Fact]
    public void Ordinary_Open_Generic_Wins_Over_Closed_Keyed_Registration()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddTransient(typeof(IRepository<>), typeof(OpenRepository<>));
            services.AddKeyedTransient<IRepository<int>, ClosedRepository>("k");
            services.AddTransient<RepositoryConsumer>();
        }, context =>
        {
            Assert.IsType<OpenRepository<int>>(context.Provider.GetRequiredService<IRepository<int>>());
            Assert.IsType<OpenRepository<int>>(context.Provider.GetRequiredService<RepositoryConsumer>().Repository);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Keyed_Generic_Collection_Preserves_Interleaved_Registration_Order(bool anyKey)
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedTransient(typeof(IRepository<>), "k", typeof(OpenRepository<>));
            services.AddKeyedTransient<IRepository<int>, ClosedRepository>("k");
            services.AddKeyedTransient(typeof(IRepository<>), "k", typeof(OtherRepository<>));
        }, context => Assert.Equal(anyKey ? new[] { typeof(ClosedRepository) } : new[] { typeof(OpenRepository<int>), typeof(ClosedRepository), typeof(OtherRepository<int>) },
            context.Provider.GetKeyedServices<IRepository<int>>(anyKey ? KeyedService.AnyKey : "k").Select(item => item.GetType())));
    }

    [Fact]
    public void Closed_AnyKey_Wins_Over_Open_Explicit_And_Previous_Expansion()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedTransient(typeof(IRepository<>), "k", typeof(OpenRepository<>));
            services.AddKeyedTransient(typeof(IRepository<>), KeyedService.AnyKey, typeof(OtherRepository<>));
            services.AddKeyedTransient<IRepository<int>, ClosedRepository>(KeyedService.AnyKey);
        }, context =>
        {
            Assert.IsType<OtherRepository<string>>(context.Provider.GetRequiredKeyedService<IRepository<string>>("other"));
            Assert.IsType<ClosedRepository>(context.Provider.GetRequiredKeyedService<IRepository<int>>("other"));
            Assert.IsType<ClosedRepository>(context.Provider.GetRequiredKeyedService<IRepository<int>>("k"));
        });
    }

    [Fact]
    public void Explicit_Collections_Win_In_Provider_And_Constructor_Paths()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddSingleton<IEnumerable<Resource>>(new[] { new Resource() });
            services.AddKeyedSingleton<IEnumerable<Resource>>("dep", new[] { new Resource(), new Resource() });
            services.AddTransient<KeyedCollectionConsumer>();
            services.AddTransient<NullCollectionConsumer>();
            services.AddTransient<InheritedCollectionConsumer>();
        }, context =>
        {
            Assert.Equal(2, context.Provider.GetRequiredKeyedService<IEnumerable<Resource>>("dep").Count());
            Assert.Equal(2, context.Provider.GetRequiredService<KeyedCollectionConsumer>().Items.Count());
            Assert.Single(context.Provider.GetRequiredService<NullCollectionConsumer>().Items);
            Assert.Single(context.Provider.GetRequiredService<InheritedCollectionConsumer>().Items);
        });
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, true)]
    [InlineData(true, false, true)]
    [InlineData(true, true, true)]
    public void Singleton_Constructs_Dependencies_In_Root_And_Outlives_Child(bool keyed, bool factory, bool warmRoot)
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddSingleton<EventLog>();
            services.AddScoped<Resource>();
            services.AddKeyedScoped<Resource>("dep");
            if (keyed)
            {
                if (factory) services.AddKeyedSingleton<SingletonConsumer>("k", (provider, _) => new SingletonConsumer(provider.GetRequiredKeyedService<Resource>("dep")));
                else services.AddKeyedSingleton<SingletonConsumer>("k");
            }
            else
            {
                if (factory) services.AddSingleton(provider => new SingletonConsumer(provider.GetRequiredKeyedService<Resource>("dep")));
                else services.AddSingleton<SingletonConsumer>();
            }
        }, context =>
        {
            if (warmRoot) context.Provider.GetRequiredKeyedService<Resource>("dep");
            SingletonConsumer singleton;
            using (var scope = context.Provider.CreateScope())
            {
                singleton = keyed ? scope.ServiceProvider.GetRequiredKeyedService<SingletonConsumer>("k")
                    : scope.ServiceProvider.GetRequiredService<SingletonConsumer>();
                Assert.NotSame(scope.ServiceProvider.GetRequiredKeyedService<Resource>("dep"), singleton.Dependency);
            }
            Assert.False(singleton.Dependency.Disposed);
            Assert.Same(context.Provider.GetRequiredKeyedService<Resource>("dep"), singleton.Dependency);
            var previousEvents = singleton.Dependency.Events.Items.Count;
            context.DisposeProvider();
            Assert.True(singleton.Dependency.Disposed);
            Assert.Equal(new[] { "singleton", "resource" }, singleton.Dependency.Events.Items.Skip(previousEvents));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Container_Disposal_Releases_Singleton_Before_Its_Root_Dependency(bool providerFactory)
    {
        var services = new ServiceCollection();
        services.AddKeyedScoped<Resource>("dep");
        services.AddKeyedSingleton<SingletonConsumer>("k");
        IWindsorContainer container;
        IServiceProvider provider;
        if (providerFactory)
        {
            var factory = new WindsorServiceProviderFactory();
            container = factory.CreateBuilder(services);
            provider = factory.CreateServiceProvider(container);
        }
        else
        {
            container = new WindsorContainer();
            provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        }
        using (container)
        {
            SingletonConsumer singleton;
            using (var scope = provider.CreateScope())
                singleton = scope.ServiceProvider.GetRequiredKeyedService<SingletonConsumer>("k");
            Assert.False(singleton.Dependency.Disposed);
            container.Dispose();
            Assert.True(singleton.Dependency.Disposed);
            Assert.Equal(new[] { "singleton", "resource" }, singleton.Dependency.Events.Items);
        }
    }

    [Fact]
    public void Ordinary_Singleton_Uses_Root_Scoped_Dependency()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddScoped<Resource>();
            services.AddSingleton<PlainConsumer>();
        }, context =>
        {
            Resource resource;
            using (var scope = context.Provider.CreateScope())
            {
                resource = scope.ServiceProvider.GetRequiredService<PlainConsumer>().Dependency;
                Assert.NotSame(scope.ServiceProvider.GetRequiredService<Resource>(), resource);
            }
            Assert.False(resource.Disposed);
            Assert.Same(context.Provider.GetRequiredService<Resource>(), resource);
            context.DisposeProvider();
            Assert.True(resource.Disposed);
        });
    }

    [Theory]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void Null_Keyed_Factory_Is_Cached_By_Lifetime_And_Injected_As_Null(ServiceLifetime lifetime)
    {
        var calls = 0;
        ParityRunner.RunAssertParity(services =>
        {
            calls = 0;
            services.Add(ServiceDescriptor.DescribeKeyed(typeof(Resource), "dep", (_, _) => { calls++; return null; }, lifetime));
            services.AddTransient<NullFactoryConsumer>();
            services.AddTransient<RequiredNullFactoryConsumer>();
        }, context =>
        {
            using var scope = context.Provider.CreateScope();
            var provider = scope.ServiceProvider;
            Assert.Null(provider.GetKeyedService<Resource>("dep"));
            Assert.Null(provider.GetKeyedService<Resource>("dep"));
            Assert.Equal(lifetime == ServiceLifetime.Transient ? 2 : 1, calls);
            Assert.Null(Assert.Single(provider.GetKeyedServices<Resource>("dep")));
            Assert.Null(provider.GetRequiredService<NullFactoryConsumer>().Value);
            Assert.Null(provider.GetRequiredService<RequiredNullFactoryConsumer>().Value);
            Assert.Throws<InvalidOperationException>(() => provider.GetRequiredKeyedService<Resource>("dep"));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_Transient_Keys_Use_The_Current_Lookup_Object_In_Windsor(bool factory)
    {
        CompareKeyIdentity(services =>
        {
            var registered = new LookupKey("k");
            if (factory) services.AddKeyedTransient<KeyConsumer>(registered, (_, key) => new KeyConsumer(key));
            else services.AddKeyedTransient<KeyConsumer>(registered);
        }, (provider, microsoft) =>
        {
            var first = new LookupKey("k");
            var second = new LookupKey("k");
            Assert.Same(first, provider.GetRequiredKeyedService<KeyConsumer>(first).Key);
            Assert.Same(microsoft ? first : second, provider.GetRequiredKeyedService<KeyConsumer>(second).Key);
            Assert.Same(microsoft ? first : second, Assert.Single(provider.GetKeyedServices<KeyConsumer>(second)).Key);
        });
    }

    [Fact]
    public void Closed_Generic_Services_Use_Their_Current_Lookup_Key_In_Windsor()
    {
        CompareKeyIdentity(services =>
            services.AddKeyedTransient(typeof(GenericKeyConsumer<>), new LookupKey("k"), typeof(GenericKeyConsumer<>)), (provider, microsoft) =>
        {
            var first = new LookupKey("k");
            var second = new LookupKey("k");
            Assert.Same(first, provider.GetRequiredKeyedService<GenericKeyConsumer<int>>(first).Key);
            Assert.Same(second, provider.GetRequiredKeyedService<GenericKeyConsumer<string>>(second).Key);
            Assert.Same(microsoft ? first : second, provider.GetRequiredKeyedService<GenericKeyConsumer<int>>(second).Key);
            Assert.Same(microsoft ? second : first, provider.GetRequiredKeyedService<GenericKeyConsumer<string>>(first).Key);
        });
    }

    [Fact]
    public void Factory_Retry_Uses_The_Current_Lookup_Key_In_Windsor()
    {
        var calls = 0;
        CompareKeyIdentity(services =>
        {
            calls = 0;
            services.AddKeyedTransient<KeyConsumer>(new LookupKey("k"), (_, key) =>
                ++calls == 1 ? throw new FactoryFailure() : new KeyConsumer(key));
        }, (provider, microsoft) =>
        {
            var first = new LookupKey("k");
            var second = new LookupKey("k");
            Assert.Throws<FactoryFailure>(() => provider.GetRequiredKeyedService<KeyConsumer>(first));
            Assert.Same(microsoft ? first : second, provider.GetRequiredKeyedService<KeyConsumer>(second).Key);
            Assert.Equal(2, calls);
        });
    }

    [Fact]
    public void Nested_Collection_Query_Keeps_The_Outer_Lookup_Key()
    {
        var collectionKey = new LookupKey("k");
        var nestedKey = new LookupKey("k");
        var reference = false;
        CompareKeyIdentity(services =>
        {
            services.AddKeyedTransient<KeyConsumer>(new LookupKey("k"), (provider, key) =>
            {
                Assert.Same(reference ? collectionKey : nestedKey, provider.GetRequiredKeyedService<KeyConsumer>(nestedKey).Key);
                return new KeyConsumer(key);
            });
            services.AddKeyedTransient<KeyConsumer>(new LookupKey("k"), (_, key) => new KeyConsumer(key));
        }, (provider, microsoft) =>
        {
            reference = microsoft;
            Assert.All(provider.GetKeyedServices<KeyConsumer>(collectionKey), item => Assert.Same(collectionKey, item.Key));
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Equal_Keys_Select_The_Same_Descriptors_Without_Retaining_Transient_Keys(bool anyKeyFirst)
    {
        var firstRegistration = new LookupKey("k");
        var lastRegistration = new LookupKey("k");
        var singleLookup = new LookupKey("k");
        var collectionLookup = new LookupKey("k");
        var firstCalls = 0;
        var lastCalls = 0;
        CompareKeyIdentity(services =>
        {
            firstCalls = lastCalls = 0;
            services.AddKeyedTransient<KeyConsumer>(firstRegistration, (_, key) => { firstCalls++; return new KeyConsumer(key); });
            services.AddKeyedTransient<KeyConsumer>(lastRegistration, (_, key) => { lastCalls++; return new KeyConsumer(key); });
        }, (provider, microsoft) =>
        {
            if (anyKeyFirst)
            {
                var items = provider.GetKeyedServices<KeyConsumer>(KeyedService.AnyKey).ToArray();
                Assert.Same(firstRegistration, items[0].Key);
                Assert.Same(lastRegistration, items[1].Key);
                Assert.Same(microsoft ? lastRegistration : singleLookup, provider.GetRequiredKeyedService<KeyConsumer>(singleLookup).Key);
            }
            else
            {
                Assert.Same(singleLookup, provider.GetRequiredKeyedService<KeyConsumer>(singleLookup).Key);
                var items = provider.GetKeyedServices<KeyConsumer>(collectionLookup).ToArray();
                Assert.Same(collectionLookup, items[0].Key);
                Assert.Same(microsoft ? singleLookup : collectionLookup, items[1].Key);
            }
            Assert.Equal(1, firstCalls);
            Assert.Equal(2, lastCalls);
        });
    }

    [Fact]
    public void ServiceKey_Requires_Exact_Type_Or_Object()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedTransient<InterfaceKeyConsumer>("k");
            services.AddKeyedTransient<NullableKeyConsumer>(1);
        }, context =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Provider.GetRequiredKeyedService<InterfaceKeyConsumer>("k"));
            Assert.Throws<InvalidOperationException>(() => context.Provider.GetRequiredKeyedService<NullableKeyConsumer>(1));
        });
    }

    [Theory]
    [InlineData(ServiceLifetime.Transient)]
    [InlineData(ServiceLifetime.Scoped)]
    [InlineData(ServiceLifetime.Singleton)]
    public void Invalid_Key_Type_Does_Not_Poison_A_Valid_Equivalent_Lookup(ServiceLifetime lifetime)
    {
        ParityRunner.RunAssertParity(services => services.Add(ServiceDescriptor.DescribeKeyed(
            typeof(ExactKeyConsumer), new ExactKey("k"), typeof(ExactKeyConsumer), lifetime)), context =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Provider.GetRequiredKeyedService<ExactKeyConsumer>(new EquivalentKey("k")));
            var valid = new ExactKey("k");
            Assert.Same(valid, context.Provider.GetRequiredKeyedService<ExactKeyConsumer>(valid).Key);
        });
    }

    [Fact]
    public void Inherited_Key_Validation_Failure_Does_Not_Poison_The_Parent()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedTransient<ExactKeyConsumer>(new ExactKey("k"));
            services.AddKeyedTransient<ExactKeyParent>(new ExactKey("k"));
        }, context =>
        {
            Assert.Throws<InvalidOperationException>(() => context.Provider.GetRequiredKeyedService<ExactKeyParent>(new EquivalentKey("k")));
            var valid = new ExactKey("k");
            Assert.Same(valid, context.Provider.GetRequiredKeyedService<ExactKeyParent>(valid).Value.Key);
        });
    }

    [Fact]
    public void Nested_Query_Does_Not_Change_The_Parents_Inherited_Key()
    {
        var parentKey = new LookupKey("k");
        var nestedKey = new LookupKey("k");
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedTransient<NestedKeyConsumer>(new LookupKey("k"), (provider, _) =>
                new NestedKeyConsumer(provider.GetRequiredKeyedService<KeyConsumer>(nestedKey)));
            services.AddKeyedTransient<KeyConsumer>(new LookupKey("k"));
            services.AddKeyedTransient<InheritedKeyParent>(new LookupKey("k"));
        }, context => Assert.Same(parentKey,
            context.Provider.GetRequiredKeyedService<InheritedKeyParent>(parentKey).Later.Key));
    }

    [Fact]
    public void Parameter_Attributes_Are_Processed_In_Declaration_Order()
    {
        ParityRunner.RunAssertParity(services =>
        {
            services.AddKeyedSingleton<object>("dep", new object());
            services.AddKeyedTransient<KeyFirstConsumer>("k");
            services.AddKeyedTransient<DependencyFirstConsumer>("k");
        }, context =>
        {
            Assert.Equal("k", context.Provider.GetRequiredKeyedService<KeyFirstConsumer>("k").Value);
            Assert.Same(context.Provider.GetRequiredKeyedService<object>("dep"), context.Provider.GetRequiredKeyedService<DependencyFirstConsumer>("k").Value);
        });
    }

    [Fact]
    public void Keyed_Builtin_Availability_Does_Not_Imply_Keyed_Resolution()
    {
        ParityRunner.RunAssertParity(_ => { }, context =>
        {
            var probe = context.Provider.GetRequiredService<IServiceProviderIsKeyedService>();
            foreach (var type in new[] { typeof(IServiceProvider), typeof(IServiceScopeFactory), typeof(IServiceProviderIsService), typeof(IServiceProviderIsKeyedService) })
            {
                Assert.True(probe.IsKeyedService(type, "k"));
                Assert.Null(context.Provider.GetKeyedService(type, "k"));
            }
        });
    }

    [Fact]
    public void Disposed_Scope_Rejects_All_Provider_Resolution_Entries()
    {
        ParityRunner.RunAssertParity(services => services.AddKeyedScoped<Resource>("dep"), context =>
        {
            var scope = context.Provider.CreateScope();
            var provider = scope.ServiceProvider;
            provider.GetKeyedServices<Resource>("dep").ToArray();
            scope.Dispose();
            Assert.Throws<ObjectDisposedException>(() => provider.GetService<Resource>());
            Assert.Throws<ObjectDisposedException>(() => provider.GetRequiredService<Resource>());
            Assert.Throws<ObjectDisposedException>(() => provider.GetKeyedServices<Resource>("dep"));
            Assert.Throws<ObjectDisposedException>(() => provider.GetRequiredKeyedService<Resource>("dep"));
        });
    }

    [Fact]
    public void Concurrent_Cached_Collection_Publishes_One_Array()
    {
        ParityRunner.RunAssertParity(services => services.AddKeyedSingleton<Resource>("dep"), context =>
        {
            using var scope = context.Provider.CreateScope();
            var results = new IEnumerable<Resource>[16];
            Parallel.For(0, results.Length, index => results[index] = scope.ServiceProvider.GetKeyedServices<Resource>("dep"));
            Assert.All(results, result => Assert.Same(results[0], result));
        });
    }

    [Fact]
    public void Failed_Collection_Initialization_Can_Be_Retried()
    {
        var calls = 0;
        ParityRunner.RunAssertParity(services =>
        {
            calls = 0;
            services.AddKeyedSingleton<Resource>("dep", (_, _) => Interlocked.Increment(ref calls) == 1 ? throw new FactoryFailure() : new Resource());
        }, context =>
        {
            Assert.Throws<FactoryFailure>(() => context.Provider.GetKeyedServices<Resource>("dep"));
            var collection = context.Provider.GetKeyedServices<Resource>("dep");
            Assert.Single(collection);
            Assert.Same(collection, context.Provider.GetKeyedServices<Resource>("dep"));
            Assert.Equal(2, calls);
        });
    }

    [Fact]
    public void Existing_KeyedProvider_Service_Alias_Remains_Available()
    {
        using var container = new WindsorContainer();
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
        Assert.IsAssignableFrom<IKeyedServiceProvider>(provider.GetRequiredService<IKeyedServiceProvider>());
        Assert.True(provider.GetRequiredService<IServiceProviderIsService>().IsService(typeof(IKeyedServiceProvider)));
    }

    private static void CompareKeyIdentity(Action<IServiceCollection> configure, Action<IServiceProvider, bool> assert)
    {
        foreach (var microsoft in new[] { true, false })
        {
            var services = new ServiceCollection();
            configure(services);
            var provider = microsoft ? services.BuildServiceProvider()
                : WindsorRegistrationHelper.CreateServiceProvider(new WindsorContainer(), services);
            try
            {
                assert(provider, microsoft);
            }
            finally
            {
                (provider as IDisposable)?.Dispose();
            }
        }
    }

    public sealed class EventLog { public List<string> Items { get; } = new(); }
    public sealed class Resource : IDisposable
    {
        public Resource(EventLog events = null) { Events = events ?? new EventLog(); }
        public EventLog Events { get; }
        public bool Disposed { get; private set; }
        public void Dispose() { Assert.False(Disposed); Disposed = true; Events.Items.Add("resource"); }
    }
    public sealed class PlainConsumer(Resource dependency) { public Resource Dependency { get; } = dependency; }
    public sealed class OptionalConsumer(Resource dependency = null) { public Resource Dependency { get; } = dependency; }
    public sealed class FallbackConsumer
    {
        public FallbackConsumer() { }
        public FallbackConsumer([FromKeyedServices("missing")] Resource dependency) { UsedLongConstructor = true; }
        public bool UsedLongConstructor { get; }
    }
    public sealed class OptionsFallbackConsumer
    {
        public OptionsFallbackConsumer() { }
        public OptionsFallbackConsumer([FromKeyedServices("missing")] IOptions<Options> options) { UsedLongConstructor = true; }
        public bool UsedLongConstructor { get; }
    }
    public sealed class Options;
    public interface IRepository<T>;
    public sealed class OpenRepository<T> : IRepository<T>;
    public sealed class OtherRepository<T> : IRepository<T>;
    public sealed class ClosedRepository : IRepository<int>;
    public sealed class RepositoryConsumer(IRepository<int> repository) { public IRepository<int> Repository { get; } = repository; }
    public sealed class KeyedCollectionConsumer([FromKeyedServices("dep")] IEnumerable<Resource> items) { public IEnumerable<Resource> Items { get; } = items; }
    public sealed class NullCollectionConsumer([FromKeyedServices(null)] IEnumerable<Resource> items) { public IEnumerable<Resource> Items { get; } = items; }
    public sealed class InheritedCollectionConsumer([FromKeyedServices] IEnumerable<Resource> items) { public IEnumerable<Resource> Items { get; } = items; }
    public sealed class SingletonConsumer([FromKeyedServices("dep")] Resource dependency) : IDisposable
    {
        public Resource Dependency { get; } = dependency;
        public void Dispose() { Assert.False(Dependency.Disposed); Dependency.Events.Items.Add("singleton"); }
    }
    public sealed class NullFactoryConsumer([FromKeyedServices("dep")] Resource value = null) { public Resource Value { get; } = value; }
    public sealed class RequiredNullFactoryConsumer([FromKeyedServices("dep")] Resource value) { public Resource Value { get; } = value; }
    public sealed record LookupKey(string Value);
    public class EquivalentKey(string value) : IEquatable<EquivalentKey>
    {
        public string Value { get; } = value;
        public bool Equals(EquivalentKey other) => other?.Value == Value;
        public override bool Equals(object other) => other is EquivalentKey key && Equals(key);
        public override int GetHashCode() => Value.GetHashCode();
    }
    public sealed class ExactKey(string value) : EquivalentKey(value);
    public sealed class ExactKeyConsumer([ServiceKey] ExactKey key) { public ExactKey Key { get; } = key; }
    public sealed class ExactKeyParent([FromKeyedServices] ExactKeyConsumer value) { public ExactKeyConsumer Value { get; } = value; }
    public sealed class NestedKeyConsumer(KeyConsumer value) { public KeyConsumer Value { get; } = value; }
    public sealed class InheritedKeyParent([FromKeyedServices] NestedKeyConsumer early, [FromKeyedServices] KeyConsumer later)
    {
        public NestedKeyConsumer Early { get; } = early;
        public KeyConsumer Later { get; } = later;
    }
    public sealed class KeyConsumer([ServiceKey] object key) { public object Key { get; } = key; }
    public sealed class GenericKeyConsumer<T>([ServiceKey] object key) { public object Key { get; } = key; }
    public sealed class InterfaceKeyConsumer([ServiceKey] IComparable key) { public IComparable Key { get; } = key; }
    public sealed class NullableKeyConsumer([ServiceKey] int? key) { public int? Key { get; } = key; }
    public sealed class KeyFirstConsumer([ServiceKey, FromKeyedServices("dep")] object value) { public object Value { get; } = value; }
    public sealed class DependencyFirstConsumer([FromKeyedServices("dep"), ServiceKey] object value) { public object Value { get; } = value; }
    public sealed class FactoryFailure : Exception;
}
