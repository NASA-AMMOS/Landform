using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Alignment;
using System.Collections.Generic;
using System.Linq;

namespace AlignmentTest.MatchFilters
{
    [TestClass]
    public class GTMTest
    {
        [TestMethod]
        public void TestGraphEqualFull()
        {
            GTM gtm = new GTM(2);
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

            Assert.IsTrue(gtm.GraphEqual(At, Atp));
        }

        [TestMethod]
        public void TestGraphEqualKOnly()
        {
            GTM gtm = new GTM(2);
            int[][] At = new int[6][];
            int[][] Atp = new int[6][];
            At[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            At[1] = new int[] { 3, 4, 5, 1, 6, -2 };
            At[2] = new int[] { 2, 5, 4, 1, 6, -2 };
            At[3] = new int[] { 2, 3, 5, 1, 6, -2 };
            At[4] = new int[] { 3, 2, 4, 1, 6, -2 };
            At[5] = new int[] { -1, 1, 5, 2, 3, -2 };

            Atp[0] = new int[] { 4, 2, 6, 4, 6, -2 };
            Atp[1] = new int[] { 3, 4, 9, 2, 6, -2 };
            Atp[2] = new int[] { 2, 5, 6, 0, 6, -2 };
            Atp[3] = new int[] { 2, 3, 9, 4, 6, -2 };
            Atp[4] = new int[] { 3, 2, 6, 2, 6, -2 };
            Atp[5] = new int[] { -1, 1, 9, 0, 3, -2 };

            Assert.IsTrue(gtm.GraphEqual(At, Atp));
        }

        [TestMethod]
        public void TestGraphNotEqual()
        {
            GTM gtm = new GTM(2);
            int[][] At = new int[6][];
            int[][] Atp = new int[6][];
            At[0] = new int[] { 4, 2, 3, 5, 6, -2 };
            At[1] = new int[] { 3, 4, 5, 1, 6, -2 };
            At[2] = new int[] { 2, 5, 4, 1, 6, -2 };
            At[3] = new int[] { 2, 3, 5, 1, 6, -2 };
            At[4] = new int[] { 3, 2, 4, 1, 6, -2 };
            At[5] = new int[] { -1, 1, 5, 2, 3, -2 };

            Atp[0] = new int[] { 4, 2, 6, 4, 6, -2 };
            Atp[1] = new int[] { 3, 4, 9, 2, 6, -2 };
            Atp[2] = new int[] { 2, 5, 6, 0, 6, -2 };
            Atp[3] = new int[] { 2, 3, 9, 4, 6, -2 };
            Atp[4] = new int[] { 3, 2, 6, 2, 6, -2 };
            Atp[5] = new int[] { 1, 1, 9, 0, 3, -2 };

            Assert.IsFalse(gtm.GraphEqual(At, Atp));
        }

        [TestMethod]
        public void TestFindOutlier()
        {
            GTM gtm = new GTM(2);
            int[][] At = new int[6][];
            int[][] Atp = new int[6][];
            At[0] = new int[] { 3, 1, 2, 4, 5, -2 };
            At[1] = new int[] { 2, 3, 4, 0, 5, -2 };
            At[2] = new int[] { 1, 4, 3, 0, 5, -2 };
            At[3] = new int[] { 1, 2, 4, 0, 5, -2 };
            At[4] = new int[] { 2, 1, 3, 0, 5, -2 };
            At[5] = new int[] { 3, 0, 4, 1, 2, -2 };

            Atp[0] = new int[] { 3, 1, 2, 4, 5, -2 };
            Atp[1] = new int[] { 2, 3, 5, 4, 0, -2 };
            Atp[2] = new int[] { 5, 1, 4, 3, 0, -2 };
            Atp[3] = new int[] { 1, 2, 0, 4, 5, -2 };
            Atp[4] = new int[] { 5, 2, 1, 3, 0, -2 };
            Atp[5] = new int[] { 2, 4, 1, 3, 0, -2 };

            HashSet<int>[] It = new HashSet<int>[6];
            It[0] = new HashSet<int>(new int[] { 5 });
            It[1] = new HashSet<int>(new int[] { 0, 2, 3, 4 });
            It[2] = new HashSet<int>(new int[] { 1, 3, 4 });
            It[3] = new HashSet<int>(new int[] { 0, 1, 5 });
            It[4] = new HashSet<int>(new int[] { 2 });
            It[5] = new HashSet<int>(new int[] { });

            HashSet<int>[] Itp = new HashSet<int>[6];
            Itp[0] = new HashSet<int>(new int[] { });
            Itp[1] = new HashSet<int>(new int[] { 0, 2, 3 });
            Itp[2] = new HashSet<int>(new int[] { 1, 3, 4, 5 });
            Itp[3] = new HashSet<int>(new int[] { 0, 1 });
            Itp[4] = new HashSet<int>(new int[] { 5 });
            Itp[5] = new HashSet<int>(new int[] { 2, 4 });

            int[] Ct = new int[At.Length].Select(x => 2).ToArray();
            int[] Ctp = new int[At.Length].Select(x => 2).ToArray();

            Assert.AreEqual(5, gtm.FindOutlier(At, Atp, It, Itp, new HashSet<int>()));

            gtm.RemoveOutlier(5, At, Atp, It, Itp, Ct, Ctp, new HashSet<int>(new int[] { 5 }));
            int[][] AtRes = new int[6][];
            int[][] AtpRes = new int[6][];

            AtRes[0] = new int[] { 3, 1, 2, 4, 5, -2 };
            AtRes[1] = new int[] { 2, 3, 4, 0, 5, -2 };
            AtRes[2] = new int[] { 1, 4, 3, 0, 5, -2 };
            AtRes[3] = new int[] { 1, 2, 4, 0, 5, -2 };
            AtRes[4] = new int[] { 2, 1, 3, 0, 5, -2 };
            AtRes[5] = new int[] { -1, 0, 4, 1, 2, -2 };

            AtpRes[0] = new int[] { 3, 1, 2, 4, 5, -2 };
            AtpRes[1] = new int[] { 2, 3, 5, 4, 0, -2 };
            AtpRes[2] = new int[] { 4, 1, 4, 3, 0, -2 };
            AtpRes[3] = new int[] { 1, 2, 0, 4, 5, -2 };
            AtpRes[4] = new int[] { 1, 2, 1, 3, 0, -2 };
            AtpRes[5] = new int[] { -1, 4, 1, 3, 0, -2 };

            Assert.IsTrue(gtm.GraphEqual(At, AtRes));
            Assert.IsTrue(gtm.GraphEqual(Atp, AtpRes));

            HashSet<int>[] ItRes = new HashSet<int>[6];
            ItRes[0] = new HashSet<int>(new int[] { });
            ItRes[1] = new HashSet<int>(new int[] { 0, 2, 3, 4 });
            ItRes[2] = new HashSet<int>(new int[] { 1, 3, 4 });
            ItRes[3] = new HashSet<int>(new int[] { 0, 1 });
            ItRes[4] = new HashSet<int>(new int[] { 2 });
            ItRes[5] = new HashSet<int>(new int[] { });

            HashSet<int>[] ItpRes = new HashSet<int>[6];
            ItpRes[0] = new HashSet<int>(new int[] { });
            ItpRes[1] = new HashSet<int>(new int[] { 0, 2, 3, 4 });
            ItpRes[2] = new HashSet<int>(new int[] { 1, 3, 4 });
            ItpRes[3] = new HashSet<int>(new int[] { 0, 1 });
            ItpRes[4] = new HashSet<int>(new int[] { 2 });
            ItpRes[5] = new HashSet<int>(new int[] { });

            for (int i = 0; i < It.Length; i++)
            {
                Assert.IsTrue(ItRes[i].SetEquals(It[i]));
                Assert.IsTrue(ItpRes[i].SetEquals(Itp[i]));
            }
        }


    }
}
