using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class ParentContainerTests
    {
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void Parent_Keyed_Services_Are_Inherited_Without_Leaking_Child_Registrations(bool attachFirst)
        {
            using var parent = new WindsorContainer();
            using var child = new WindsorContainer();
            var parentServices = new ServiceCollection();
            parentServices.AddKeyedSingleton("k", new Resource("parent"));
            parentServices.AddSingleton(typeof(int), 1);
            var parentProvider = WindsorRegistrationHelper.CreateServiceProvider(parent, parentServices);
            if (attachFirst)
            {
                parent.AddChildContainer(child);
            }

            var childServices = new ServiceCollection();
            childServices.AddKeyedSingleton("k", new Resource("child"));
            childServices.AddSingleton(typeof(int), 2);
            var childProvider = WindsorRegistrationHelper.CreateServiceProvider(child, childServices);
            if (!attachFirst)
            {
                parent.AddChildContainer(child);
            }

            Assert.Equal("child", childProvider.GetRequiredKeyedService<Resource>("k").Name);
            Assert.Equal(new[] { 1, 2 }, childProvider.GetServices<int>());
            Assert.Equal(new[] { "parent", "child" }, childProvider.GetKeyedServices<Resource>("k").Select(item => item.Name));
            Assert.Equal("parent", Assert.Single(parentProvider.GetKeyedServices<Resource>("k")).Name);
            parent.AddServices(new ServiceCollection().AddKeyedSingleton("late", new Resource("late")));
            Assert.Equal("late", childProvider.GetRequiredKeyedService<Resource>("late").Name);
            parent.RemoveChildContainer(child);
            Assert.Null(childProvider.GetKeyedService<Resource>("late"));
            Assert.Equal("child", Assert.Single(childProvider.GetKeyedServices<Resource>("k")).Name);
            ((IDisposable)childProvider).Dispose();
            ((IDisposable)parentProvider).Dispose();
        }

        public sealed record Resource(string Name);
    }
}
