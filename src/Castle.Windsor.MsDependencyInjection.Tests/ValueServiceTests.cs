using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class ValueServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Value_Instances_And_Factories_Are_Resolved_And_Injected(bool microsoft)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(int), 42);
        services.AddTransient(typeof(int), _ => 43);
        services.AddSingleton(typeof(string), "value");
        services.AddKeyedSingleton(typeof(int), "k", 44);
        services.AddTransient<Owner>();
        using var container = new WindsorContainer();
        var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        Assert.Equal(43, root.GetRequiredService<int>());
        Assert.Equal(new[] { 42, 43 }, root.GetServices<int>());
        Assert.Equal(44, root.GetRequiredKeyedService<int>("k"));
        Assert.Null(root.GetService<object>());
        var owner = root.GetRequiredService<Owner>();
        Assert.Equal(43, owner.Value);
        Assert.Equal(44, owner.KeyedValue);
        Assert.Equal("value", owner.Text);
        Assert.Equal(new[] { 42, 43 }, owner.Items);
        ((IDisposable)root).Dispose();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Struct_Implementations_Are_Activated_By_The_Container(bool microsoft)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(int), 42);
        services.AddTransient(typeof(Stamp), typeof(Stamp));
        services.AddKeyedTransient(typeof(Stamp), "k", typeof(Stamp));
        using var container = new WindsorContainer();
        var root = microsoft ? services.BuildServiceProvider() : WindsorRegistrationHelper.CreateServiceProvider(container, services);
        Assert.Equal(42, root.GetRequiredService<Stamp>().Value);
        Assert.Equal(42, root.GetRequiredKeyedService<Stamp>("k").Value);
        ((IDisposable)root).Dispose();
    }

    public struct Stamp(int value) { public int Value { get; } = value; }
    public sealed class Owner(int value, [FromKeyedServices("k")] int keyedValue, string text, IEnumerable<int> items)
    {
        public int Value { get; } = value;
        public int KeyedValue { get; } = keyedValue;
        public string Text { get; } = text;
        public IEnumerable<int> Items { get; } = items;
    }
}
