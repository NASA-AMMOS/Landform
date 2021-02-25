using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Imaging;
using OPS.Pipeline;
using OPS.Pipeline.AlignmentServer;
using OPS.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace RoverTest
{
    [TestClass()]
    public class RoverObservationComparatorTests
    {
        [TestMethod()]
        public void KeepBestRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filenameLin = "NRB_449704993RASLM0301254NCAM00536M_.IMG";
            string filePathLin = Path.Combine("TestData", "img", filenameLin);  //Doesn't exist.

            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M_.IMG";
            string filePathNonLin = Path.Combine("TestData", "img", filenameLin);  //Doesn't exist.

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Framen", root, false);

            RoverObservation obsLin = RoverObservation.Create(pipeline, frame, filenameLin, filePathLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, filenameNonLin, filePathNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLin.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);

            RoverObservationComparator comp = new RoverObservationComparator(preferOPGS: true,
                                                                             preferLinearGeometryProducts: true,
                                                                             preferLinearRasterProducts: true,
                                                                             preferColor: true,
                                                                             preferEyeForGeometry: RoverStereoEye.Left,
                                                                             mission:new MissionMSL());

            List<Observation> allObs = new List<Observation>(2) { obsLin, obsNonLin };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Best);
            Assert.IsTrue(result.Count() == 1);
            Assert.IsTrue(result.First() == obsLin);

            allObs = new List<Observation>(2) {obsNonLin, obsLin };
            result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Best);
            Assert.IsTrue(result.Count() == 1);
            Assert.IsTrue(result.First() == obsLin);

        }

        [TestMethod()]
        public void KeepBothRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filenameLin = "NRB_449704993RASLM0301254NCAM00536M_.IMG";
            string filePathLin = Path.Combine("TestData", "img", filenameLin);  //Doesn't exist.

            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M_.IMG";
            string filePathNonLin = Path.Combine("TestData", "img", filenameNonLin);  //Doesn't exist.

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Framen", root, false);

            RoverObservation obsLin = RoverObservation.Create(pipeline, frame, filenameLin, filePathLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, filenameNonLin, filePathNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLin.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);

            RoverObservationComparator comp = new RoverObservationComparator(preferOPGS: true,
                                                                             preferLinearGeometryProducts: true,
                                                                             preferLinearRasterProducts: true,
                                                                             preferColor: true,
                                                                             preferEyeForGeometry: RoverStereoEye.Left,
                                                                             mission: new MissionMSL());

            List<Observation> allObs = new List<Observation>(2) { obsLin, obsNonLin };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            Assert.IsTrue(result.Count() == 2);
            Assert.IsTrue(result.First() == obsLin);

            allObs = new List<Observation>(2) { obsNonLin, obsLin };
            result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            Assert.IsTrue(result.Count() == 2);
            Assert.IsTrue(result.First() == obsLin);

        }

        [TestMethod()]
        public void KeepBestDifferBeforeLinearRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filenameLin = "NRB_449704993RASLM0301254NCAM00536M_.IMG";
            string filePathLin = Path.Combine("TestData", "img", filenameLin);  //Doesn't exist.

            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M_.IMG";
            string filePathNonLin = Path.Combine("TestData", "img", filenameNonLin);  //Doesn't exist.

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Frame", root, false);

            RoverObservation obsLinBW = RoverObservation.Create(pipeline, frame, filenameLin, filePathLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsLinCol = RoverObservation.Create(pipeline, frame, filenameLin, filePathLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 3, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.FullColor, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, filenameNonLin, filePathNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 3, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLinBW.IsLinear);
            Assert.IsTrue(obsLinCol.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);

            Assert.IsTrue(obsLinBW.Color == RoverProductColor.Grayscale);
            Assert.IsTrue(obsLinCol.Color == RoverProductColor.FullColor);
            Assert.IsTrue(obsNonLin.Color == RoverProductColor.Grayscale);

            RoverObservationComparator comp = new RoverObservationComparator(preferOPGS: true,
                                                                             preferLinearGeometryProducts: true,
                                                                             preferLinearRasterProducts: true,
                                                                             preferColor: true,
                                                                             preferEyeForGeometry: RoverStereoEye.Left,
                                                                             mission:new MissionMSL());

            List<Observation> allObs = new List<Observation>(3) { obsLinBW, obsNonLin, obsLinCol };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Best);
            Assert.IsTrue(result.Count() == 1);
            Assert.IsTrue(result.First() == obsLinCol);
        }

        [TestMethod()]
        public void KeepBothDifferAfterLinearRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filename = Path.Combine("TestData", "img", "dummy.IMG");

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Frame", root, false);

            string filenameLin1 = "NRB_449704993RASLM0301254NCAM00536M1.IMG";
            string filePathLin1 = Path.Combine("TestData", "img", filenameLin1);  //Doesn't exist.

            string filenameLin2 = "NRB_449704993RASLM0301254NCAM00536M2.IMG";
            string filePathLin2 = Path.Combine("TestData", "img", filenameLin2);  //Doesn't exist.

            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M1.IMG";
            string filePathNonLin = Path.Combine("TestData", "img", filenameNonLin);  //Doesn't exist.

            RoverObservation obsLinV1 = RoverObservation.Create(pipeline, frame, filenameLin1, filePathLin1, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsLinV2 = RoverObservation.Create(pipeline, frame, filenameLin2, filePathLin2, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 2, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLinV2 = RoverObservation.Create(pipeline, frame, filenameNonLin, filePathNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 2, 3, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLinV1.IsLinear);
            Assert.IsTrue(obsLinV2.IsLinear);
            Assert.IsTrue(!obsNonLinV2.IsLinear);
          
            RoverObservationComparator comp = new RoverObservationComparator(preferOPGS: true,
                                                                             preferLinearGeometryProducts: true,
                                                                             preferLinearRasterProducts: true,
                                                                             preferColor: true,
                                                                             preferEyeForGeometry: RoverStereoEye.Left,
                                                                             mission:new MissionMSL());

            List<Observation> allObs = new List<Observation>(3) { obsLinV1, obsNonLinV2, obsLinV2 };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            Assert.IsTrue(result.Count() == 2);
            Assert.IsTrue(result.First() == obsLinV2);
            Assert.IsTrue(result.ElementAt(1) == obsNonLinV2);
        }

        [TestMethod()]
        public void TestDiffProducersRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Frame", root, false);

            RoverObservation makeObs(string filename, CameraModel cmod, RoverProductProducer producer)
            {
                string filePath = Path.Combine("TestData", "img", filename);  //Doesn't exist.
                return RoverObservation.Create(pipeline, frame, filename, filePath, cmod,
                                               true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                               RoverProductType.Image, RoverProductCamera.NavcamLeft,
                                               producer, RoverProductColor.FullColor, false);
            }

            //producer trumps linearness
            string filenameLin = "0609ML0025670000301546E01_DRCX.IMG";  //msss name for the same image
            string filenameNonLin = "MLF_451556453RAS_S0311256MCAM02567M1.IMG"; //opgs name for the same image
            
            var obsLinMSSS = makeObs(filenameLin, new CAHV(), RoverProductProducer.MSSS);
            var obsNonLinOPGS = makeObs(filenameNonLin, new CAHVORE(), RoverProductProducer.OPGS);
            
            var linComp = new RoverObservationComparator(preferOPGS: true,
                                                         preferLinearGeometryProducts: true,
                                                         preferLinearRasterProducts: true,
                                                         preferColor: true,
                                                         preferEyeForGeometry: RoverStereoEye.Left,
                                                         mission: new MissionMSL());
            
            var allObs = new List<Observation>(3) { obsNonLinOPGS, obsLinMSSS };
            
            var result = linComp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            
            Assert.IsTrue(result.Count() == 1);
            Assert.IsTrue(result.First() == obsNonLinOPGS);
        }
    }
}
