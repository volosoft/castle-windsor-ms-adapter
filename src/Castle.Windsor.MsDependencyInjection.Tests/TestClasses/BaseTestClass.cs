using System;
using System.Reflection;

namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class BaseTestClass : IDisposable
    {
        //Property-injected
        public DisposeCounter DisposeCounter { get; set; }

        public bool IsDisposed { get; set; }

        public virtual void Dispose()
        {
            var type = GetType().GetTypeInfo();

            if (type.Namespace.StartsWith("Castle.Proxies"))
            {
                type = type.BaseType.GetTypeInfo();
            }

            if (IsDisposed)
            {
                throw new ObjectDisposedException(type.FullName, "This object is already disposed!");
            }

            IsDisposed = true;

            DisposeCounter.Increment(type.AsType());
        }
    }
}