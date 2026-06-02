using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes
{
    public interface IRepo<T>
    {
    }

    public sealed class Repo<T> : IRepo<T>
    {
    }

    public sealed class OtherRepo<T> : IRepo<T>
    {
    }

    public sealed class KeyAwareRepo<T> : IRepo<T>
    {
        public KeyAwareRepo([ServiceKey] string key)
        {
            Key = key;
        }

        public string Key { get; }
    }

    /// <summary>A closed-specific implementation used to test "closed reg overrides open-generic".</summary>
    public sealed class SpecialIntRepo : IRepo<int>
    {
    }

    /// <summary>Open-generic implementation constrained to reference-type arguments.</summary>
    public sealed class ClassConstrainedRepo<T> : IRepo<T> where T : class
    {
    }
}
