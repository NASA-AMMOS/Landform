using System.IO;
using System.Linq;
using System.Collections.Generic;
using JPLOPS.Imaging;
using JPLOPS.Pipeline;
using JPLOPS.Pipeline.AlignmentServer;
using JPLOPS.Util;

namespace PipelineTest
{
    public class RoverObservationComparatorTests
    {
        [Fact]
        public void KeepBestRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filenameLin = "NRB_449704993RASLM0301254NCAM00536M_.IMG";  //Doesn't exist.
            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M_.IMG";  //Doesn't exist.

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Framen", root, false);

            RoverObservation obsLin = RoverObservation.Create(pipeline, frame, filenameLin, filenameLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, filenameNonLin, filenameNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.True(obsLin.IsLinear);
            Assert.True(!obsNonLin.IsLinear);

            RoverObservationComparator comp = new RoverObservationComparator(new MissionMSL());

            List<Observation> allObs = new List<Observation>(2) { obsLin, obsNonLin };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Best);
            Assert.True(result.Count() == 1);
            Assert.True(result.First() == obsNonLin);

            allObs = new List<Observation>(2) {obsNonLin, obsLin };
            result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Best);
            Assert.True(result.Count() == 1);
            Assert.True(result.First() == obsNonLin);

        }

        [Fact]
        public void KeepBothRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filenameLin = "NRB_449704993RASLM0301254NCAM00536M_.IMG";  //Doesn't exist.
            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M_.IMG";  //Doesn't exist.

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Framen", root, false);

            RoverObservation obsLin = RoverObservation.Create(pipeline, frame, filenameLin, filenameLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, filenameNonLin, filenameNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.True(obsLin.IsLinear);
            Assert.True(!obsNonLin.IsLinear);

            RoverObservationComparator comp = new RoverObservationComparator(new MissionMSL());

            List<Observation> allObs = new List<Observation>(2) { obsLin, obsNonLin };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            Assert.True(result.Count() == 2);
            Assert.True(result.First() == obsNonLin);

            allObs = new List<Observation>(2) { obsNonLin, obsLin };
            result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            Assert.True(result.Count() == 2);
            Assert.True(result.First() == obsNonLin);

        }

        [Fact]
        public void KeepBestDifferBeforeLinearRoverObservationsTest()
        {
            // prepare input data
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "KeepBestRoverObservationsTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(), config);

            string filenameLin = "NRB_449704993RASLM0301254NCAM00536M_.IMG";  //Doesn't exist.
            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M_.IMG";  //Doesn't exist.

            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame = Frame.Create(pipeline, projectName, "Frame", root, false);

            RoverObservation obsLinBW = RoverObservation.Create(pipeline, frame, filenameLin, filenameLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsLinCol = RoverObservation.Create(pipeline, frame, filenameLin, filenameLin, new CAHV(),
                                                  true, true, true, 1024, 1024, 3, 16, 609, 1, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.FullColor, false);

            RoverObservation obsNonLin = RoverObservation.Create(pipeline, frame, filenameNonLin, filenameNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 3, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.True(obsLinBW.IsLinear);
            Assert.True(obsLinCol.IsLinear);
            Assert.True(!obsNonLin.IsLinear);

            Assert.True(obsLinBW.Color == RoverProductColor.Grayscale);
            Assert.True(obsLinCol.Color == RoverProductColor.FullColor);
            Assert.True(obsNonLin.Color == RoverProductColor.Grayscale);

            RoverObservationComparator comp = new RoverObservationComparator(new MissionMSL());

            List<Observation> allObs = new List<Observation>(3) { obsLinBW, obsNonLin, obsLinCol };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Best);
            Assert.True(result.Count() == 1);
            Assert.True(result.First() == obsLinCol);
        }

        [Fact]
        public void KeepBothDifferAfterLinearRoverObservationsTest()
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

            string filenameLin1 = "NRB_449704993RASLM0301254NCAM00536M1.IMG";  //Doesn't exist.
            string filenameLin2 = "NRB_449704993RASLM0301254NCAM00536M2.IMG";  //Doesn't exist.
            string filenameNonLin = "NRB_449704993RAS_M0301254NCAM00536M1.IMG";  //Doesn't exist.

            RoverObservation obsLinV1 = RoverObservation.Create(pipeline, frame, filenameLin1, filenameLin1, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 1, 1, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsLinV2 = RoverObservation.Create(pipeline, frame, filenameLin2, filenameLin2, new CAHV(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 2, 2, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            RoverObservation obsNonLinV2 = RoverObservation.Create(pipeline, frame, filenameNonLin, filenameNonLin, new CAHVORE(),
                                                  true, true, true, 1024, 1024, 1, 16, 609, 2, 3, 31, 1330,
                                                  RoverProductType.Image, RoverProductCamera.NavcamLeft, RoverProductProducer.OPGS,
                                                  RoverProductColor.Grayscale, false);

            Assert.True(obsLinV1.IsLinear);
            Assert.True(obsLinV2.IsLinear);
            Assert.True(!obsNonLinV2.IsLinear);
          
            RoverObservationComparator comp = new RoverObservationComparator(new MissionMSL());

            List<Observation> allObs = new List<Observation>(3) { obsLinV1, obsNonLinV2, obsLinV2 };
            var result = comp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            Assert.True(result.Count() == 2);
            Assert.True(result.First() == obsNonLinV2);
            Assert.True(result.ElementAt(1) == obsLinV2);
        }

        [Fact]
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
                return RoverObservation.Create(pipeline, frame, filename, filename, cmod,
                                               true, true, true, 1024, 1024, 1, 16, 609, 1, 2, 31, 1330,
                                               RoverProductType.Image, RoverProductCamera.NavcamLeft,
                                               producer, RoverProductColor.FullColor, false);
            }

            //producer trumps linearness
            string filenameLin = "0609ML0025670000301546E01_DRCX.IMG";  //msss name for the same image
            string filenameNonLin = "MLF_451556453RAS_S0311256MCAM02567M1.IMG"; //opgs name for the same image
            
            var obsLinMSSS = makeObs(filenameLin, new CAHV(), RoverProductProducer.MSSS);
            var obsNonLinOPGS = makeObs(filenameNonLin, new CAHVORE(), RoverProductProducer.OPGS);
            
            var linComp = new RoverObservationComparator(new MissionMSL());
            
            var allObs = new List<Observation>(3) { obsNonLinOPGS, obsLinMSSS };
            
            var result = linComp.KeepBestRoverObservations(allObs, RoverObservationComparator.LinearVariants.Both);
            
            Assert.True(result.Count() == 1);
            Assert.True(result.First() == obsNonLinOPGS);
        }
    }
}
