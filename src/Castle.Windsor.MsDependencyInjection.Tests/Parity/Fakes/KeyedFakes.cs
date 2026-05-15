using System;
using System.Threading.Tasks;
using Castle.Windsor.MsDependencyInjection.Tests.Parity.Infrastructure;

namespace Castle.Windsor.MsDependencyInjection.Tests.Parity.Fakes
{
    public interface IKeyedFake
    {
    }

    /// <summary>
    /// Base for disposable keyed fakes. The <see cref="DisposeTracker"/> is supplied by constructor
    /// injection (works identically on MS DI and Windsor — unlike property injection) so disposal is
    /// observable on both backends.
    /// </summary>
    public abstract class TrackedFake : IKeyedFake, IDisposable
    {
        private readonly DisposeTracker _tracker;
        private bool _disposed;

        protected TrackedFake(DisposeTracker tracker)
        {
            _tracker = tracker;
        }

        public bool IsDisposed => _disposed;

        public void Dispose()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(GetType().FullName, "This object is already disposed!");
            }

            _disposed = true;
            _tracker?.Record(this);
        }
    }

    public sealed class KeyedFakeA : TrackedFake
    {
        public KeyedFakeA(DisposeTracker tracker) : base(tracker) { }
    }

    public sealed class KeyedFakeB : TrackedFake
    {
        public KeyedFakeB(DisposeTracker tracker) : base(tracker) { }
    }

    public sealed class KeyedFakeC : TrackedFake
    {
        public KeyedFakeC(DisposeTracker tracker) : base(tracker) { }
    }

    /// <summary>Captures the actual key handed to a factory (used for factory-AnyKey scenarios).</summary>
    public sealed class KeyedFakeKeyCapture : IKeyedFake
    {
        public KeyedFakeKeyCapture(object key)
        {
            CapturedKey = key;
        }

        public object CapturedKey { get; }
    }

    /// <summary>An externally-supplied instance: a container must NOT dispose it.</summary>
    public sealed class PreBuiltFake : IKeyedFake, IDisposable
    {
        public bool Disposed { get; private set; }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    /// <summary>Implements both dispose contracts so we can observe which one a container prefers.</summary>
    public sealed class KeyedFakeAsync : IKeyedFake, IAsyncDisposable, IDisposable
    {
        private readonly DisposeTracker _tracker;

        public KeyedFakeAsync(DisposeTracker tracker)
        {
            _tracker = tracker;
        }

        public bool DisposedSync { get; private set; }
        public bool DisposedAsync { get; private set; }

        public void Dispose()
        {
            DisposedSync = true;
            _tracker?.Record(this, "sync");
        }

        public ValueTask DisposeAsync()
        {
            DisposedAsync = true;
            _tracker?.Record(this, "async");
            return default;
        }
    }
}
