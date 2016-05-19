using System;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Extends <see cref="IServiceProvider"/> and declares a reference to contained scope.
    /// </summary>
    public interface IScopedWindsorServiceProvider : IServiceProvider
    {
        /// <summary>
        /// Gets the scope related to this service provider.
        /// </summary>
        IWindsorServiceScope Scope { get; }
    }
}