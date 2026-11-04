using System;
using System.Threading;
using Castle.Core;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class RootLifetime : IDisposable
    {
        private Action _release;

        internal ComponentModel Model { get; set; }

        public RootLifetime(Action release)
        {
            _release = release;
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref _release, null)?.Invoke();
        }
    }
}
