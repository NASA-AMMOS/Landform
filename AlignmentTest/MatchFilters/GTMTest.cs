using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Alignment;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

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
            At[5] = new int[] { 2, 1, 5, 2, 3, -2 };

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

        [TestMethod]
        public void TestFindOutlierRepeating()
        {
            int K = 2;
            GTM gtm = new GTM(K);
            int[][] O = new int[8][];
            int[][] OPrime = new int[8][];

            O[0] = new int[] { 1, 7, 6, 2, 3, 4, 5, -2 };
            O[1] = new int[] { 0, 7, 6, 2, 3, 4, 5, -2 };
            O[2] = new int[] { 6, 3, 7, 1, 0, 4, 5, -2 };
            O[3] = new int[] { 2, 6, 4, 5, 7, 1, 0, -2 };
            O[4] = new int[] { 5, 3, 2, 7, 1, 6, 0, -2 };
            O[5] = new int[] { 4, 3, 2, 7, 6, 1, 0, -2 };
            O[6] = new int[] { 2, 1, 3, 7, 0, 4, 5, -2 };
            O[7] = new int[] { 1, 0, 2, 6, 3, 4, 5, -2 };

            OPrime[0] = new int[] { 1, 2, 6, 3, 4, 5, 7, -2 };
            OPrime[1] = new int[] { 0, 2, 3, 4, 6, 5, 7, -2 };
            OPrime[2] = new int[] { 3, 1, 0, 4, 5, 6, 7, -2 };
            OPrime[3] = new int[] { 2, 4, 5, 1, 0, 6, 7, -2 };
            OPrime[4] = new int[] { 6, 5, 3, 2, 7, 1, 0, -2 };
            OPrime[5] = new int[] { 7, 4, 3, 2, 6, 1, 0, -2 };
            OPrime[6] = new int[] { 4, 0, 1, 5, 2, 3, 7, -2};
            OPrime[7] = new int[] { 5, 4, 3, 6, 2, 1, 0, -2};

            HashSet<int>[] I = new HashSet<int>[8];
            I[0] = new HashSet<int>(new int[] { 1, 6, 7 });
            I[1] = new HashSet<int>(new int[] { 0, 7 });
            I[2] = new HashSet<int>(new int[] { 3, 6 });
            I[3] = new HashSet<int>(new int[] { 2, 4, 5 });
            I[4] = new HashSet<int>(new int[] { 5 });
            I[5] = new HashSet<int>(new int[] { 4 });
            I[6] = new HashSet<int>(new int[] { 2, 3 });
            I[7] = new HashSet<int>(new int[] { 0, 1 });

            HashSet<int>[] IPrime = new HashSet<int>[8];
            IPrime[0] = new HashSet<int>(new int[] { 1, 6 });
            IPrime[1] = new HashSet<int>(new int[] { 0, 2 });
            IPrime[2] = new HashSet<int>(new int[] { 0, 1, 3 });
            IPrime[3] = new HashSet<int>(new int[] { 2 });
            IPrime[4] = new HashSet<int>(new int[] { 3, 5, 6, 7 });
            IPrime[5] = new HashSet<int>(new int[] { 4, 7 });
            IPrime[6] = new HashSet<int>(new int[] { 4 });
            IPrime[7] = new HashSet<int>(new int[] { 5 });

            int[] C = new int[O.Length].Select(x => K).ToArray();
            int[] CPrime = new int[O.Length].Select(x => K).ToArray();

            Assert.AreEqual(6, gtm.FindOutlier(O, OPrime, I, IPrime, new HashSet<int>()));

            gtm.RemoveOutlier(6, O, OPrime, I, IPrime, C, CPrime, new HashSet<int>(new int[] { 6 }));
            int[][] ORes = new int[8][];
            int[][] OPrimeRes = new int[8][];

            ORes[0] = new int[] { 1, 7, 6, 2, 3, 4, 5, -2 };
            ORes[1] = new int[] { 0, 7, 6, 2, 3, 4, 5, -2 };
            ORes[2] = new int[] { 7, 3, 7, 1, 0, 4, 5, -2 };
            ORes[3] = new int[] { 2, 4, 4, 5, 7, 1, 0, -2 };
            ORes[4] = new int[] { 5, 3, 2, 7, 1, 6, 0, -2 };
            ORes[5] = new int[] { 4, 3, 2, 7, 6, 1, 0, -2 };
            ORes[6] = new int[] { -1, 1, 3, 7, 0, 4, 5, -2 };
            ORes[7] = new int[] { 1, 0, 2, 6, 3, 4, 5, -2 };

            OPrimeRes[0] = new int[] { 1, 2, 6, 3, 4, 5, 7, -2 };
            OPrimeRes[1] = new int[] { 0, 2, 3, 4, 6, 5, 7, -2 };
            OPrimeRes[2] = new int[] { 3, 1, 0, 4, 5, 6, 7, -2 };
            OPrimeRes[3] = new int[] { 2, 4, 5, 1, 0, 6, 7, -2 };
            OPrimeRes[4] = new int[] { 3, 5, 3, 2, 7, 1, 0, -2 };
            OPrimeRes[5] = new int[] { 7, 4, 3, 2, 6, 1, 0, -2 };
            OPrimeRes[6] = new int[] { -1, 0, 1, 5, 2, 3, 7, -2 };
            OPrimeRes[7] = new int[] { 5, 4, 3, 6, 2, 1, 0, -2 };

            Assert.IsTrue(gtm.GraphEqual(O, ORes));
            Assert.IsTrue(gtm.GraphEqual(OPrime, OPrimeRes));

            HashSet<int>[] IRes = new HashSet<int>[8];
            IRes[0] = new HashSet<int>(new int[] { 1, 7 });
            IRes[1] = new HashSet<int>(new int[] { 0, 7 });
            IRes[2] = new HashSet<int>(new int[] { 3 });
            IRes[3] = new HashSet<int>(new int[] { 2, 4, 5 });
            IRes[4] = new HashSet<int>(new int[] { 3, 5 });
            IRes[5] = new HashSet<int>(new int[] { 4 });
            IRes[6] = new HashSet<int>(new int[] {  });
            IRes[7] = new HashSet<int>(new int[] { 0, 1, 2 });

            HashSet<int>[] IPrimeRes = new HashSet<int>[8];
            IPrimeRes[0] = new HashSet<int>(new int[] { 1 });
            IPrimeRes[1] = new HashSet<int>(new int[] { 0, 2 });
            IPrimeRes[2] = new HashSet<int>(new int[] { 0, 1, 3 });
            IPrimeRes[3] = new HashSet<int>(new int[] { 2, 4 });
            IPrimeRes[4] = new HashSet<int>(new int[] { 3, 5, 7 });
            IPrimeRes[5] = new HashSet<int>(new int[] { 4, 7 });
            IPrimeRes[6] = new HashSet<int>(new int[] {  });
            IPrimeRes[7] = new HashSet<int>(new int[] { 5 });

            for (int i = 0; i < I.Length; i++)
            {
                Assert.IsTrue(IRes[i].SetEquals(I[i]));
                Assert.IsTrue(IPrimeRes[i].SetEquals(IPrime[i]));
            }




            Assert.AreEqual(7, gtm.FindOutlier(O, OPrime, I, IPrime, new HashSet<int>()));

            gtm.RemoveOutlier(7, O, OPrime, I, IPrime, C, CPrime, new HashSet<int>(new int[] { 6, 7 }));
            int[][] ORes1 = new int[8][];
            int[][] OPrimeRes1 = new int[8][];

            ORes1[0] = new int[] { 1, 2, 6, 2, 3, 4, 5, -2 };
            ORes1[1] = new int[] { 0, 2, 6, 2, 3, 4, 5, -2 };
            ORes1[2] = new int[] { 1, 3, 7, 1, 0, 4, 5, -2 };
            ORes1[3] = new int[] { 2, 4, 4, 5, 7, 1, 0, -2 };
            ORes1[4] = new int[] { 5, 3, 2, 7, 1, 6, 0, -2 };
            ORes1[5] = new int[] { 4, 3, 2, 7, 6, 1, 0, -2 };
            ORes1[6] = new int[] { -1, 1, 3, 7, 0, 4, 5, -2 };
            ORes1[7] = new int[] { -1, 0, 2, 6, 3, 4, 5, -2 };

            OPrimeRes1[0] = new int[] { 1, 2, 6, 3, 4, 5, 7, -2 };
            OPrimeRes1[1] = new int[] { 0, 2, 3, 4, 6, 5, 7, -2 };
            OPrimeRes1[2] = new int[] { 3, 1, 0, 4, 5, 6, 7, -2 };
            OPrimeRes1[3] = new int[] { 2, 4, 5, 1, 0, 6, 7, -2 };
            OPrimeRes1[4] = new int[] { 3, 5, 3, 2, 7, 1, 0, -2 };
            OPrimeRes1[5] = new int[] { 3, 4, 3, 2, 6, 1, 0, -2 };
            OPrimeRes1[6] = new int[] { -1, 0, 1, 5, 2, 3, 7, -2 };
            OPrimeRes1[7] = new int[] { -1, 4, 3, 6, 2, 1, 0, -2 };

            Assert.IsTrue(gtm.GraphEqual(O, ORes1));
            Assert.IsTrue(gtm.GraphEqual(OPrime, OPrimeRes1));

            HashSet<int>[] IRes1 = new HashSet<int>[8];
            IRes1[0] = new HashSet<int>(new int[] { 1 });
            IRes1[1] = new HashSet<int>(new int[] { 0, 2 });
            IRes1[2] = new HashSet<int>(new int[] { 0, 1, 3 });
            IRes1[3] = new HashSet<int>(new int[] { 2, 4, 5 });
            IRes1[4] = new HashSet<int>(new int[] { 3, 5 });
            IRes1[5] = new HashSet<int>(new int[] { 4 });
            IRes1[6] = new HashSet<int>(new int[] {  });
            IRes1[7] = new HashSet<int>(new int[] {  });

            HashSet<int>[] IPrimeRes1 = new HashSet<int>[8];
            IPrimeRes1[0] = new HashSet<int>(new int[] { 1 });
            IPrimeRes1[1] = new HashSet<int>(new int[] { 0, 2 });
            IPrimeRes1[2] = new HashSet<int>(new int[] { 0, 1, 3 });
            IPrimeRes1[3] = new HashSet<int>(new int[] { 2, 4, 5 });
            IPrimeRes1[4] = new HashSet<int>(new int[] { 3, 5 });
            IPrimeRes1[5] = new HashSet<int>(new int[] { 4 });
            IPrimeRes1[6] = new HashSet<int>(new int[] {  });
            IPrimeRes1[7] = new HashSet<int>(new int[] {  });

            for (int i = 0; i < I.Length; i++)
            {
                Assert.IsTrue(IRes1[i].SetEquals(I[i]));
                Assert.IsTrue(IPrimeRes1[i].SetEquals(IPrime[i]));
            }
        }

        [TestMethod]
        public void TestRemoveOutlierWithMedian()
        {
            int K = 2;
            GTM gtm = new GTM(K);

            ImageFeature u0 = new ImageFeature(new Vector2(0, 0), null);
            ImageFeature u1 = new ImageFeature(new Vector2(1, 0), null);
            ImageFeature u2 = new ImageFeature(new Vector2(3, 1), null);
            ImageFeature u3 = new ImageFeature(new Vector2(4, 2), null);
            ImageFeature u4 = new ImageFeature(new Vector2(2, 4), null);
            ImageFeature u5 = new ImageFeature(new Vector2(4, 5), null);
            ImageFeature u6 = new ImageFeature(new Vector2(3, 0), null);
            ImageFeature u6P = new ImageFeature(new Vector2(0, 4), null);
            ImageFeature u7 = new ImageFeature(new Vector2(1, 1), null);
            ImageFeature u7P = new ImageFeature(new Vector2(4, 7), null);

            ImageFeature[] modelSet = new ImageFeature[] { u0, u1, u2, u3, u4, u5, u6, u7 };
            ImageFeature[] dataSet = new ImageFeature[] { u0, u1, u2, u3, u4, u5, u6P, u7P };


            double[][] DistP = gtm.ComputeDistanceMatrix(modelSet);
            double[][] DistPPrime = gtm.ComputeDistanceMatrix(dataSet);
            double MedianP = gtm.ComputeMedian(DistP);
            double MedianPPrime = gtm.ComputeMedian(DistPPrime);
            int[][] O = gtm.InitMedianKNNGraphOptimized(DistP, MedianP);
            int[][] OPrime = gtm.InitMedianKNNGraphOptimized(DistPPrime, MedianPPrime);
            HashSet<int>[] I = gtm.InitNeighborVector(O, K);
            HashSet<int>[] IPrime = gtm.InitNeighborVector(OPrime, K);
            int[] C = new int[modelSet.Count()].Select(x => K).ToArray();
            int[] CPrime = new int[modelSet.Count()].Select(x => K).ToArray();

            HashSet<int> outliers = new HashSet<int>();
            int outlier1 = gtm.FindOutlier(O, OPrime, I, IPrime, new HashSet<int>());
            outliers.Add(outlier1);
            Assert.IsTrue(outlier1 == 6);
            gtm.RemoveOutlier(outlier1, O, OPrime, I, IPrime, C, CPrime, outliers);
            int outlier2 = gtm.FindOutlier(O, OPrime, I, IPrime, outliers);
            outliers.Add(outlier2);
            Assert.IsTrue(outlier2 == 7);
            gtm.RemoveOutlier(outlier2, O, OPrime, I, IPrime, C, CPrime, outliers);

            int[][] ORes = new int[8][];
            int[][] OPrimeRes = new int[8][];

            ORes[0] = new int[] { 1, 2, 6, 2, 3, 4, 5, -2 };
            ORes[1] = new int[] { 0, 2, 6, 2, 3, 4, 5, -2 };
            ORes[2] = new int[] { 1, 3, 7, 1, 0, 4, 5, -2 };
            ORes[3] = new int[] { 2, 4, 4, 5, 7, 1, 0, -2 };
            ORes[4] = new int[] { 5, 3, 2, 7, 1, 6, 0, -2 };
            ORes[5] = new int[] { 4, 3, 2, 7, 6, 1, 0, -2 };
            ORes[6] = new int[] { -1, 1, 3, 7, 0, 4, 5, -2 };
            ORes[7] = new int[] { -1, 0, 2, 6, 3, 4, 5, -2 };

            OPrimeRes[0] = new int[] { 1, 2, 6, 3, 4, 5, 7, -2 };
            OPrimeRes[1] = new int[] { 0, 2, 3, 4, 6, 5, 7, -2 };
            OPrimeRes[2] = new int[] { 3, 1, 0, 4, 5, 6, 7, -2 };
            OPrimeRes[3] = new int[] { 2, 4, 5, 1, 0, 6, 7, -2 };
            OPrimeRes[4] = new int[] { 3, 5, 3, 2, 7, 1, 0, -2 };
            OPrimeRes[5] = new int[] { 3, 4, 3, 2, 6, 1, 0, -2 };
            OPrimeRes[6] = new int[] { -1, 0, 1, 5, 2, 3, 7, -2 };
            OPrimeRes[7] = new int[] { -1, 4, 3, 6, 2, 1, 0, -2 };

            Assert.IsTrue(gtm.GraphEqual(O, ORes));
            Assert.IsTrue(gtm.GraphEqual(OPrime, OPrimeRes));
        }
    }
}
