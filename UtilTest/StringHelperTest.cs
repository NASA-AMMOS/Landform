using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Util;

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
                new Tuple<string,int>("6789 12345", 12345),
                new Tuple<string,int>("-12345", -12345),
                new Tuple<string,int>("+12345", 12345),
                new Tuple<string,int>(" +12345", 12345),
                new Tuple<string,int>("12345 ", 12345),
                new Tuple<string,int>("-12345. ", -12345),
                new Tuple<string,int>("12345 foo", 12345),
                new Tuple<string,int>("boo 12345", 12345),
                new Tuple<string,int>("boo12345", 12345),
                new Tuple<string,int>("boo12345foo", 12345),
                new Tuple<string,int>("1999999999", 1999999999),
                new Tuple<string,int>("+1999999999", 1999999999),
                new Tuple<string,int>("-1999999999", -1999999999)
            };

            foreach (var pair in inputs)
            {
                Assert.AreEqual(pair.Item2, StringHelper.FastParseInt(pair.Item1), "input \"{0}\"", pair.Item1);
            }
        }

        [TestMethod]
        public void TestFastParseDouble()
        {
            var inputs = new Tuple<string,double>[]
            {
                new Tuple<string,double>("0", 0),
                new Tuple<string,double>("10", 10),
                new Tuple<string,double>("-10", -10),
                new Tuple<string,double>("12345", 12345),
                new Tuple<string,double>("-12345", -12345),
                new Tuple<string,double>("+12345", 12345),
                new Tuple<string,double>(" +12345", 12345),
                new Tuple<string,double>("12345 ", 12345),
                new Tuple<string,double>("-12345. ", -12345),
                new Tuple<string,double>("12345 foo", 12345),
                new Tuple<string,double>("boo 12345", 12345),
                new Tuple<string,double>("boo12345", 12345),
                new Tuple<string,double>("boo12345foo", 12345),
                new Tuple<string,double>("1999999999", 1999999999),
                new Tuple<string,double>("+1999999999", 1999999999),
                new Tuple<string,double>("-1999999999", -1999999999),
                new Tuple<string,double>("0.", 0),
                new Tuple<string,double>(".0", 0),
                new Tuple<string,double>("10.1", 10.1),
                new Tuple<string,double>("-10.5", -10.5),
                new Tuple<string,double>("99999.99999", 99999.99999),
                new Tuple<string,double>("-99999.99999", -99999.99999),
                new Tuple<string,double>("12345.54321", 12345.54321),
                new Tuple<string,double>(" 12345.54321", 12345.54321),
                new Tuple<string,double>(" 12345.54321 ", 12345.54321),
                new Tuple<string,double>("12345.54321 ", 12345.54321),
                new Tuple<string,double>("6789 12345.54321 ", 12345.54321),
                new Tuple<string,double>("-12345.12345", -12345.12345),
                new Tuple<string,double>("+12345.0001", 12345.0001),
                new Tuple<string,double>(" +12345.000", 12345),
                new Tuple<string,double>("12345.54321 ", 12345.54321),
                new Tuple<string,double>("-12345.", -12345),
                new Tuple<string,double>("-.54321", -0.54321),
                new Tuple<string,double>("-0.54321", -0.54321),
                new Tuple<string,double>("-00.54321", -0.54321),
                new Tuple<string,double>("-001.5432100", -1.54321),
                new Tuple<string,double>("001.5432100", 1.54321),
                new Tuple<string,double>("+001.5432100", 1.54321),
                new Tuple<string,double>("12345. foo", 12345),
                new Tuple<string,double>("boo 12345.", 12345),
                new Tuple<string,double>("boo12345", 12345),
                new Tuple<string,double>("boo12345foo", 12345),
                new Tuple<string,double>("1999999999.12345", 1999999999.12345),
                new Tuple<string,double>("+1999999999.12345", 1999999999.12345),
                new Tuple<string,double>("-1999999999.12345", -1999999999.12345)
            };

            double tol = 1e-6;

            foreach (var pair in inputs)
            {
                Assert.AreEqual(pair.Item2, StringHelper.FastParseDouble(pair.Item1), tol, "input \"{0}\"", pair.Item1);
            }
        }
    }
}
