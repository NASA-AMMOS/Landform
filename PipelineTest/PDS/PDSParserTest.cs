using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using OPS.Pipeline;
using Microsoft.Xna.Framework;

namespace PipelineTest
{
    [TestClass]
    public class PDSParserTest
    {
        [TestMethod]
        [DeploymentItem("TestData", ".")]
        [DeploymentItem("gdal", "gdal")]
        public void PDSParser()
        {
            {                
                string filename = @"ML0_451292526RCX_S0311094MCAM02555M1.IMG";
                var m = new PDSParser( new PDSMetadata(filename));

                var img = Image.Load(filename);

                Assert.AreEqual(2014, m.ProductCreationTime.Year);
                Assert.AreEqual(4, m.ProductCreationTime.Month);
                Assert.AreEqual(22, m.ProductCreationTime.Day);
                Assert.AreEqual(1, m.FirstLine);
                Assert.AreEqual(129, m.FirstSample);
                Assert.AreEqual("0606ML0025550030301440E01_DRCX", m.ProductId.filename);
                Assert.AreEqual(PDSInstrument.MastcamLeft, m.Instrument);
                Assert.AreEqual(PDSGeometryProjection.Raw, m.GeometricProjection);
                Assert.AreEqual(PDSImageSizeType.Regular, m.ImageSizeType);
                Assert.AreEqual(PDSDerivedImageType.Image, m.DerivedImageType);
                Assert.AreEqual(PDSInstitution.MSSS, m.ProducingInstitution);
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
                string filename = @"NLB_451649560RNGLF0311330NCAM12813M1.IMG";
                var m = new PDSParser(new PDSMetadata(filename));
                Assert.AreEqual(2014, m.ProductCreationTime.Year);
                Assert.AreEqual(4, m.ProductCreationTime.Month);
                Assert.AreEqual(27, m.ProductCreationTime.Day);
                Assert.AreEqual(1, m.FirstLine);
                Assert.AreEqual(1, m.FirstSample);
                Assert.AreEqual("NLB_451649560RNGLF0311330NCAM12813M1", m.ProductId.filename);
                Assert.AreEqual(PDSInstrument.NavcamLeft, m.Instrument);
                Assert.AreEqual(PDSGeometryProjection.Linearized, m.GeometricProjection);
                Assert.AreEqual(PDSImageSizeType.Regular, m.ImageSizeType);
                Assert.AreEqual(PDSDerivedImageType.Range, m.DerivedImageType);
                Assert.AreEqual(PDSInstitution.OPGS, m.ProducingInstitution);
                Assert.AreEqual(353.28, m.ExposureDuration);
               
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

        }
    }


}
