using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.Test;

namespace ImagingTest
{
    [TestClass]
    public class CAHVORETest
    {
        // TODO: Get actual model with R and E values and test with all perspective, fisheye, and custom linearity
        [TestMethod]
        public void TestCAHVORE()
        {
            double[] C = new double[] {8.440591e-01, 6.851400e-01,-1.970154e+00};
            double[] A = new double[] {-3.992719e-01, 2.502615e-01,8.820063e-01};
            double[] H = new double[] { -2.880015e+03, -3.672948e+03,5.334030e+02};
            double[] V = new double[] {3.133263e+03, -2.175767e+03,2.717509e+03};
            double[] O = new double[] {-4.017427e-01, 2.497198e-01,8.810374e-01};
            double[] R = new double[] {0,0,0 };
            double[] E = new double[] {0,0,0 };

            CAHVORE cahvor = new CAHVORE(new Vector3(C), new Vector3(A), new Vector3(H), new Vector3(V), 
                                       new Vector3(O), new Vector3(R), new Vector3(E), CAHVORE.PERSPECTIVE_LINEARITY);
            Reference.CAHVORE oldcahvore = new Reference.CAHVORE(C, A, H, V, O, R, E, 0, 1);

            double[] pos3 = new double[3];
            double[] uvec3 = new double[3];
           
            for (double x = 0; x < 1024; x+=3.3)
            {
                for (double y = 0; y < 1024; y += 3.3)
                {
                    Ray ray = cahvor.Unproject(new Vector2(x,y));
                    oldcahvore.Project_2d_to_3d(new double[] { x, y }, pos3, uvec3);
                    AssertE.AreSimilar(pos3[0], ray.Position.X);
                    AssertE.AreSimilar(pos3[1], ray.Position.Y);
                    AssertE.AreSimilar(pos3[2], ray.Position.Z);
                    AssertE.AreSimilar(uvec3[0], ray.Direction.X);
                    AssertE.AreSimilar(uvec3[1], ray.Direction.Y);
                    AssertE.AreSimilar(uvec3[2], ray.Direction.Z);

                    for (double r = 0.5; r < 20; r += 2.321)
                    {
                        Vector3 point = ray.Position + ray.Direction * r;
                        double range;
                        Vector2 pixelPos = cahvor.Project(point, out range);
                        
                        double oldRange;
                        double[] oldPixelPos = new double[2];
                        double[] oldPoint = new double[3];
                        Reference.CAHV.scale3(r, uvec3, oldPoint);
                        Reference.CAHV.add3(oldPoint, pos3, oldPoint);
                        oldcahvore.Project_3d_to_2d(oldPoint, out oldRange, oldPixelPos);

                        AssertE.AreSimilar(oldPixelPos[0], pixelPos.X);
                        AssertE.AreSimilar(oldPixelPos[1], pixelPos.Y);
                        // This assert disabled because oldRange believed to be incorrect
                        //AssertAlmostEqual(oldRange, range);

                        Vector3 pointOther = cahvor.Unproject(new Vector2(x, y), r);
                        Vector3.AlmostEqual(point, pointOther, eps: AssertE.EPSILON);
                        Assert.AreEqual(r, range, 0.001);
                    }
                }
            }
        }


        [TestMethod]
        public void TestCAHVOREClone()
        {
            CAHVORE cm = new CAHVORE(new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12), new Vector3(13, 14, 15), new Vector3(16, 17, 18), new Vector3(19, 20, 21), 7);
            CAHVORE cm2 = (CAHVORE)cm.Clone();
            Assert.AreEqual(cm.C, cm2.C);
            Assert.AreEqual(cm.A, cm2.A);
            Assert.AreEqual(cm.H, cm2.H);
            Assert.AreEqual(cm.V, cm2.V);
            Assert.AreEqual(cm.O, cm2.O);
            Assert.AreEqual(cm.R, cm2.R);
            Assert.AreEqual(cm.E, cm2.E);
            Assert.AreEqual(cm.Linearity, cm2.Linearity);
            cm2.C = cm2.A = cm2.H = cm2.V = cm2.O = cm2.R = cm2.E = Vector3.Zero;
            cm2.Linearity = 2;
            Assert.AreEqual(new Vector3(1, 2, 3), cm.C);
            Assert.AreEqual(new Vector3(4, 5, 6), cm.A);
            Assert.AreEqual(new Vector3(7, 8, 9), cm.H);
            Assert.AreEqual(new Vector3(10, 11, 12), cm.V);
            Assert.AreEqual(new Vector3(13, 14, 15), cm.O);
            Assert.AreEqual(new Vector3(16, 17, 18), cm.R);
            Assert.AreEqual(new Vector3(19, 20, 21), cm.E);
            Assert.AreEqual(7, cm.Linearity);
        }
    }
}
