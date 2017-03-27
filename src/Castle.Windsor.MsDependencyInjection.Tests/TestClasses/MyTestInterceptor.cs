using System;
using Castle.DynamicProxy;

namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class MyTestInterceptor : IInterceptor, IDisposable
    {
        public void Intercept(IInvocation invocation)
        {
            invocation.Proceed();
        }

        public void Dispose()
        {
        }
    }
}