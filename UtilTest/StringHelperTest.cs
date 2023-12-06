using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using JPLOPS.Util;

namespace UtilTest
{
    [TestClass]
    public class StringHelperTest
    {
        [TestMethod]
        public void TestFastParseInt()
        {
            var inputs = new Tuple<string,int>[]
            {
                new Tuple<string,int>("0", 0),
                new Tuple<string,int>("10", 10),
                new Tuple<string,int>("-10", -10),
                new Tuple<string,int>("12345", 12345),
                new Tuple<string,int>("6789 12345", 6789),
                new Tuple<string,int>("-12345", -12345),
                new Tuple<string,int>("+12345", 12345),
                new Tuple<string,int>(" +12345", 12345),
                new Tuple<string,int>("12345 ", 12345),
                new Tuple<string,int>("-12345. ", -12345),
                new Tuple<string,int>("12345 foo", 12345),
                new Tuple<string,int>("boo 12345", 12345),
                new Tuple<string,int>("boo12345", 12345),
                new Tuple<string,int>("boo12345foo", 12345),
                new Tuple<string,int>("999999999", 999999999),
                new Tuple<string,int>("+999999999", 999999999),
                new Tuple<string,int>("-999999999", -999999999),
                new Tuple<string,int>("-2147483648", -2147483648),
                new Tuple<string,int>("2147483647", 2147483647)
            };

            foreach (var pair in inputs)
            {
                Assert.AreEqual(pair.Item2, StringHelper.FastParseInt(pair.Item1), "input \"{0}\"", pair.Item1);
            }
        }

        [TestMethod]
        public void TestFastParseFloat()
        {
            var inputs = new Tuple<string,float>[]
            {
                new Tuple<string,float>("0", 0f),
                new Tuple<string,float>("10", 10f),
                new Tuple<string,float>("-10", -10f),
                new Tuple<string,float>("12345", 12345f),
                new Tuple<string,float>("-12345", -12345f),
                new Tuple<string,float>("+12345", 12345f),
                new Tuple<string,float>(" +12345", 12345f),
                new Tuple<string,float>("12345 ", 12345f),
                new Tuple<string,float>("-12345. ", -12345f),
                new Tuple<string,float>("12345 foo", 12345f),
                new Tuple<string,float>("boo 12345", 12345f),
                new Tuple<string,float>("boo12345", 12345f),
                new Tuple<string,float>("boo12345foo", 12345f),
                new Tuple<string,float>("999999999", 999999999f),
                new Tuple<string,float>("+999999999", 999999999f),
                new Tuple<string,float>("-999999999", -999999999f),
                new Tuple<string,float>("-2147483648", -2147483648),
                new Tuple<string,float>("2147483647", 2147483647),
                new Tuple<string,float>("0.", 0f),
                new Tuple<string,float>(".0", 0f),
                new Tuple<string,float>("10.1", 10.1f),
                new Tuple<string,float>("-10.5", -10.5f),
                new Tuple<string,float>("99999.99999", 99999.99999f),
                new Tuple<string,float>("-99999.99999", -99999.99999f),
                new Tuple<string,float>("12345.54321", 12345.54321f),
                new Tuple<string,float>(" 12345.54321", 12345.54321f),
                new Tuple<string,float>(" 12345.54321 ", 12345.54321f),
                new Tuple<string,float>("12345.54321 ", 12345.54321f),
                new Tuple<string,float>("12345.54321 6789", 12345.54321f),
                new Tuple<string,float>("-12345.12345", -12345.12345f),
                new Tuple<string,float>("+12345.0001", 12345.0001f),
                new Tuple<string,float>(" +12345.000", 12345f),
                new Tuple<string,float>("12345.54321 ", 12345.54321f),
                new Tuple<string,float>("-12345.", -12345f),
                new Tuple<string,float>("-.54321", -0.54321f),
                new Tuple<string,float>("+.54321", 0.54321f),
                new Tuple<string,float>(".54321", 0.54321f),
                new Tuple<string,float>(" .54321", 0.54321f),
                new Tuple<string,float>("-0.54321", -0.54321f),
                new Tuple<string,float>("-00.54321", -0.54321f),
                new Tuple<string,float>("-001.5432100", -1.54321f),
                new Tuple<string,float>("-001.005432100", -1.0054321f),
                new Tuple<string,float>("001.5432100", 1.54321f),
                new Tuple<string,float>("+001.5432100", 1.54321f),
                new Tuple<string,float>("12345. foo", 12345f),
                new Tuple<string,float>("boo 12345.", 12345f),
                new Tuple<string,float>("boo12345", 12345f),
                new Tuple<string,float>("boo12345foo", 12345f),
                new Tuple<string,float>("999999999.12345", 999999999.12345f),
                new Tuple<string,float>("+999999999.12345", 999999999.12345f),
                new Tuple<string,float>("-999999999.12345", -999999999.12345f),
                new Tuple<string,float>("1.2345e6", 1.2345e6f),
                new Tuple<string,float>("1.2345e-6", 1.2345e-6f),
                new Tuple<string,float>("-1.2345E+6", -1.2345e6f),
                new Tuple<string,float>("bar-1.2345E-6 foo", -1.2345e-6f),
                new Tuple<string,float>("123.456e7", 1.23456e9f),
                new Tuple<string,float>("123.e7", 1.23e9f),
                new Tuple<string,float>("123.e+7", 1.23e9f),
                new Tuple<string,float>("123.e-7", 1.23e-5f),
                new Tuple<string,float>(".567e-7", 5.67e-8f),
                new Tuple<string,float>("5e07", 5.0e7f),
                new Tuple<string,float>("5e-07", 5.0e-7f),
                new Tuple<string,float>("0.60970000000000368000000000000000000000001", 0.6097f)
            };

            float tol = 1e-6f;

            foreach (var pair in inputs)
            {
                Assert.AreEqual(pair.Item2, StringHelper.FastParseFloat(pair.Item1), tol, "input \"{0}\"", pair.Item1);
            }
        }
    }
}
