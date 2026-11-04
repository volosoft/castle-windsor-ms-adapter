using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Castle.Windsor.MsDependencyInjection.Tests;

public class CollectionActivationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Failed_Constructor_Collection_Releases_Completed_Items_With_The_Scope(bool keyed)
    {
        var created = new List<DisposableItem>();
        var services = new ServiceCollection();
        DisposableItem Create()
        {
            var item = new DisposableItem();
            created.Add(item);
            return item;
        }
        if (keyed)
        {
            services.AddKeyedTransient<IItem>("k", (_, _) => Create());
            services.AddKeyedTransient<IItem>("k", (_, _) => throw new InvalidOperationException("Activation failed."));
            services.AddTransient<KeyedOwner>();
        }
        else
        {
            services.AddTransient<IItem>(_ => Create());
            services.AddTransient<IItem>(_ => throw new InvalidOperationException("Activation failed."));
            services.AddTransient<Owner>();
        }
        using var container = new WindsorContainer();
        var root = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        using (var scope = root.CreateScope())
        {
            Assert.ThrowsAny<Exception>(() => scope.ServiceProvider.GetRequiredService(keyed ? typeof(KeyedOwner) : typeof(Owner)));
            Assert.Equal(0, Assert.Single(created).Disposals);
        }
        Assert.Equal(1, Assert.Single(created).Disposals);
        ((IDisposable)root).Dispose();
        Assert.Equal(1, created[0].Disposals);
    }

    [Fact]
    public void Constructor_Collections_Activate_Items_In_Registration_Order()
    {
        var calls = new List<string>();
        var services = new ServiceCollection();
        services.AddTransient<IItem>(_ => { calls.Add("first"); return new Item("first"); });
        services.AddTransient<IItem>(_ => { calls.Add("second"); return new Item("second"); });
        services.AddTransient<Owner>();
        using var container = new WindsorContainer();
        var provider = WindsorRegistrationHelper.CreateServiceProvider(container, services);
        var owner = provider.GetRequiredService<Owner>();
        Assert.Equal(new[] { "first", "second" }, owner.Items.Select(item => item.Name));
        Assert.Equal(new[] { "first", "second" }, calls);
    }

    public interface IItem { string Name { get; } }
    public sealed class Item(string name) : IItem { public string Name { get; } = name; }
    public sealed class Owner(IEnumerable<IItem> items) { public IEnumerable<IItem> Items { get; } = items; }
    public sealed class KeyedOwner([FromKeyedServices("k")] IEnumerable<IItem> items) { public IEnumerable<IItem> Items { get; } = items; }
    public sealed class DisposableItem : IItem, IDisposable
    {
        public string Name => "item";
        public int Disposals { get; private set; }
        public void Dispose() => Disposals++;
    }
}
