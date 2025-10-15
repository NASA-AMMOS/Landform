using System.IO;
using Microsoft.Xna.Framework;
using JPLOPS.Imaging;
using JPLOPS.Pipeline;

namespace PipelineTest
{
    public class PDSParserTest
    {
        [Fact]
        public void PDSParserBase()
        {
            {
                string filename = "ML0_451292526RCX_S0311094MCAM02555M1.IMG";
                var md = new PDSMetadata(filename);
                var m = new PDSParser(md);

                Assert.Equal(2024, m.ProductCreationTime.Year);
                Assert.Equal(5, m.ProductCreationTime.Month);
                Assert.Equal(9, m.ProductCreationTime.Day);
                Assert.Equal(1, m.FirstLine);
                Assert.Equal(129, m.FirstSample);
                Assert.Equal("0606ML0025550030301440E01_DRCX", m.ProductIdString);

                var mission = MissionSpecific.GetInstance(Mission.MSL);
                Assert.Equal(RoverProductCamera.MastcamLeft, mission.GetCamera(m));
                Assert.Equal(RoverProductGeometry.Raw, m.GeometricProjection);
                Assert.Equal(RoverProductSize.Regular, m.ImageSizeType);
                Assert.Equal(RoverProductType.Image, mission.GetProductType(m));
                Assert.Equal(RoverProductProducer.MSSS, m.ProducingInstitution);
                Assert.Equal(10.1, m.ExposureDuration);
                Assert.Equal(0, m.FilterNumber);
                Assert.Equal(2.9, mission.GetMaximumFocusDistance(md));
                Assert.Equal(451292530.0000, m.SpacecraftClock);
                Assert.Equal(new Quaternion(-0.0452929, 0.0026977, -0.9728553, 0.2269226), m.RoverOriginRotation);
                Assert.Equal(new Vector3(-78.980461, -44.499561, 2.310069), m.OriginOffset);
                var rmc = new int[] { 31, 1094, 52, 106, 0, 0, 468, 268, 0, 0 };
                for (int i = 0; i < rmc.Length; i++)
                {
                    Assert.Equal(rmc[i], m.MotionCounter[i]);
                }
                Assert.Equal("0311094", m.SiteDrive);
                Assert.Equal(606, m.PlanetDayNumber);

                var articulation = (MSLRoverArticulation)(new MSLRoverArticulationParser(md).Parse());

                Assert.Equal(1.568154, articulation.ArmAngle1);
                Assert.Equal(-0.277720, articulation.ArmAngle2);
                Assert.Equal(-2.825491, articulation.ArmAngle3);
                Assert.Equal(3.116510, articulation.ArmAngle4);
                Assert.Equal(0.593527, articulation.ArmAngle5);

                Assert.Equal(0.049941, articulation.LeftBogieAngle);
                Assert.Equal(-0.000753, articulation.RightBogieAngle);
                Assert.Equal(0.0038070471491664648, articulation.LeftRockerAngle);
                Assert.Equal(5.771630, articulation.MastAzimuth);
                Assert.Equal(0.631446, articulation.MastElevation);

                Assert.Equal(0.30104660668874589, m.HorizontalFOV);
                Assert.Equal(0.25710096145278072, m.VerticalFOV);
            }

            {
                string filename = "NLB_451649560RNGLF0311330NCAM12813M1.IMG";
                var md = new PDSMetadata(filename);
                var m = new PDSParser(md);

                Assert.Equal(2014, m.ProductCreationTime.Year);
                Assert.Equal(8, m.ProductCreationTime.Month);
                Assert.Equal(22, m.ProductCreationTime.Day);
                Assert.Equal(1, m.FirstLine);
                Assert.Equal(1, m.FirstSample);
                Assert.Equal("NLB_451649560RNGLF0311330NCAM12813M1", m.ProductIdString);
                var mission = MissionSpecific.GetInstance(Mission.MSL);
                Assert.Equal(RoverProductCamera.NavcamLeft, mission.GetCamera(m));

                Assert.Equal(RoverProductGeometry.Linearized, m.GeometricProjection);
                Assert.Equal(RoverProductSize.Regular, m.ImageSizeType);
                Assert.Equal(RoverProductType.Range, mission.GetProductType(m));
                Assert.Equal(RoverProductProducer.OPGS, m.ProducingInstitution);
                Assert.Equal(353.28, m.ExposureDuration);
               
                Assert.Equal(451649560.213, m.SpacecraftClock);
                Assert.Equal(new Quaternion(0.0252917, 0.0771217, -0.460822, 0.883773), m.RoverOriginRotation);
                Assert.Equal(new Vector3(-85.4875, -59.027, 1.74495), m.OriginOffset);
                var rmc = new int[] { 31, 1330, 6, 0, 0, 0, 146, 138, 0, 0 };
                for (int i = 0; i < rmc.Length; i++)
                {
                    Assert.Equal(rmc[i], m.MotionCounter[i]);
                }
                Assert.Equal("0311330", m.SiteDrive);
                Assert.Equal(610, m.PlanetDayNumber);

                var articulation = (MSLRoverArticulation)(new MSLRoverArticulationParser(md).Parse());

                Assert.Equal(1.56815, articulation.ArmAngle1);
                Assert.Equal(-0.27772, articulation.ArmAngle2);
                Assert.Equal(-2.82549, articulation.ArmAngle3);
                Assert.Equal(3.11651, articulation.ArmAngle4);
                Assert.Equal(0.593527, articulation.ArmAngle5);

                Assert.Equal(-0.0359147, articulation.LeftBogieAngle);
                Assert.Equal(-0.0323853, articulation.RightBogieAngle);
                Assert.Equal(0.0275015, articulation.LeftRockerAngle);
                Assert.Equal(6.20445, articulation.MastAzimuth);
                Assert.Equal(1.97266, articulation.MastElevation);
                Assert.Equal(PDSParser.ReferenceCoordinateFrame.RoverNav, m.CameraModelRefFrame);
                Assert.Equal(PDSParser.ReferenceCoordinateFrame.Site, m.DerivedImageRefFrame);

                Assert.Equal(MathHelper.ToRadians(45.3641), m.HorizontalFOV);
                Assert.Equal(MathHelper.ToRadians(45.3596), m.VerticalFOV);
            }

            {
                string filename = "0608ML0025660260301542E01_DRCX.IMG";
                var md = new PDSMetadata(filename);
                var m = new PDSParser(md);

                Assert.Equal(2024, m.ProductCreationTime.Year);
                Assert.Equal(5, m.ProductCreationTime.Month);
                Assert.Equal(9, m.ProductCreationTime.Day);
                Assert.Equal(385, m.FirstLine);
                Assert.Equal(305, m.FirstSample);
                Assert.Equal("0608ML0025660260301542E01_DRCX", m.ProductIdString);

                var mission = MissionSpecific.GetInstance(Mission.MSL);
                Assert.Equal(RoverProductCamera.MastcamLeft, mission.GetCamera(m));
                Assert.Equal(RoverProductGeometry.Raw, m.GeometricProjection);
                Assert.Equal(RoverProductSize.Regular, m.ImageSizeType);
                Assert.Equal(RoverProductType.Image, mission.GetProductType(m));
                Assert.Equal(RoverProductProducer.MSSS, m.ProducingInstitution);

                Assert.Equal(451471168.0000, m.SpacecraftClock);
                Assert.Equal(new Quaternion(0.0233355, 0.0664593, -0.3743627, 0.9246033), m.RoverOriginRotation);
                Assert.Equal(new Vector3(-86.509682, -57.647167, 1.966438), m.OriginOffset);
                var rmc = new int[] { 31, 1256, 10, 0, 0, 0, 552, 250, 0, 0 };
                for (int i = 0; i < rmc.Length; i++)
                {
                    Assert.Equal(rmc[i], m.MotionCounter[i]);
                }
                Assert.Equal("0311256", m.SiteDrive);
                Assert.Equal(608, m.PlanetDayNumber);

                var articulation = (MSLRoverArticulation)(new MSLRoverArticulationParser(md).Parse());

                Assert.Equal(1.568154, articulation.ArmAngle1);
                Assert.Equal(-0.277720, articulation.ArmAngle2);
                Assert.Equal(-2.825491, articulation.ArmAngle3);
                Assert.Equal(3.116510, articulation.ArmAngle4);
                Assert.Equal(0.593527, articulation.ArmAngle5);

                Assert.Equal(-0.021033, articulation.LeftBogieAngle);
                Assert.Equal(0.007730, articulation.RightBogieAngle);
                Assert.Equal(-0.013007268309593201, articulation.LeftRockerAngle);
                Assert.Equal(4.092333, articulation.MastAzimuth);
                Assert.Equal(1.127138, articulation.MastElevation);

                Assert.Equal(0.24690300263337783, m.HorizontalFOV);
                Assert.Equal(0.093003359851021844, m.VerticalFOV);
            }
        }
    }
}
