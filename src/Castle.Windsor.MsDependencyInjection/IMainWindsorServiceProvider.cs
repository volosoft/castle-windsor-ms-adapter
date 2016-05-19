using System;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Main service provider.
    /// If it's disposed, the container is disposed.
    /// </summary>
    public interface IMainWindsorServiceProvider : IScopedWindsorServiceProvider, IDisposable
    {
        
    }
}