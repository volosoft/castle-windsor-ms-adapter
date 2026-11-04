using System;
using System.Threading;
using System.Threading.Tasks;
using Castle.MicroKernel;
using Castle.Core;

namespace Castle.Windsor.MsDependencyInjection
{
    internal sealed class ScopeActivation
    {
        private readonly Burden _burden;
        private int _released;

        internal MsLifetimeScope Scope { get; }

        internal Burden Owner { get; }

        internal object Instance
        {
            get
            {
                return _burden.Instance;
            }
        }

        internal ComponentModel Model
        {
            get
            {
                return _burden.Model;
            }
        }

        internal bool IsReleased
        {
            get
            {
                return Volatile.Read(ref _released) != 0;
            }
        }

        public ScopeActivation(MsLifetimeScope scope, Burden burden, Burden owner)
        {
            _burden = burden;
            Scope = scope;
            Owner = owner;
        }

        internal void Observe()
        {
            _burden.Released += Released;
        }

        private void Released(Burden value)
        {
            Interlocked.Exchange(ref _released, 1);
            value.Released -= Released;
        }

        internal void Release()
        {
            if (!IsReleased && Owner?.Instance == null)
            {
                _burden.Release();
            }
        }

        internal void ReleaseAfter(ScopeActivation[] owners)
        {
            var remaining = owners.Length;
            foreach (var value in owners)
            {
                value.WhenReleased(() =>
                {
                    if (Interlocked.Decrement(ref remaining) == 0)
                    {
                        Release();
                    }
                });
            }
        }

        private void WhenReleased(Action callback)
        {
            var observed = 0;
            void OnReleased(Burden value)
            {
                if (Interlocked.Exchange(ref observed, 1) != 0)
                {
                    return;
                }
                value.Released -= OnReleased;
                callback();
            }
            _burden.Released += OnReleased;
            if (IsReleased)
            {
                OnReleased(_burden);
            }
        }
    }

    internal sealed class NativeAsyncRelease
    {
        private readonly IAsyncDisposable _target;
        private readonly Action _release;

        public NativeAsyncRelease(IAsyncDisposable target, Action release)
        {
            _target = target;
            _release = release;
        }

        internal void Dispose()
        {
            var context = MsAsyncDisposal.Current;
            if (context?.StopDecommission == true)
            {
                return;
            }
            try
            {
                if (context?.Asynchronous != true)
                {
                    throw new InvalidOperationException($"'{_target.GetType()}' only implements IAsyncDisposable. Use DisposeAsync.");
                }

                _target.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                if (context == null)
                {
                    throw;
                }
                context.Capture(exception);
                context.StopDecommission = true;
            }
            finally
            {
                _release();
            }
        }
    }
}
