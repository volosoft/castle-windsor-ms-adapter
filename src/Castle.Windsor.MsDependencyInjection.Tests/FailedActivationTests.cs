using System;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class FailedActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Constructor_Body_Failure_Keeps_Dependencies_Until_Scope_End(bool microsoft)
    {
        Resource resource = null;
        var services = new ServiceCollection();
        services.AddTransient(_ => resource = new Resource());
        services.AddTransient<FailingOwner>();
        using var container = new WindsorContainer();
        var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        using (var scope = root.CreateScope())
        {
            Assert.ThrowsAny<Exception>(() => scope.ServiceProvider.GetRequiredService<FailingOwner>());
            Assert.NotNull(resource);
            Assert.Equal(0, resource.Disposals);
        }
        Assert.Equal(1, resource.Disposals);
        ((IDisposable)root).Dispose();
        Assert.Equal(1, resource.Disposals);
    }

    public sealed class Resource : IDisposable
    {
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
    public sealed class FailingOwner
    {
        public FailingOwner(Resource resource) => throw new InvalidOperationException("Construction failed.");
    }
}
