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

            string filenameLin = Path.Combine("TestData", "img", "dummy.IMG");  //Doesn't exist.
            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Framen", root, false);

            RoverObservation obsLin = RoverObservation.Create(pipeline, frame, "ObsLin", filenameLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, "ObsNonLin", filenameLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLin.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);

            RoverObservationComparator comp = new RoverObservationComparator(preferMSSS: false, preferLinear: true, preferColor: true,
                                          preferEyeForGeometry: RoverStereoEye.Left);

            List<Observation> allObs = new List<Observation>(2) { obsLin, obsNonLin };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.KeepLinearVariants.Best);
            Assert.IsTrue(result.Count() == 1);
            Assert.IsTrue(result.First() == obsLin);

            allObs = new List<Observation>(2) {obsNonLin, obsLin };
            result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.KeepLinearVariants.Best);
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

            string filenameLin = Path.Combine("TestData", "img", "dummy.IMG"); //Doesn't exist.
            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Framen", root, false);

            RoverObservation obsLin = RoverObservation.Create(pipeline, frame, "ObsLin", filenameLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, "ObsNonLin", filenameLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLin.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);

            RoverObservationComparator comp = new RoverObservationComparator(preferMSSS: false, preferLinear: true, preferColor: true,
                                          preferEyeForGeometry: RoverStereoEye.Left);

            List<Observation> allObs = new List<Observation>(2) { obsLin, obsNonLin };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.KeepLinearVariants.Both);
            Assert.IsTrue(result.Count() == 2);
            Assert.IsTrue(result.First() == obsLin);

            allObs = new List<Observation>(2) { obsNonLin, obsLin };
            result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.KeepLinearVariants.Both);
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

            string filename = Path.Combine("TestData", "img", "dummy.IMG");
         
            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Frame", root, false);

            RoverObservation obsLinBW = RoverObservation.Create(pipeline, frame, "ObsLinBW", filename, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsLinCol = RoverObservation.Create(pipeline, frame, "ObsLinCol", filename, new CAHV(),
                                                  true, true, true, 1024, 1024, 3, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.FullColor, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, "ObsNonLin", filename, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 3, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLinBW.IsLinear);
            Assert.IsTrue(obsLinCol.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);

            Assert.IsTrue(obsLinBW.Color == RoverProductColor.Grayscale);
            Assert.IsTrue(obsLinCol.Color == RoverProductColor.FullColor);
            Assert.IsTrue(obsNonLin.Color == RoverProductColor.Grayscale);

            RoverObservationComparator comp = new RoverObservationComparator(preferMSSS: false, preferLinear: true, preferColor: true,
                                          preferEyeForGeometry: RoverStereoEye.Left);

            List<Observation> allObs = new List<Observation>(3) { obsLinBW, obsNonLin, obsLinCol };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.KeepLinearVariants.Best);
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

            RoverObservation obsLinV1 = RoverObservation.Create(pipeline, frame, "ObsLinV1", filename, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsLinV2 = RoverObservation.Create(pipeline, frame, "ObsLinV2", filename, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 2, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, "ObsNonLin", filename, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 3, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.IsTrue(obsLinV1.IsLinear);
            Assert.IsTrue(obsLinV2.IsLinear);
            Assert.IsTrue(!obsNonLin.IsLinear);
          
            RoverObservationComparator comp = new RoverObservationComparator(preferMSSS: false, preferLinear: true, preferColor: true,
                                          preferEyeForGeometry: RoverStereoEye.Left);

            List<Observation> allObs = new List<Observation>(3) { obsLinV1, obsNonLin, obsLinV2 };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.KeepLinearVariants.Both);
            Assert.IsTrue(result.Count() == 2);
            Assert.IsTrue(result.First() == obsLinV2);
            Assert.IsTrue(result.ElementAt(1) == obsNonLin);
        }
    }
}