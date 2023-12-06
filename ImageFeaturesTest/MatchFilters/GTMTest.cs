using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Collections.Generic;

namespace ImageFeaturesTest.MatchFilters
{
    [TestClass]
    public class GTMTest
    {
        [TestMethod]
        public void TestGraphEqualFull()
        {
            int[][] At = new int[6][];
            int[][] Atp = new int[6][];
            At[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            At[1] = new int[] { 3, 4, 5, 1, 6, -2 };
            At[2] = new int[] { 2, 5, 4, 1, 6, -2 };
            At[3] = new int[] { 2, 3, 5, 1, 6, -2 };
            At[4] = new int[] { 3, 2, 4, 1, 6, -2 };
            At[5] = new int[] { 4, 1, 5, 2, 3, -2 };

            Atp[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            Atp[1] = new int[] { 3, 4, 5, 1, 6, -2 };
            Atp[2] = new int[] { 2, 5, 4, 1, 6, -2 };
            Atp[3] = new int[] { 2, 3, 5, 1, 6, -2 };
            Atp[4] = new int[] { 3, 2, 4, 1, 6, -2 };
            Atp[5] = new int[] { 4, 1, 5, 2, 3, -2 };

            Assert.IsTrue(GraphEqual(At, Atp));
        }

        [TestMethod]
        public void TestGraphNotEqual()
        {
            int[][] At = new int[6][];
            int[][] Atp = new int[6][];
            At[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            At[1] = new int[] { 3, 4, 5, 1, 6, -2 };
            At[2] = new int[] { 2, 5, 4, 1, 6, -2 };
            At[3] = new int[] { 2, 3, 5, 1, 6, -2 };
            At[4] = new int[] { 3, 2, 4, 1, 6, -2 };
            At[5] = new int[] { 2, 1, 5, 2, 3, -2 };

            Atp[0] = new int[] { 4, 2, 6, 4, 6, -2 };
            Atp[1] = new int[] { 3, 4, 9, 2, 6, -2 };
            Atp[2] = new int[] { 2, 5, 6, 0, 6, -2 };
            Atp[3] = new int[] { 2, 3, 9, 4, 6, -2 };
            Atp[4] = new int[] { 3, 2, 6, 2, 6, -2 };
            Atp[5] = new int[] { 1, 1, 9, 0, 3, -2 };

            Assert.IsFalse(GraphEqual(At, Atp));
        }

      
        /// <summary>
        /// Checks if two optimized graphs are equal.
        /// </summary>
        /// <param name="A"></param>
        /// <param name="AP"></param>
        /// <returns></returns>
        public bool GraphEqual(int[][] A, int[][] AP)
        {
            HashSet<int> ARow, APRow;
            for (int i = 0; i < A.Length; i++)
            {

                ARow = new HashSet<int>();
                APRow = new HashSet<int>();

                if (A[i][0] == -1 && AP[i][0] == -1) continue;

                for (int k = 0; k < 5; k++) // changed K to 5
                {
                    ARow.Add(A[i][k]);
                    APRow.Add(AP[i][k]);
                }

                if (!ARow.SetEquals(APRow))
                    return false;
                ARow.Clear();
                APRow.Clear();
            }
            return true;
        }
    }
}
