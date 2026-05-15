using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes
{
    public sealed class FromKeyedCtorConsumer
    {
        public FromKeyedCtorConsumer([FromKeyedServices("k")] IKeyedFake dep)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    public sealed record FromKeyedRecord([FromKeyedServices("k")] IKeyedFake Dep);

    public sealed class FromKeyedEnumerableConsumer
    {
        public FromKeyedEnumerableConsumer([FromKeyedServices("k")] IEnumerable<IKeyedFake> deps)
        {
            Deps = deps.ToList();
        }

        public IReadOnlyList<IKeyedFake> Deps { get; }
    }

    public sealed class FromKeyedNullConsumer
    {
        public FromKeyedNullConsumer([FromKeyedServices(null)] IKeyedFake dep)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    public sealed class FromKeyedMissingConsumer
    {
        public FromKeyedMissingConsumer([FromKeyedServices("missing")] IKeyedFake dep)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    /// <summary>Explicit-key dependency with a constructor default - used when the key is missing.</summary>
    public sealed class FromKeyedDefaultConsumer
    {
        public FromKeyedDefaultConsumer([FromKeyedServices("k")] IKeyedFake dep = null)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    /// <summary>Mirrors the MS spec OtherServiceWithDefaultCtorArgs (two defaulted keyed params).</summary>
    public sealed class FromKeyedTwoDefaultsConsumer
    {
        public FromKeyedTwoDefaultsConsumer(
            [FromKeyedServices("a")] IKeyedFake a = null,
            [FromKeyedServices("b")] IKeyedFake b = null)
        {
            A = a;
            B = b;
        }

        public IKeyedFake A { get; }
        public IKeyedFake B { get; }
    }

    /// <summary><c>[FromKeyedServices(null)]</c> (non-keyed) dependency with a constructor default.</summary>
    public sealed class FromKeyedNullDefaultConsumer
    {
        public FromKeyedNullDefaultConsumer([FromKeyedServices(null)] IKeyedFake dep = null)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    /// <summary>Parameterless <c>[FromKeyedServices]</c> (inherit-key) dependency.</summary>
    public sealed class FromKeyedInheritConsumer
    {
        public FromKeyedInheritConsumer([FromKeyedServices] IKeyedFake dep)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    /// <summary>Parameterless <c>[FromKeyedServices]</c> (inherit-key) dependency with a default.</summary>
    public sealed class FromKeyedInheritDefaultConsumer
    {
        public FromKeyedInheritDefaultConsumer([FromKeyedServices] IKeyedFake dep = null)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }

    public sealed class MixedArgConsumer
    {
        public MixedArgConsumer(string label, [FromKeyedServices("k")] IKeyedFake dep)
        {
            Label = label;
            Dep = dep;
        }

        public string Label { get; }
        public IKeyedFake Dep { get; }
    }

    public sealed class TwoKeyConsumer
    {
        public TwoKeyConsumer([FromKeyedServices("a")] IKeyedFake a, [FromKeyedServices("b")] IKeyedFake b)
        {
            A = a;
            B = b;
        }

        public IKeyedFake A { get; }
        public IKeyedFake B { get; }
    }

    public interface IInheritChild
    {
        object CapturedKey { get; }
    }

    public sealed class InheritChild : IInheritChild
    {
        public InheritChild([ServiceKey] object key)
        {
            CapturedKey = key;
        }

        public object CapturedKey { get; }
    }

    public interface IInheritParent
    {
        IInheritChild Child { get; }
    }

    /// <summary>
    /// The child uses the parameterless <c>[FromKeyedServices]</c> (LookupMode.InheritKey), so it must
    /// be resolved with the key the parent itself was resolved with.
    /// </summary>
    public sealed class InheritKeyParent : IInheritParent
    {
        public InheritKeyParent([FromKeyedServices] IInheritChild child)
        {
            Child = child;
        }

        public IInheritChild Child { get; }
    }
}
