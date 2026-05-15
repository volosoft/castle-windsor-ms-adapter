using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes
{
    /// <summary>
    /// Two public constructors that BOTH declare a parameter named <c>key</c>, but only the
    /// single-arg one marks it <c>[ServiceKey]</c> and the types differ. The old
    /// "scan every constructor, merge by parameter name" approach cannot tell which
    /// <c>key</c> is the service-key sink; the reworked design keys off the exact
    /// constructor Windsor/MS selected, so it must inject the key only into the selected
    /// constructor's parameter.
    /// </summary>
    public sealed class OverloadedServiceKeyConsumer : IKeyedFake
    {
        public OverloadedServiceKeyConsumer(IKeyedFake collaborator, int key)
        {
            SelectedCtor = "two-arg";
            Collaborator = collaborator;
            BoxedKey = key;
        }

        public OverloadedServiceKeyConsumer([ServiceKey] string key)
        {
            SelectedCtor = "service-key";
            BoxedKey = key;
        }

        public string SelectedCtor { get; }
        public IKeyedFake Collaborator { get; }
        public object BoxedKey { get; }
    }

    /// <summary>
    /// Constructor overloads with a colliding <c>dep</c> parameter name; only one is a
    /// <c>[FromKeyedServices]</c> sink and the parameter types differ.
    /// </summary>
    public sealed class OverloadedFromKeyedConsumer
    {
        public OverloadedFromKeyedConsumer(int dep)
        {
            SelectedCtor = "int";
        }

        public OverloadedFromKeyedConsumer([FromKeyedServices("k")] IKeyedFake dep)
        {
            SelectedCtor = "keyed";
            Dep = dep;
        }

        public string SelectedCtor { get; }
        public IKeyedFake Dep { get; }
    }

    /// <summary>
    /// A NON-keyed component whose constructor takes a keyed dependency. Exercises the
    /// metadata fast-gate for a non-keyed implementation type.
    /// </summary>
    public sealed class NonKeyedConsumerOfKeyedDep
    {
        public NonKeyedConsumerOfKeyedDep([FromKeyedServices("k")] IKeyedFake dep)
        {
            Dep = dep;
        }

        public IKeyedFake Dep { get; }
    }
}
