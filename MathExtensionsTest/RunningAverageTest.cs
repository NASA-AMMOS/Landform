using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Linq;
using JPLOPS.MathExtensions;
using JPLOPS.Test;

namespace MathExtensionsTest
{
    [TestClass]
    public class RunningAverageTest
    {
        [TestMethod]
        public void RunningAverage()
        {
            double[] someDoubles = { 34.6, 45.1, 55.5, 78.5, 84.66, 1400.32, 99.04, 103.99, -1025.173, 0 };
            double average = someDoubles.Average();
            double sumOfSquaresOfDifferences = someDoubles.Select(val => (val - average) * (val - average)).Sum();
            double sd = Math.Sqrt(sumOfSquaresOfDifferences / (someDoubles.Length-1));
            double variance = sumOfSquaresOfDifferences / (someDoubles.Length - 1);

            RunningAverage ra = new RunningAverage();
            foreach(double d in someDoubles)
            {
                ra.Push(d);
            }
            Assert.AreEqual(someDoubles.Length, ra.Count);
            AssertE.AreSimilar(average, ra.Mean);
            Assert.AreEqual(-1025.173, ra.Min);
            Assert.AreEqual(1400.32, ra.Max);
            AssertE.AreSimilar(sd, ra.StandardDeviation);
            AssertE.AreSimilar(variance, ra.Variance);


            ra = new RunningAverage();

        }

        [TestMethod]
        public void RunningAverageEdgeCasees()
        {
            RunningAverage ra = new RunningAverage();
            Assert.AreEqual(0, ra.Count);
            AssertE.AreSimilar(0, ra.Mean);
            Assert.AreEqual(0, ra.Min);
            Assert.AreEqual(0, ra.Max);
            AssertE.AreSimilar(0, ra.StandardDeviation);
            AssertE.AreSimilar(0, ra.Variance);

            ra.Push(7);
            Assert.AreEqual(1, ra.Count);
            AssertE.AreSimilar(7, ra.Mean);
            Assert.AreEqual(7, ra.Min);
            Assert.AreEqual(7, ra.Max);
            AssertE.AreSimilar(0, ra.StandardDeviation);
            AssertE.AreSimilar(0, ra.Variance);
        }
    }
}
