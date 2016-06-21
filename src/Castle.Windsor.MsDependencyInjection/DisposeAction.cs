using System;

namespace Castle.Windsor.MsDependencyInjection
{
    /// <summary>
    /// This class can be used to provide an action when
    /// Dispose method is called.
    /// </summary>
    internal class DisposeAction : IDisposable
    {
        private readonly Action _action;

        /// <summary>
        /// Creates a new <see cref="DisposeAction"/> object.
        /// </summary>
        /// <param name="action">Action to be executed when this object is disposed.</param>
        public DisposeAction(Action action)
        {
            if (action == null)
            {
                throw new ArgumentNullException(nameof(action));
            }

            _action = action;
        }

        public void Dispose()
        {
            _action();
        }
    }
}