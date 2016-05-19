using Castle.MicroKernel;
using Castle.MicroKernel.Lifestyle.Scoped;
using Microsoft.Extensions.DependencyInjection;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Extends <see cref="IServiceScope"/> to work within Windsor.
    /// </summary>
    public interface IWindsorServiceScope : IServiceScope
    {
        /// <summary>
        /// Gets life time scope of this service scope.
        /// </summary>
        ILifetimeScope LifeTimeScope { get; }

        /// <summary>
        /// Adds a new <see cref="Burden"/> to dispose it if needed
        /// when this <see cref="IWindsorServiceScope"/> instance is disposed.
        /// </summary>
        void Track(Burden burden);
    }
}