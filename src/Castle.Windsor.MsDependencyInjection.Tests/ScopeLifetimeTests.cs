using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ScopeLifetimeTests
    {
        [Fact]
        public void A_Factory_First_Resolved_In_A_Child_Remains_Usable_From_The_Root()
        {
            using var container = new WindsorContainer();
            var services = new ServiceCollection();
            services.AddScoped<Middle>();
            services.AddSingleton(new Log());
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            using (var first = new WindsorServiceScope(container, container.Resolve<GlobalMsLifetimeScope>()))
            {
                first.ServiceProvider.GetRequiredService<IServiceScopeFactory>();
            }

            using (var second = root.CreateScope())
            {
                Assert.NotNull(second.ServiceProvider.GetRequiredService<Middle>());
            }

            ((IDisposable)root).Dispose();
        }

        [Fact]
        public void Disposed_Child_Rejects_All_Resolution_Entries_And_Cached_Collections()
        {
            var services = new ServiceCollection();
            services.AddTransient<Resource>();
            services.AddKeyedSingleton<Resource>("k");
            using var container = new WindsorContainer();
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            var scope = root.CreateScope();
            var provider = scope.ServiceProvider;
            provider.GetKeyedServices<Resource>("k");
            scope.Dispose();
            Assert.Throws<ObjectDisposedException>(() => provider.GetService<Resource>());
            Assert.Throws<ObjectDisposedException>(() => provider.GetRequiredService<Resource>());
            Assert.Throws<ObjectDisposedException>(() => provider.GetKeyedService<Resource>("k"));
            Assert.Throws<ObjectDisposedException>(() => provider.GetRequiredKeyedService<Resource>("k"));
            Assert.Throws<ObjectDisposedException>(() => provider.GetKeyedServices<Resource>("k"));
            ((IDisposable)root).Dispose();
        }

        [Fact]
        public void Retained_Scope_Factory_Rejects_Closed_Root()
        {
            using var container = new WindsorContainer();
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, new ServiceCollection());
            var factory = root.GetRequiredService<IServiceScopeFactory>();
            ((IDisposable)root).Dispose();
            Assert.Throws<ObjectDisposedException>(() => factory.CreateScope());
        }

        [Fact]
        public void Concurrent_Cached_Collections_Return_One_Published_Array()
        {
            var services = new ServiceCollection();
            services.AddKeyedScoped<Resource>("k");
            using var container = new WindsorContainer();
            var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
            using (var scope = root.CreateScope())
            {
                var arrays = new IEnumerable<Resource>[32];
                Parallel.For(0, arrays.Length, index => arrays[index] = scope.ServiceProvider.GetRequiredKeyedService<IEnumerable<Resource>>("k"));
                Assert.All(arrays, array => Assert.Same(arrays[0], array));
                Assert.Single(arrays[0]);
            }
            ((IDisposable)root).Dispose();
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void Root_Cached_Generics_Dispose_In_Reverse_Completion_Order(bool microsoft, bool scoped)
        {
            var log = new Log();
            var services = new ServiceCollection();
            services.AddSingleton(log);
            if (scoped)
            {
                services.AddScoped(typeof(Generic<>));
                services.AddScoped<Middle>();
            }
            else
            {
                services.AddSingleton(typeof(Generic<>));
                services.AddSingleton<Middle>();
            }
            using var container = new WindsorContainer();
            var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
            root.GetRequiredService<Generic<int>>();
            root.GetRequiredService<Middle>();
            root.GetRequiredService<Generic<string>>();
            ((IDisposable)root).Dispose();
            Assert.Equal(new[] { "String", "middle", "Int32" }, log.Events);
        }

        public sealed class Resource;

        public sealed class Log
        {
            public List<string> Events { get; } = new();
        }

        public sealed class Generic<T> : IDisposable
        {
            private readonly Log _log;

            public Generic(Log log)
            {
                _log = log;
            }

            public void Dispose()
            {
                _log.Events.Add(typeof(T).Name);
            }
        }

        public sealed class Middle : IDisposable
        {
            private readonly Log _log;

            public Middle(Log log)
            {
                _log = log;
            }

            public void Dispose()
            {
                _log.Events.Add("middle");
            }
        }
    }
}
