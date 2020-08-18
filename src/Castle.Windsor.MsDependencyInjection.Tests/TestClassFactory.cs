using System.Collections.Concurrent;
using Castle.Windsor.MsDependencyInjection.Tests.TestClasses;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class MyTestClass3Factory
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConcurrentDictionary<string, MyTestClass3> _services = new ConcurrentDictionary<string, MyTestClass3>();

        public MyTestClass3Factory(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        public MyTestClass3 Create(string name)
        {
            return _services.GetOrAdd(name, s =>
            {
                var scope = _scopeFactory.CreateScope(); // The scope will not dispose, by design.
                return scope.ServiceProvider.GetRequiredService<MyTestClass3>();
            });
        }
    }
}
