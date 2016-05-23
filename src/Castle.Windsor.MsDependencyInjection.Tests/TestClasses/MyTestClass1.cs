namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class MyTestClass1 : BaseTestClass
    {
        private readonly MyTestClass2 _testObj2;

        public MyTestClass1(MyTestClass2 testObj2)
        {
            _testObj2 = testObj2;
        }
    }
}