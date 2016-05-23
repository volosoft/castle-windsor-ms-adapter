using System;

namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class BaseTestClass : IDisposable
    {
        //Property-injected
        public DisposeCounter DisposeCounter { get; set; }

        public bool IsDisposed { get; set; }

        public virtual void Dispose()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(GetType().FullName, "This object is already disposed!");
            }

            IsDisposed = true;

            DisposeCounter.Increment(GetType());
        }
    }
}