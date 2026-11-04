using System;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel;
using Castle.Core;

namespace Castle.Windsor.MsDependencyInjection;

internal sealed class ScopeActivation(MsLifetimeScope scope, Burden burden, Burden owner)
{
    private int _released;
    internal MsLifetimeScope Scope { get; } = scope;
    internal Burden Owner { get; } = owner;
    internal object Instance => burden.Instance;
    internal ComponentModel Model => burden.Model;
    internal bool IsReleased => Volatile.Read(ref _released) != 0;

    internal void Observe() => burden.Released += Released;

    private void Released(Burden value)
    {
        Interlocked.Exchange(ref _released, 1);
        value.Released -= Released;
    }

    internal void Release()
    {
        if (!IsReleased && Owner?.Instance == null) burden.Release();
    }

    internal void ReleaseAfter(ScopeActivation[] owners)
    {
        var remaining = owners.Length;
        foreach (var value in owners)
            value.WhenReleased(() => { if (Interlocked.Decrement(ref remaining) == 0) Release(); });
    }

    private void WhenReleased(Action callback)
    {
        var observed = 0;
        void OnReleased(Burden value)
        {
            if (Interlocked.Exchange(ref observed, 1) != 0) return;
            value.Released -= OnReleased;
            callback();
        }
        burden.Released += OnReleased;
        if (IsReleased) OnReleased(burden);
    }
}

internal sealed class NativeAsyncRelease(IAsyncDisposable target, Action release)
{
    internal void Dispose()
    {
        var context = MsAsyncDisposal.Current;
        if (context?.StopDecommission == true) return;
        try
        {
            if (context?.Asynchronous != true)
                throw new InvalidOperationException($"'{target.GetType()}' only implements IAsyncDisposable. Use DisposeAsync.");
            target.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            if (context == null) throw;
            context.Capture(exception);
            context.StopDecommission = true;
        }
        finally { release(); }
    }
}
