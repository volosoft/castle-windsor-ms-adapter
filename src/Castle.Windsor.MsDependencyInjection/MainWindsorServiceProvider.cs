using Castle.Core.Internal;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// Implements <see cref="IMainWindsorServiceProvider"/>.
    /// </summary>
    public class MainWindsorServiceProvider : ScopedWindsorServiceProvider, IMainWindsorServiceProvider
    {
        private readonly IWindsorContainer _container;
        private ThreadSafeFlag _disposed;

        public MainWindsorServiceProvider(IWindsorContainer container, IWindsorServiceScope scope)
            : base(container, scope)
        {
            _container = container;
            _disposed = new ThreadSafeFlag();
        }

        public void Dispose()
        {
            if (!_disposed.Signal())
            {
                return;
            }

            _container.Dispose();
        }
    }
}