using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace JPLOPS.Test
{
    public static class AssertE
    {
        public const double EPSILON = 1e-10;

        // Note that this is not the most rigorous way to compare floats:
        // https://randomascii.wordpress.com/2012/02/25/comparing-floating-point-numbers-2012-edition/
        public static void AreSimilar(double expected, double actual, double eps = EPSILON)
        {
            Assert.IsTrue(Math.Abs(expected - actual) < eps,
                string.Format("Values are not similar. Expected: {0}, Actual: {1} (tolerance: {2}", expected, actual, eps));
        }
    }
}
