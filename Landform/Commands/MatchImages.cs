using CommandLine;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Emgu.CV;
using Emgu.CV.Structure;
using Emgu.CV.CvEnum;
using Microsoft.Xna.Framework;
using Emgu.CV.XFeatures2D;
using Emgu.CV.Util;
using Emgu.CV.Features2D;
using System.Drawing;
using Emgu.Util;
using OPS.Imaging.Emgu;
using OPS.Alignment;
using System.Diagnostics;

namespace OPS.Pipeline
{

    [Verb("matchimages", HelpText = "")]
    public class MatchImagesOptions
    {
        [Value(0, Required = false, HelpText = "")]
        public string ImageA { get; set; }

        [Value(1, Required = false, HelpText = "")]
        public string ImageB { get; set; }

        [Option('p', "patches", Required = false, HelpText = "Indicate saving location for gathered patches")]
        public string PatchFile { get; set; }

        [Option('t', "train", Required = false, HelpText = "Indicate directory of training images")]
        public string TrainingPath { get; set; }

        [Option('o', "output", Required = false, HelpText = "Indicate output image file for image matches")]
        public string OutputFile { get; set; }

        [Option('e', "eigenspace", Required = false, HelpText = "Indicate saving location for computed eigenspace")]
        public string TrainingFile { get; set; }

    }

    public class MatchImages
    {
        public MatchImagesOptions options;
        public MatchImages(MatchImagesOptions options)
        {
            this.options = options;
        }

        public int Run()
        {
            string imageFileA = options.ImageA;
            string imageFileB = options.ImageB;

            string outputFile = options.OutputFile;
            string trainingPath = options.TrainingPath;
            string trainingFile = options.TrainingFile;

            int patchsize = 41;

            //Image<Gray, byte> modelImageA = Imaging.Image.Load(imageFileA).ToEmguGrayscale();
            //Image<Gray, float> grayModelImageA = modelImageA.Convert<Gray, float>();

            //Image<Gray, byte> modelImageB = Imaging.Image.Load(imageFileB).ToEmguGrayscale();
            //Image<Gray, float> grayModelImageB = modelImageB.Convert<Gray, float>();

            string gpcafile = options.TrainingFile;

            // 0. Computes eigenspace from set of training images.
            if (trainingPath != null)
            {
                if (trainingFile == null) {  trainingFile = trainingPath + ".txt"; }
                Trace.WriteLine("Training...");
                PCA_Train train = new PCA_Train(trainingFile);
                train.Train(trainingPath);
                Trace.WriteLine("Trained.");
                return 0;
            }

            // 1. Calculate keypoints of an image using SIFT detection
            //SIFT siftCPU = new SIFT();
            //MKeyPoint[] mKeypointsA = siftCPU.Detect(modelImageA);
            //MKeyPoint[] mKeypointsB = siftCPU.Detect(modelImageB);
            //List<PCA_SIFTFeature> keypointsA = PCA_KeypointDetector.GetPatches(grayModelImageA, mKeypointsA, patchsize);
            //List<PCA_SIFTFeature> keypointsB = PCA_KeypointDetector.GetPatches(grayModelImageB, mKeypointsB, patchsize);
            //VectorOfKeyPoint vokpA = new VectorOfKeyPoint(mKeypointsA);
            //VectorOfKeyPoint vokpB = new VectorOfKeyPoint(mKeypointsB);

            // 2. Recalculate keypoints, given the eigenspace, an image, and its keypoints  
            //PCA_KeypointDetector detector = new PCA_KeypointDetector(gpcafile);
            //Trace.WriteLine("Projecting keypoints into lower dimension...");
            //detector.ProjectKeypoints(grayModelImageA, keypointsA);
            //detector.ProjectKeypoints(grayModelImageB, keypointsB);
            //Trace.WriteLine("Keypoints projected.");


            Imaging.Image model = Imaging.Image.Load(imageFileA);
            Imaging.Image data = Imaging.Image.Load(imageFileB);

            List<PCA_SIFTFeature> featuresA = new PCA_SIFTDetector().Detect(model, null).Cast<PCA_SIFTFeature>().ToList();
            List<PCA_SIFTFeature> featuresB = new PCA_SIFTDetector().Detect(data, null).Cast<PCA_SIFTFeature>().ToList();
            PCA_KeypointDetector detector = new PCA_KeypointDetector(gpcafile);
            detector.ProjectKeypoints(model, featuresA);
            detector.ProjectKeypoints(data, featuresB);

            Mat descriptorsA = ToDescriptorMatrix(featuresA);
            Mat descriptorsB = ToDescriptorMatrix(featuresB);

            // 3. Return list of keypoints with updated descriptors?
            if (outputFile == null) { outputFile = trainingFile + ".png"; }
            PCA_Match.Match(model.ToEmguGrayscale(), data.ToEmguGrayscale(), featuresA, featuresB, outputFile);
            return 0;
        }
        static Mat ToDescriptorMatrix(List<PCA_SIFTFeature> features)
        {
            Matrix<float> res = new Matrix<float>(features.Count, features[0].Descriptor.Length);
            float[,] data = res.Data;
            int i, j;
            for (i = 0; i < features.Count; i++)
            {
                float[] d = ((FeatureDescriptor<float>)features[i].Descriptor).Data;
                for (j = 0; j < d.Length; j++)
                {
                    data[i, j] = d[j];
                    if (float.IsNaN(d[j]))
                    {
                        Trace.WriteLine("nan");
                    }
                }
            }
            return res.Mat;
        }

        void dfghdfgh()
        {
            //List<ImageFeature> features = new SIFTDetector().Detect(null, null).ToList();
            //PCA_Something.ComputeDescriptors(Imaging.Image.Load(imageFileA).ToEmguGrayscale(), features);
        }
    }
}
