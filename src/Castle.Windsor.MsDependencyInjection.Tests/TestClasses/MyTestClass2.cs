namespace Castle.Windsor.MsDependencyInjection.Tests.TestClasses
{
    public class MyTestClass2 : BaseTestClass
    {
        private readonly MyTestClass3 _testObj3;

        public MyTestClass2(MyTestClass3 testObj3)
        {
            _testObj3 = testObj3;
        }
    }
}