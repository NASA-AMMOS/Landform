using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class CAHVORTest
    {
        [TestMethod]
        public void TestCAHVOR()
        {
            double[] C = new double[] {8.440591e-01, 6.851400e-01,-1.970154e+00};
            double[] A = new double[] {-3.992719e-01, 2.502615e-01,8.820063e-01};
            double[] H = new double[] { -2.880015e+03, -3.672948e+03,5.334030e+02};
            double[] V = new double[] {3.133263e+03, -2.175767e+03,2.717509e+03};
            double[] O = new double[] {-4.017427e-01, 2.497198e-01,8.810374e-01};
            double[] R = new double[] {-1.510000e-04, -1.391890e-01,-1.250336e+00 };


            CAHVOR cahvor = new CAHVOR(new Vector3(C), new Vector3(A), new Vector3(H), new Vector3(V), 
                                       new Vector3(O), new Vector3(R));
            Reference.CAHVOR oldcahvor = new Reference.CAHVOR(C, A, H, V, O, R);

            double[] pos3 = new double[3];
            double[] uvec3 = new double[3];
           
            for (double x = 0; x < 1024; x+=3.3)
            {
                for (double y = 0; y < 1024; y += 3.3)
                {
                    Ray ray = cahvor.Unproject(new Vector2(x,y));
                    oldcahvor.Project_2d_to_3d(new double[] { x, y }, pos3, uvec3);
                    Assert.AreEqual(pos3[0], ray.Position.X);
                    Assert.AreEqual(pos3[1], ray.Position.Y);
                    Assert.AreEqual(pos3[2], ray.Position.Z);
                    Assert.AreEqual(uvec3[0], ray.Direction.X);
                    Assert.AreEqual(uvec3[1], ray.Direction.Y);
                    Assert.AreEqual(uvec3[2], ray.Direction.Z);

                    for (double r = 0.5; r < 20; r += 2.321)
                    {
                        Vector3 point = ray.Position + ray.Direction*r;
                        double range;
                        Vector2 pixelPos = cahvor.Project(point, out range);
                        
                        double oldRange;
                        double[] oldPixelPos = new double[2];
                        double[] oldPoint = new double[3];
                        Reference.CAHV.scale3(r, uvec3, oldPoint);
                        Reference.CAHV.add3(oldPoint, pos3, oldPoint);
                        oldcahvor.Project_3d_to_2d(oldPoint, out oldRange, oldPixelPos);

                        Assert.AreEqual(oldPixelPos[0], pixelPos.X);
                        Assert.AreEqual(oldPixelPos[1], pixelPos.Y);
                        // This assert disabled because oldRange believed to be incorrect
                        //Assert.AreEqual(oldRange, range);

                        Vector3 pointOther = cahvor.Unproject(new Vector2(x, y), r);
                        Assert.AreEqual(point, pointOther);
                        Assert.AreEqual(r, range, 0.0001);
                    }
                }
            }
        }

        [TestMethod]
        public void TestCAHVORClone()
        {
            CAHVOR cm = new CAHVOR(new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12), new Vector3(13,14,15), new Vector3(16,17,18));
            CAHVOR cm2 = (CAHVOR)cm.Clone();
            Assert.AreEqual(cm.C, cm2.C);
            Assert.AreEqual(cm.A, cm2.A);
            Assert.AreEqual(cm.H, cm2.H);
            Assert.AreEqual(cm.V, cm2.V);
            Assert.AreEqual(cm.O, cm2.O);
            Assert.AreEqual(cm.R, cm2.R);
            cm2.C = Vector3.Zero;
            Assert.AreEqual(new Vector3(1, 2, 3), cm.C);
        }
    }
}
