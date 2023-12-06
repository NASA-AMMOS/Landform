using Microsoft.VisualStudio.TestTools.UnitTesting;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;

namespace ImagingTest
{
    [TestClass]
    public class CAHVTest
    {
        [TestMethod]
        public void TestCAHV()
        {
            double[] C = new double[] { 0.823394, 0.796086, -1.84555 };
            double[] A = new double[] { -0.589809, 0.344282, 0.730464 };
            double[] H = new double[] { -919.237, -888.049, 370.124 };
            double[] V = new double[] { 473.569, -276.566, 1210.87 };

            CAHV cahv = new CAHV(new Vector3(C), new Vector3(A), new Vector3(H), new Vector3(V));
            Reference.CAHV oldcahv = new Reference.CAHV(C, A, H, V);

            double[] pos3 = new double[3];
            double[] uvec3 = new double[3];

            for (double x = -1024; x < 1024; x += 3.3)
            {
                for (double y = -1024; y < 1024; y += 3.3)
                {
                    Ray ray = cahv.Unproject(new Vector2(x, y));
                    oldcahv.Project_2d_to_3d(new double[] { x, y }, pos3, uvec3);
                    Assert.AreEqual(pos3[0], ray.Position.X);
                    Assert.AreEqual(pos3[1], ray.Position.Y);
                    Assert.AreEqual(pos3[2], ray.Position.Z);
                    Assert.AreEqual(uvec3[0], ray.Direction.X);
                    Assert.AreEqual(uvec3[1], ray.Direction.Y);
                    Assert.AreEqual(uvec3[2], ray.Direction.Z);

                    for (double r = -5; r < 20; r += 2.321)
                    {
                        Vector3 point = ray.Position + ray.Direction * r;
                        double range;
                        Vector2 pixelPos = cahv.Project(point, out range);

                        double oldRange;
                        double[] oldPixelPos = new double[2];
                        double[] oldPoint = new double[3];
                        Reference.CAHV.scale3(r, uvec3, oldPoint);
                        Reference.CAHV.add3(oldPoint, pos3, oldPoint);
                        oldcahv.Project_3d_to_2d(oldPoint, out oldRange, oldPixelPos);

                        Assert.AreEqual(oldPixelPos[0], pixelPos.X);
                        Assert.AreEqual(oldPixelPos[1], pixelPos.Y);
                        // This assert disabled because oldRange believed to be incorrect
                        //Assert.AreEqual(oldRange, range);

                        Vector3 pointOther = cahv.Unproject(new Vector2(x, y), r);
                        Assert.AreEqual(point, pointOther);
                        Assert.AreEqual(r, range, 0.0001);
                    }
                }
            }
        }

        [TestMethod]
        public void TestCAHVClone()
        {
            CAHV cm = new CAHV(new Vector3(1, 2, 3), new Vector3(4, 5, 6), new Vector3(7, 8, 9), new Vector3(10, 11, 12));
            CAHV cm2 = (CAHV) cm.Clone();
            Assert.AreEqual(cm.C, cm2.C);
            Assert.AreEqual(cm.A, cm2.A);
            Assert.AreEqual(cm.H, cm2.H);
            Assert.AreEqual(cm.V, cm2.V);
            cm2.C = Vector3.Zero;
            Assert.AreEqual(new Vector3(1, 2, 3), cm.C);
        }
    }
}
