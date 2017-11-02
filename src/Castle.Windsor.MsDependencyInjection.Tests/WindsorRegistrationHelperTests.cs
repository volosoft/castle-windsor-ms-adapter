using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests
{
    public class WindsorRegistrationHelperTests
    {
        [Fact]
        public void WindsorServiceProvider_ResolvingCollection_IfRegisteredInOneBatch_ReturnsServicesInProperOrder()
        {
            var serviceProvider = CreateWindsorServiceProviderInOneBatch();

            var result = serviceProvider.GetService<IEnumerable<ITestInterface>>().Select(x => x.GetType()).ToList();

            result[0].ShouldBe(typeof(TestClass1), "item 1 is wrong");
            result[1].ShouldBe(typeof(TestClass2), "item 2 is wrong");
            result[2].ShouldBe(typeof(TestClass3), "item 3 is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingCollection_IfAddedInMultipleBatches_ReturnsServicesInProperOrder()
        {
            var serviceProvider = CreateWindsorServiceProviderInMultipleBatches();

            var result = serviceProvider.GetService<IEnumerable<ITestInterface>>().Select(x => x.GetType()).ToList();

            result[0].ShouldBe(typeof(TestClass1), "item 1 is wrong");
            result[1].ShouldBe(typeof(TestClass2), "item 2 is wrong");
            result[2].ShouldBe(typeof(TestClass3), "item 3 is wrong");
            result[3].ShouldBe(typeof(TestClass4), "item 4 is wrong");
            result[4].ShouldBe(typeof(TestClass5), "item 5 is wrong");
            result[5].ShouldBe(typeof(TestClass6), "item 6 is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingOneItem_IfRegisteredInOneBatch_ReturnsLastRegisteredItem()
        {
            var serviceProvider = CreateWindsorServiceProviderInOneBatch();

            var result = serviceProvider.GetService<ITestInterface>().GetType();

            result.ShouldBe(typeof(TestClass3), "item is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingOneItem_IfAddedInMultipleBatches_ReturnsLastRegisteredItem()
        {
            var serviceProvider = CreateWindsorServiceProviderInMultipleBatches();

            var result = serviceProvider.GetService<ITestInterface>().GetType();

            result.ShouldBe(typeof(TestClass6), "item is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingClassDependingOnCollection_IfRegisteredInOneBatch_InjectsServicesInProperOrder()
        {
            var serviceProvider = CreateWindsorServiceProviderInOneBatch();

            var result = serviceProvider.GetService<TestClassWithCollectionDependency>();

            result.DependenciesTypes[0].ShouldBe(typeof(TestClass1), "item 1 is wrong");
            result.DependenciesTypes[1].ShouldBe(typeof(TestClass2), "item 2 is wrong");
            result.DependenciesTypes[2].ShouldBe(typeof(TestClass3), "item 3 is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingClassDependingOnCollection_IfAddedInMultipleBatches_InjectsServicesInProperOrder()
        {
            var serviceProvider = CreateWindsorServiceProviderInMultipleBatches();

            var result = serviceProvider.GetService<TestClassWithCollectionDependency>();

            result.DependenciesTypes[0].ShouldBe(typeof(TestClass1), "item 1 is wrong");
            result.DependenciesTypes[1].ShouldBe(typeof(TestClass2), "item 2 is wrong");
            result.DependenciesTypes[2].ShouldBe(typeof(TestClass3), "item 3 is wrong");
            result.DependenciesTypes[3].ShouldBe(typeof(TestClass4), "item 4 is wrong");
            result.DependenciesTypes[4].ShouldBe(typeof(TestClass5), "item 5 is wrong");
            result.DependenciesTypes[5].ShouldBe(typeof(TestClass6), "item 6 is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingClassDependingOnOneItem_IfRegisteredInOneBatch_InjectsLastRegisteredItem()
        {
            var serviceProvider = CreateWindsorServiceProviderInOneBatch();

            var result = serviceProvider.GetService<TestClassWithSingleDependency>();

            result.DependencyType.ShouldBe(typeof(TestClass3), "item is wrong");
        }

        [Fact]
        public void WindsorServiceProvider_ResolvingClassDependingOnOneItem_IfAddedInMultipleBatches_InjectsLastRegisteredItem()
        {
            var serviceProvider = CreateWindsorServiceProviderInMultipleBatches();

            var result = serviceProvider.GetService<TestClassWithSingleDependency>();

            result.DependencyType.ShouldBe(typeof(TestClass6), "item is wrong");
        }

        private IServiceProvider CreateWindsorServiceProviderInOneBatch()
        {
            var windsorContainer = new WindsorContainer();
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<TestClassWithSingleDependency>();
            serviceCollection.AddSingleton<TestClassWithCollectionDependency>();
            serviceCollection.AddSingleton<ITestInterface, TestClass1>();
            serviceCollection.AddSingleton<ITestInterface, TestClass2>();
            serviceCollection.AddSingleton<ITestInterface, TestClass3>();
            return WindsorRegistrationHelper.CreateServiceProvider(windsorContainer, serviceCollection);
        }

        private IServiceProvider CreateWindsorServiceProviderInMultipleBatches()
        {
            var windsorContainer = new WindsorContainer();
            // batch 1
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<TestClassWithSingleDependency>();
            serviceCollection.AddSingleton<TestClassWithCollectionDependency>();
            serviceCollection.AddSingleton<ITestInterface, TestClass1>();
            serviceCollection.AddSingleton<ITestInterface, TestClass2>();
            var serviceProvider = WindsorRegistrationHelper.CreateServiceProvider(windsorContainer, serviceCollection);
            // batch 2
            var serviceCollection2 = new ServiceCollection();
            serviceCollection2.AddSingleton<ITestInterface, TestClass3>();
            serviceCollection2.AddSingleton<ITestInterface, TestClass4>();
            windsorContainer.AddServices(serviceCollection2);
            // batch 3
            var serviceCollection3 = new ServiceCollection();
            serviceCollection3.AddSingleton<ITestInterface, TestClass5>();
            serviceCollection3.AddSingleton<ITestInterface, TestClass6>();
            windsorContainer.AddServices(serviceCollection3);

            return serviceProvider;
        }

        // Windsor Service Provider should behave the same as default implementation
        #region Comparison tests of default .NET Implementation

        [Fact]
        public void DotNet_ResolvingCollection_ReturnsServicesInProperOrder()
        {
            var serviceProvider = CreateDotNetServiceProvider();

            var result = serviceProvider.GetService<IEnumerable<ITestInterface>>().Select(x => x.GetType()).ToList();

            result[0].ShouldBe(typeof(TestClass1), "item 1 is wrong");
            result[1].ShouldBe(typeof(TestClass2), "item 2 is wrong");
            result[2].ShouldBe(typeof(TestClass3), "item 3 is wrong");
        }

        [Fact]
        public void DotNet_ResolvingClassDependingOnCollection_InjectsServicesInProperOrder()
        {
            var serviceProvider = CreateDotNetServiceProvider();

            var result = serviceProvider.GetService<TestClassWithCollectionDependency>();

            result.DependenciesTypes[0].ShouldBe(typeof(TestClass1), "item 1 is wrong");
            result.DependenciesTypes[1].ShouldBe(typeof(TestClass2), "item 2 is wrong");
            result.DependenciesTypes[2].ShouldBe(typeof(TestClass3), "item 3 is wrong");
        }

        [Fact]
        public void DotNet_ResolvingOneItem_ReturnsLastRegisteredItem()
        {
            var serviceProvider = CreateDotNetServiceProvider();

            var result = serviceProvider.GetService<ITestInterface>().GetType();

            result.ShouldBe(typeof(TestClass3), "item is wrong");
        }

        [Fact]
        public void DotNet_ResolvingClassDependingOnOneItem_InjectsLastRegisteredItem()
        {
            var serviceProvider = CreateDotNetServiceProvider();

            var result = serviceProvider.GetService<TestClassWithSingleDependency>();

            result.DependencyType.ShouldBe(typeof(TestClass3), "item is wrong");
        }

        private IServiceProvider CreateDotNetServiceProvider()
        {
            var serviceCollection = new ServiceCollection();
            serviceCollection.AddSingleton<TestClassWithSingleDependency>();
            serviceCollection.AddSingleton<TestClassWithCollectionDependency>();
            serviceCollection.AddSingleton<ITestInterface, TestClass1>();
            serviceCollection.AddSingleton<ITestInterface, TestClass2>();
            serviceCollection.AddSingleton<ITestInterface, TestClass3>();
            return serviceCollection.BuildServiceProvider();
        }

        #endregion Comparison tests of default .NET Implementation

        private interface ITestInterface
        {
        }

        private sealed class TestClass1 : ITestInterface
        {
        }
        private sealed class TestClass2 : ITestInterface
        {
        }
        private sealed class TestClass3 : ITestInterface
        {
        }
        private sealed class TestClass4 : ITestInterface
        {
        }
        private sealed class TestClass5 : ITestInterface
        {
        }
        private sealed class TestClass6 : ITestInterface
        {
        }

        private sealed class TestClassWithSingleDependency
        {
            public TestClassWithSingleDependency(ITestInterface dependency)
            {
                DependencyType = dependency.GetType();
            }

            public Type DependencyType { get; }
        }

        private sealed class TestClassWithCollectionDependency
        {
            public TestClassWithCollectionDependency(IEnumerable<ITestInterface> dependencies)
            {
                DependenciesTypes = dependencies.Select(x => x.GetType()).ToList();
            }

            public IList<Type> DependenciesTypes { get; }
        }
    }
}
