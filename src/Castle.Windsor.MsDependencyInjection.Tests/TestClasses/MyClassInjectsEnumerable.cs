using System.Collections.Generic;
using System.Linq;

namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class MyClassInjectsEnumerable
    {
        public List<MyTestClass3> Objects { get; }

        public MyClassInjectsEnumerable(IEnumerable<MyTestClass3> objects)
        {
            Objects = objects.ToList();
        }
    }
}