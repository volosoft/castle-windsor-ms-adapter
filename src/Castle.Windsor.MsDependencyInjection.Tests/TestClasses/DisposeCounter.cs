using System;
using System.Collections.Generic;

namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class DisposeCounter
    {
        public int this[Type type]
        {
            get
            {
                if (!_types.ContainsKey(type))
                {
                    _types[type] = 0;
                }

                return _types[type];
            }
        }

        private readonly Dictionary<Type, int> _types;

        public DisposeCounter()
        {
            _types = new Dictionary<Type, int>();
        }

        public int Get<T>()
        {
            return this[typeof(T)];
        }

        public void Increment(Type type, int count = 1)
        {
            if (!_types.ContainsKey(type))
            {
                _types[type] = 0;
            }

            _types[type] = _types[type] + count;
        }
    }
}