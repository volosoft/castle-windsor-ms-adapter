using System;
using System.Threading;
using Castle.Core;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class RootLifetime(Action release) : IDisposable
{
    private Action _release = release;

    internal ComponentModel Model { get; set; }

    public void Dispose() => Interlocked.Exchange(ref _release, null)?.Invoke();
}
