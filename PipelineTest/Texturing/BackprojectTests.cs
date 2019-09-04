using Microsoft.VisualStudio.TestTools.UnitTesting;
using OPS.Pipeline;
using OPS.Imaging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xna.Framework;
using System.IO;
using OPS.Pipeline.AlignmentServer;
using OPS.Util;

namespace PipelineTest
{
    [TestClass()]
    public class BackprojectTests
    {
        [TestMethod()]
        public void FillIndexImageTest()
        {
            // prepare input data
            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions());

            string filename1 = Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG");
            string filename2 = Path.Combine("TestData", "img", "NLB_451557756RASLF0311330NCAM00353M1.IMG");
            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame1 = Frame.Create(pipeline, projectName, "Frame1", root, false);
            Frame frame2 = Frame.Create(pipeline, projectName, "Frame2", root, false);

            Observation obs1 = Observation.Create(pipeline, new Frame(), "Obs1", filename1, ObservationType.Image.ToString(), JsonHelper.ToJson(new CAHV()),
                              true, 1408, 1200, 3, 8, 606, 1, false);

            Observation obs2 = Observation.Create(pipeline, frame2, "Obs2", filename2, ObservationType.Image.ToString(), JsonHelper.ToJson(new CAHV()),
                             true, 1024, 1024, 1, 16, 609, 2, false);


            Dictionary<Pixel, Backproject.ObsPixel> backprojectResults = new Dictionary<Pixel, Backproject.ObsPixel>();
            //0,0 skipped 
            backprojectResults.Add(new Pixel(0, 1), new Backproject.ObsPixel { Obs = obs1, Pixel = new Vector2(34, 45) });
            backprojectResults.Add(new Pixel(1, 0), new Backproject.ObsPixel { Obs = obs2, Pixel = new Vector2(56, 78) });
            //1,1 skipped

            //run code
            Image outputImage = new Image(3, 2, 2);
            Backproject.FillIndexImage(backprojectResults, outputImage);

            //validate output
            Assert.IsTrue(outputImage[0, 0, 0] == 0 && outputImage[1, 0, 0] == 0 && outputImage[2, 0, 0] == 0);
            Assert.IsTrue(outputImage[0, 0, 1] == 1 && outputImage[1, 0, 1] == 45 && outputImage[2, 0, 1] == 34);
            Assert.IsTrue(outputImage[0, 1, 0] == 2 && outputImage[1, 1, 0] == 78 && outputImage[2, 1, 0] == 56);
            Assert.IsTrue(outputImage[0, 1, 1] == 0 && outputImage[1, 1, 1] == 0 && outputImage[2, 1, 1] == 0);

        }

        [TestMethod()]
        public void FillOutputTextureTest()
        {
            LocalPipelineConfig config = new LocalPipelineConfig();
            config.Venue = "FillOutputTextureTest";
            config.StorageDir = StringHelper.NormalizeUrl(".", "file://");
            config.MaxCores = 1;
            config.RandomSeed = 0;

            LocalPipeline pipeline = new LocalPipeline(new PipelineCoreOptions(),config);

            string filename1 = Path.Combine("TestData", "img", "ML0_451292526RCX_S0311094MCAM02555M1.IMG");
            string filename2 = Path.Combine("TestData", "img", "NLB_451557756RASLF0311330NCAM00353M1.IMG");
            string projectName = "unittest";

            Frame root = new Frame();
            Frame frame1 = Frame.Create(pipeline, projectName, "Frame1", root, false);
            Frame frame2 = Frame.Create(pipeline, projectName, "Frame2", root, false);

            Observation obs1 = Observation.Create(pipeline, frame1, "Obs1", filename1, ObservationType.Image.ToString(), JsonHelper.ToJson(new CAHV()),
                              true, 1408, 1200, 3, 8, 606, 1, false);

            Observation obs2 = Observation.Create(pipeline, frame2, "Obs2", filename2, ObservationType.Image.ToString(), JsonHelper.ToJson(new CAHV()),
                             true, 1024, 1024, 1, 16, 609, 2, false);

            Dictionary<Pixel, Backproject.ObsPixel> results = new Dictionary<Pixel, Backproject.ObsPixel>();
            
            for(int idxRow = 0; idxRow < 64; idxRow++)
            {
                for (int idxCol = 0; idxCol < 64; idxCol++)
                {
                    results.Add(new Pixel(idxRow, idxCol), new Backproject.ObsPixel() { Obs=obs1, Pixel = new Vector2(idxCol, idxRow) });
                    results.Add(new Pixel(idxRow, idxCol+64), new Backproject.ObsPixel() { Obs=obs2, Pixel = new Vector2(idxCol, idxRow) });
                }
            }

            //allocate output image
            Image outputImage = new Image(3, 128, 64);
            Backproject.FillOutputTexture(pipeline, results, outputImage, false);

            Image img1 = pipeline.LoadImage(filename1);
            Image img2 = pipeline.LoadImage(filename2);

            for (int idxRow = 0; idxRow < 64; idxRow++)
            {
                for (int idxCol = 0; idxCol < 64; idxCol++)
                {
                    for(int idxBand=0; idxBand < 3; idxBand++)
                    {
                        Assert.IsTrue(outputImage[idxBand, idxRow, idxCol] == img1[idxBand, idxRow, idxCol]);
                        Assert.IsTrue(outputImage[idxBand, idxRow, idxCol + 64] == img2[0, idxRow, idxCol]);
                    }
                }
            }
        }
    }
}