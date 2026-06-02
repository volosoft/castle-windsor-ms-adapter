using System;
using System.Collections.Generic;
using System.Linq;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure
{
    /// <summary>
    /// Thread-safe, ordered record of dispose events. Registered as a singleton *instance* on both
    /// backends (<c>services.AddSingleton(tracker)</c>) so it is injected by constructor identically
    /// on MS DI and Windsor, is never owned/disposed by either container, and survives provider
    /// disposal so assertions can inspect it afterwards.
    /// </summary>
    public sealed class DisposeTracker
    {
        private readonly object _sync = new object();
        private readonly List<string> _order = new List<string>();

        public void Record(object instance, string phase = null)
        {
            var name = instance.GetType().Name + (phase == null ? string.Empty : ":" + phase);
            lock (_sync)
            {
                _order.Add(name);
            }
        }

        /// <summary>Dispose events in the exact order they happened. Names are simple type names,
        /// optionally suffixed <c>:phase</c> (e.g. <c>KeyedFakeAsync:async</c>).</summary>
        public IReadOnlyList<string> Order
        {
            get
            {
                lock (_sync)
                {
                    return _order.ToArray();
                }
            }
        }

        public int Count(Type type)
        {
            lock (_sync)
            {
                return _order.Count(n => n == type.Name || n.StartsWith(type.Name + ":", StringComparison.Ordinal));
            }
        }

        public int Count<T>() => Count(typeof(T));

        public bool Any<T>() => Count<T>() > 0;
    }
}
