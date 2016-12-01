using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using Microsoft.Xna.Framework;

namespace ImagingTest.Serializers
{
    [TestClass]
    public class PDSMetadataTest
    {
        [TestMethod]
        [DeploymentItem("TestData", ".")]
        public void PDSMetadata()
        {
            {
                string filename = @"ML0_451292526RCX_S0311094MCAM02555M1.IMG";
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1408);
                Assert.AreEqual(m.Height, 1200);
                Assert.AreEqual(m.Bands, 3);
                Assert.AreEqual(1408, m.RecordBytes);

                Assert.AreEqual(typeof(CAHVOR), m.CameraModel.GetType());
                var cm = (CAHVOR)m.CameraModel;
                Vector3 mc_1 = new Vector3(8.414585e-01, 6.835634e-01, -1.974642e+00);
                Vector3 mc_2 = new Vector3(-5.092083e-01, 2.731827e-01, 8.161263e-01);
                Vector3 mc_3 = new Vector3(-2.645033e+03, -3.850412e+03, 4.976792e+02);
                Vector3 mc_4 = new Vector3(2.972422e+03, -1.740013e+03, 3.173833e+03);
                Vector3 mc_5 = new Vector3(-5.115142e-01, 2.723840e-01, 8.149505e-01);
                Vector3 mc_6 = new Vector3(-1.510000e-04, -1.391890e-01, -1.250336e+00);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
                Assert.IsTrue(Vector3.AlmostEqual(mc_5, cm.O));
                Assert.IsTrue(Vector3.AlmostEqual(mc_6, cm.R));

                Assert.IsTrue(m.Filename.Contains(filename));
                Assert.AreEqual(2014, m.ProductCreationTime.Year);
                Assert.AreEqual(4, m.ProductCreationTime.Month);
                Assert.AreEqual(22, m.ProductCreationTime.Day);
                Assert.AreEqual(1, m.FirstLine);
                Assert.AreEqual(129, m.FirstSample);
                Assert.AreEqual(8, m.BitDepth);
                Assert.AreEqual((uint)255, m.BitMask);
                Assert.AreEqual("0606ML0025550030301440E01_DRCX",  m.ProductId.filename);
                Assert.AreEqual(PDSInstrument.MastcamLeft, m.Instrument);
                Assert.AreEqual(PDSGeometryProjection.Raw, m.GeometryProjection);
                Assert.AreEqual(PDSImageSizeType.Regular, m.ImageSizeType);
                Assert.AreEqual(PDSDerivedImageType.Image, m.DerivedImageType);
                Assert.AreEqual(Institution.MSSS, m.ProducingInstitution);
                Assert.AreEqual(10.1, m.ExposureDuration);
                Assert.AreEqual(0, m.FilterNumber);
                Assert.AreEqual(2.9, m.MaximumFocusDistance);
                Assert.AreEqual(451292530.0000, m.SpacecraftClock);
                Assert.AreEqual(new Quaternion(-0.0452929, 0.0026977, -0.9728553, 0.2269226), m.RoverOriginRotation);
                Assert.AreEqual(new Vector3(-78.980461, -44.499561, 2.310069), m.OriginOffset);
                var rmc = new int[] { 31, 1094, 52, 106, 0, 0, 468, 268, 0, 0 };
                for (int i = 0; i < rmc.Length; i++)
                {
                    Assert.AreEqual(rmc[i], m.MotionCounter[i]);
                }
                Assert.AreEqual("0003101094", m.SiteDrive);
                Assert.AreEqual(606, m.PlanetDayNumber);
                
                Assert.AreEqual(1.568154, m.Articulation.ArmAngle1);
                Assert.AreEqual(-0.277720, m.Articulation.ArmAngle2);
                Assert.AreEqual(-2.825491, m.Articulation.ArmAngle3);
                Assert.AreEqual(3.116510, m.Articulation.ArmAngle4);
                Assert.AreEqual(0.593527, m.Articulation.ArmAngle5);

                Assert.AreEqual(0.049941, m.Articulation.LeftBogieAngle);
                Assert.AreEqual(-0.000753, m.Articulation.RightBogieAngle);
                Assert.AreEqual(0.003807, m.Articulation.LeftRockerAngle);
                Assert.AreEqual(5.771630, m.Articulation.MastAzimuth);
                Assert.AreEqual(0.631446, m.Articulation.MastElevation);
            }

            {
                string filename = @"NLB_451025090ARMLF0311052NCAM00493M1.IMG";
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1024);
                Assert.AreEqual(m.Height, 1024);
                Assert.AreEqual(m.Bands, 5);

                Assert.AreEqual(typeof(CAHV), m.CameraModel.GetType());
                var cm = (CAHV)m.CameraModel;
                Vector3 mc_1 = new Vector3(1.04791, 0.571407, -1.93105);
                Vector3 mc_2 = new Vector3(0.433947, 0.897764, 0.0754502);
                Vector3 mc_3 = new Vector3(-887.963, 990.629, 36.5519);
                Vector3 mc_4 = new Vector3(179.469, 375.167, 1262.74);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
            }

            {
                string filename = @"NLB_451557756RASLF0311330NCAM00353M1.IMG";
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1024);
                Assert.AreEqual(m.Height, 1024);
                Assert.AreEqual(m.Bands, 1);

                Assert.AreEqual(typeof(CAHV), m.CameraModel.GetType());
                var cm = (CAHV)m.CameraModel;
                Vector3 mc_1 = new Vector3(0.809503, 0.324769, -1.8358);
                Vector3 mc_2 = new Vector3(0.558137, -0.23828, 0.794788);
                Vector3 mc_3 = new Vector3(764.598, 1011.53, 403.962);
                Vector3 mc_4 = new Vector3(-613.528, 259.841, 1150.39);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
            }

            {
                string filename = @"NLB_451649560RNGLF0311330NCAM12813M1.IMG";
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 1024);
                Assert.AreEqual(m.Height, 1024);
                Assert.AreEqual(m.Bands, 1);

                Assert.AreEqual(typeof(CAHV), m.CameraModel.GetType());
                var cm = (CAHV)m.CameraModel;
                Vector3 mc_1 = new Vector3(0.733394, 0.781776, -1.98051);
                Vector3 mc_2 = new Vector3(-0.921673, 0.0942946, -0.376311);
                Vector3 mc_3 = new Vector3(-591.815, -1176.07, -191.985);
                Vector3 mc_4 = new Vector3(-929.662, 93.7434, 945.276);
                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));

                Assert.IsTrue(m.Filename.Contains(filename));
                Assert.AreEqual(2014, m.ProductCreationTime.Year);
                Assert.AreEqual(4, m.ProductCreationTime.Month);
                Assert.AreEqual(27, m.ProductCreationTime.Day);
                Assert.AreEqual(1, m.FirstLine);
                Assert.AreEqual(1, m.FirstSample);
                Assert.AreEqual(32, m.BitDepth);
                Assert.AreEqual((uint)32767, m.BitMask);
                Assert.AreEqual("NLB_451649560RNGLF0311330NCAM12813M1", m.ProductId.filename);
                Assert.AreEqual(PDSInstrument.NavcamLeft, m.Instrument);
                Assert.AreEqual(PDSGeometryProjection.Linearized, m.GeometryProjection);
                Assert.AreEqual(PDSImageSizeType.Regular, m.ImageSizeType);
                Assert.AreEqual(PDSDerivedImageType.Range, m.DerivedImageType);
                Assert.AreEqual(Institution.OPGS, m.ProducingInstitution);
                Assert.AreEqual(353.28, m.ExposureDuration);
                Assert.AreEqual(0, m.FilterNumber);
                Assert.AreEqual(double.MaxValue, m.MaximumFocusDistance);
                Assert.AreEqual(451649560.213, m.SpacecraftClock);
                Assert.AreEqual(new Quaternion(0.0252917, 0.0771217, -0.460822, 0.883773), m.RoverOriginRotation);
                Assert.AreEqual(new Vector3(-85.4875, -59.027, 1.74495), m.OriginOffset);
                var rmc = new int[] { 31, 1330, 6, 0, 0, 0, 146, 138, 0, 0 };
                for (int i = 0; i < rmc.Length; i++)
                {
                    Assert.AreEqual(rmc[i], m.MotionCounter[i]);
                }
                Assert.AreEqual("0003101330", m.SiteDrive);
                Assert.AreEqual(610, m.PlanetDayNumber);

                Assert.AreEqual(1.56815, m.Articulation.ArmAngle1);
                Assert.AreEqual(-0.27772, m.Articulation.ArmAngle2);
                Assert.AreEqual(-2.82549, m.Articulation.ArmAngle3);
                Assert.AreEqual(3.11651, m.Articulation.ArmAngle4);
                Assert.AreEqual(0.593527, m.Articulation.ArmAngle5);

                Assert.AreEqual(-0.0359147, m.Articulation.LeftBogieAngle);
                Assert.AreEqual(-0.0323853, m.Articulation.RightBogieAngle);
                Assert.AreEqual(0.0275015, m.Articulation.LeftRockerAngle);
                Assert.AreEqual(6.20445, m.Articulation.MastAzimuth);
                Assert.AreEqual(1.97266, m.Articulation.MastElevation);
            }

            {
                string filename = @"FLB_509619692RAS_T0530000FHAZ00323M_.IMG";
                var m = new PDSMetadata(filename);
                Assert.AreEqual(m.Width, 64);
                Assert.AreEqual(m.Height, 64);
                Assert.AreEqual(m.Bands, 1);

                Assert.AreEqual(typeof(CAHVORE), m.CameraModel.GetType());
                var cm = (CAHVORE)m.CameraModel;
                Vector3 mc_1 = new Vector3(1.03304, -0.17145, -0.707908);
                Vector3 mc_2 = new Vector3(0.706662, 0.00032, 0.707551);
                Vector3 mc_3 = new Vector3(21.9636, 28.3687, 21.6746);
                Vector3 mc_4 = new Vector3(2.37507, 0.231257, 42.5149);
                Vector3 mc_5 = new Vector3(0.704793, -0.000541, 0.709413);
                Vector3 mc_6 = new Vector3(8.0e-06, -0.013391, -0.007308);
                Vector3 mc_7 = new Vector3(0.001868, 0.001572, 0.001665);


                Assert.IsTrue(Vector3.AlmostEqual(mc_1, cm.C));
                Assert.IsTrue(Vector3.AlmostEqual(mc_2, cm.A));
                Assert.IsTrue(Vector3.AlmostEqual(mc_3, cm.H));
                Assert.IsTrue(Vector3.AlmostEqual(mc_4, cm.V));
                Assert.IsTrue(Vector3.AlmostEqual(mc_5, cm.O));
                Assert.IsTrue(Vector3.AlmostEqual(mc_6, cm.R));
                Assert.IsTrue(Vector3.AlmostEqual(mc_7, cm.E));
                Assert.AreNotEqual(LinearityMode.Perspective, cm.linearityMode);
                Assert.AreNotEqual(LinearityMode.Fisheye, cm.linearityMode);
                Assert.AreEqual(0.37, cm.linearityMode.Linearity);
            }
        }
    }
}
