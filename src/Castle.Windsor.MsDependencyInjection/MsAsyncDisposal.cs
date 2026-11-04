using System;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using Castle.Core;
using Castle.MicroKernel;
using Castle.MicroKernel.LifecycleConcerns;

namespace Castle.Windsor.MsDependencyInjection;

internal static class MsAsyncDisposal
{
    internal sealed class Context(bool asynchronous)
    {
        internal readonly bool Asynchronous = asynchronous;
        internal ExceptionDispatchInfo Failure;
        internal bool StopDecommission;
        internal void Capture(Exception exception) => Failure ??= ExceptionDispatchInfo.Capture(exception);
    }

    [ThreadStatic]
    internal static Context Current;

    internal static void ReleaseLate(object instance, Action release)
    {
        var target = Castle.DynamicProxy.ProxyUtil.GetUnproxiedInstance(instance);
        if (target is IAsyncDisposable && target is not IDisposable) Run(release).AsTask().GetAwaiter().GetResult();
        else RunInCurrentContext(release);
    }

    internal static void RunSynchronous(Action dispose)
    {
        var previous = Current;
        var context = new Context(false);
        Current = context;
        try { dispose(); }
        catch (Exception exception) { context.Capture(exception); }
        finally { Current = previous; }
        context.Failure?.Throw();
    }

    internal static void RunInCurrentContext(Action dispose)
    {
        if (Current == null) RunSynchronous(dispose);
        else dispose();
    }

    internal static void Complete(Action dispose, Action cleanup)
    {
        ExceptionDispatchInfo failure = null;
        try { dispose(); }
        catch (Exception exception)
        {
            failure = ExceptionDispatchInfo.Capture(exception);
            Current?.Capture(exception);
        }
        try { cleanup(); }
        catch (Exception exception)
        {
            failure ??= ExceptionDispatchInfo.Capture(exception);
            Current?.Capture(exception);
        }
        failure?.Throw();
    }

    internal static ValueTask Run(Action dispose, bool requiresThread = true)
    {
        if (!requiresThread)
        {
            var previous = Current;
            var context = new Context(true);
            Current = context;
            try { dispose(); }
            catch (Exception exception) { context.Capture(exception); }
            finally { Current = previous; }
            return context.Failure == null ? default : ValueTask.FromException(context.Failure.SourceException);
        }
        return new ValueTask(Task.Factory.StartNew(() =>
        {
            var previous = Current;
            var context = new Context(true);
            Current = context;
            try { dispose(); }
            catch (Exception exception) { context.Capture(exception); }
            finally { Current = previous; }
            context.Failure?.Throw();
        }, System.Threading.CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default));
    }
}

internal sealed class MsDecommissionConcern(IDecommissionConcern[] concerns) : LateBoundDecommissionConcerns
{
    public override void Apply(ComponentModel model, object component)
    {
        if (component is MsLifetimeScope or ScopedWindsorServiceProvider)
        {
            foreach (var concern in concerns) concern.Apply(model, component);
            ApplyAdditionalConcerns(model, component);
            return;
        }
        if (MsAsyncDisposal.Current is { Asynchronous: true } asynchronous)
        {
            if (asynchronous.StopDecommission) return;
            // Windsor's synchronous release pipeline must keep dependencies alive until the owner finishes.
            try { ApplyAsync(model, component).AsTask().GetAwaiter().GetResult(); }
            catch (Exception exception)
            {
                asynchronous.Capture(exception);
                asynchronous.StopDecommission = true;
            }
            return;
        }
        if (MsAsyncDisposal.Current is { } synchronous)
        {
            if (synchronous.StopDecommission) return;
            try { ApplySynchronous(model, component); }
            catch (Exception exception)
            {
                // Let Windsor detach the burden before reporting the user's error.
                synchronous.Capture(exception);
                synchronous.StopDecommission = true;
            }
            return;
        }
        ApplySynchronous(model, component);
    }

    private void ApplySynchronous(ComponentModel model, object component)
    {
        if (component is IAsyncDisposable && component is not IDisposable)
            throw new InvalidOperationException($"'{component.GetType()}' only implements IAsyncDisposable. Use DisposeAsync to dispose the scope or provider.");
        foreach (var concern in concerns) concern.Apply(model, component);
        ApplyAdditionalConcerns(model, component);
    }

    private async ValueTask ApplyAsync(ComponentModel model, object component)
    {
        if (component is IAsyncDisposable asyncOnly && component is not IDisposable &&
            !concerns.Any(concern => concern is IMsAsyncConcern or MsDecommissionConcern))
            await asyncOnly.DisposeAsync().ConfigureAwait(false);
        foreach (var concern in concerns)
        {
            if (concern is MsDecommissionConcern nested)
                await nested.ApplyAsync(model, component).ConfigureAwait(false);
            else if (concern is IMsAsyncConcern asynchronous)
            {
                await asynchronous.ApplyAsync(model, component).ConfigureAwait(false);
            }
            else concern.Apply(model, component);
        }
        ApplyAdditionalConcerns(model, component);
    }

    private void ApplyAdditionalConcerns(ComponentModel model, object component)
    {
        if (HasConcerns) base.Apply(model, component);
    }
}

internal interface IMsAsyncConcern
{
    ValueTask ApplyAsync(ComponentModel model, object component);
}

internal sealed class MsDisposalConcern : IDecommissionConcern, IMsAsyncConcern
{
    internal static readonly MsDisposalConcern Instance = new();
    public void Apply(ComponentModel model, object component)
    {
        if (component is IDisposable disposable) disposable.Dispose();
        else if (component is IAsyncDisposable)
            throw new InvalidOperationException($"'{component.GetType()}' only implements IAsyncDisposable. Use DisposeAsync.");
    }
    public ValueTask ApplyAsync(ComponentModel model, object component)
    {
        if (component is IAsyncDisposable asynchronous) return asynchronous.DisposeAsync();
        Apply(model, component);
        return default;
    }
}

internal sealed class MsLateBoundDisposal : LateBoundDecommissionConcerns, IMsAsyncConcern
{
    public override void Apply(ComponentModel model, object component)
    {
        MsDisposalConcern.Instance.Apply(model, component);
        if (HasConcerns) base.Apply(model, component);
    }
    public async ValueTask ApplyAsync(ComponentModel model, object component)
    {
        await MsDisposalConcern.Instance.ApplyAsync(model, component).ConfigureAwait(false);
        if (HasConcerns) base.Apply(model, component);
    }
}
