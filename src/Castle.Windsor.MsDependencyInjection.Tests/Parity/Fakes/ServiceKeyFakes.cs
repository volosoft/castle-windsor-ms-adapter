using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes
{
    public enum FakeEnumKey
    {
        Alpha,
        Beta
    }

    public sealed class StringKeyConsumer : IKeyedFake
    {
        public StringKeyConsumer([ServiceKey] string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    public sealed class IntKeyConsumer : IKeyedFake
    {
        public IntKeyConsumer([ServiceKey] int key)
        {
            Key = key;
        }

        public int Key { get; }
    }

    public sealed class EnumKeyConsumer : IKeyedFake
    {
        public EnumKeyConsumer([ServiceKey] FakeEnumKey key)
        {
            Key = key;
        }

        public FakeEnumKey Key { get; }
    }

    /// <summary>
    /// The <c>[ServiceKey]</c> parameter is <see cref="int"/> but the registration uses a string key,
    /// so the key cannot be assigned to the parameter (wrong-type scenario).
    /// </summary>
    public sealed class WrongTypeKeyConsumer : IKeyedFake
    {
        public WrongTypeKeyConsumer([ServiceKey] int key)
        {
            Key = key;
        }

        public int Key { get; }
    }

    /// <summary>
    /// Has both a keyless constructor and a <c>[ServiceKey]</c> one (mirrors the MS spec
    /// <c>Service</c> shape). A keyed resolve injects the key; a non-keyed resolve must fall
    /// back to the keyless constructor instead of failing.
    /// </summary>
    public sealed class OptionalServiceKeyConsumer : IKeyedFake
    {
        public OptionalServiceKeyConsumer()
        {
            Key = null;
            UsedKeylessCtor = true;
        }

        public OptionalServiceKeyConsumer([ServiceKey] string key)
        {
            Key = key;
            UsedKeylessCtor = false;
        }

        public string Key { get; }

        public bool UsedKeylessCtor { get; }
    }
}
