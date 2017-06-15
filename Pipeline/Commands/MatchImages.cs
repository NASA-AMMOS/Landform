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

            Image<Gray, byte> modelImageA = Imaging.Image.Load(imageFileA).ToEmguGrayscale();
            Image<Gray, float> grayModelImageA = modelImageA.Convert<Gray, float>();

            Image<Gray, byte> modelImageB = Imaging.Image.Load(imageFileB).ToEmguGrayscale();
            Image<Gray, float> grayModelImageB = modelImageB.Convert<Gray, float>();

            string gpcafile = options.TrainingFile;
            string gradfile = "C:\\Users\\charchut\\Downloads\\grads.txt";

            // 0. Computes eigenspace from set of training images.
            if (trainingPath != null && trainingFile != null)
            {
                Trace.WriteLine("Training...");
                PCA_Train train = new PCA_Train(trainingFile);
                train.Train(trainingPath);
                Trace.WriteLine("Trained.");
            }
            
            // 1. Calculate keypoints of an image using SIFT detection
            SIFT siftCPU = new SIFT();
            MKeyPoint[] mKeypointsA = siftCPU.Detect(modelImageA);
            MKeyPoint[] mKeypointsB = siftCPU.Detect(modelImageB);
            List<PCA_Keypoint> keypointsA = PCA_KeypointDetector.GetPatches(grayModelImageA, mKeypointsA, patchsize);

            VectorOfKeyPoint vokp = new VectorOfKeyPoint(mKeypointsA);
            //Matrix<float> mda = new Matrix<float>(1, 1);
            //MKeyPoint[] mKeypointsB = 
            //siftCPU.DetectAndCompute(modelImageB, null, mkpa, mda, false);
            //VectorOfKeyPoint mkpb = new VectorOfKeyPoint();
            //Matrix<float> mdb = new Matrix<float>(1, 1);
            //MKeyPoint[] mKeypointsB = 
            //siftCPU.DetectAndCompute(modelImageB, null, mkpb, mdb, false);
            List<PCA_Keypoint> keypointsB = PCA_KeypointDetector.GetPatches(grayModelImageB, mKeypointsB, patchsize);
            //PCA_KeypointDetector.WritePatchesToFile(keypoints, "C:\\Users\\charchut\\Downloads\\patches.txt", patchsize);
            //PCA_KeypointDetector.WriteGradientsToFile(keypoints, gradfile);

            VectorOfKeyPoint vokp1 = new VectorOfKeyPoint(mKeypointsB);
            

            // 2. Recalculate keypoints, given the eigenspace, an image, and its keypoints
            //// e.g. ./recalckeys gpcavects.txt image1.pgm image2.pgm image1.lkeys image1.pkeys    
            PCA_KeypointDetector detector = new PCA_KeypointDetector(gpcafile);
            Trace.WriteLine("Projecting keypoints into lower dimension...");
            detector.ProjectKeypoints(grayModelImageA, keypointsA);
            detector.ProjectKeypoints(grayModelImageB, keypointsB);
            Trace.WriteLine("Keypoints projected.");

            Mat descriptorsA = ToDescriptorMatrix(keypointsA);
            Mat descriptorsB = ToDescriptorMatrix(keypointsB);


            // 3. Return list of keypoints with updated descriptors?
            PCA_Match.Match(modelImageA, modelImageB, descriptorsA, vokp, descriptorsB, vokp1, outputFile);
            //PCA_Match.Match(modelImageA, modelImageB, keypointsA, keypointsB, outputFile);
            return 0;
        }
        static Mat ToDescriptorMatrix(List<PCA_Keypoint> features)
        {
            Matrix<float> res = new Matrix<float>(features.Count, features[0].desc.Length);
            float[,] data = res.Data;
            int i, j;
            for (i = 0; i < features.Count; i++)
            {
                var d = features[i].desc;
                for (j = 0; j < d.Length; j++)
                {
                    data[i, j] = d[j];
                }
            }
            return res.Mat;
        }
    }
}
